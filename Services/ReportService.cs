using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using GroceryPOS.Data;
using GroceryPOS.Data.Repositories;
using GroceryPOS.Helpers;
using GroceryPOS.Models;

namespace GroceryPOS.Services
{
    /// <summary>
    /// Reporting helper DTO for product-wise reports.
    /// </summary>
    public class ReportItem
    {
        public string ItemDescription { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
        public int QuantitySold { get; set; }
        public double TotalRevenue { get; set; }
    }

    /// <summary>
    /// Report generation service using raw SQL queries against Bill/BillDescription tables.
    /// Supports daily, monthly, and product-wise reports.
    /// </summary>
    public class ReportService
    {
        private readonly BillRepository _billRepo;

        public ReportService(BillRepository billRepo)
        {
            _billRepo = billRepo;
        }

        /// <summary>Delegates straight to repository for custom range sales.</summary>
        public List<Bill> GetByDateRange(DateTime from, DateTime to) => _billRepo.GetByDateRange(from, to);

        /// <summary>Gets all bills for a specific date.</summary>
        public List<Bill> GetDailyReport(DateTime date)
        {
            var start = date.Date;
            var end = start.AddDays(1);
            return _billRepo.GetByDateRange(start, end);
        }

        /// <summary>Gets all bills for a week starting from the given date (Monday-based).</summary>
        public List<Bill> GetWeeklyReport(DateTime date)
        {
            // Calculate start of week (Monday)
            int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
            var from = date.AddDays(-1 * diff).Date;
            var to = from.AddDays(7);
            return _billRepo.GetByDateRange(from, to);
        }

        /// <summary>Gets all bills for a specific month.</summary>
        public List<Bill> GetMonthlyReport(int year, int month)
        {
            var start = new DateTime(year, month, 1);
            var end = start.AddMonths(1);
            return _billRepo.GetByDateRange(start, end);
        }

        /// <summary>Gets product-wise sales summary for a date range.</summary>
        public List<ReportItem> GetProductWiseReport(DateTime from, DateTime to)
        {
            var reportItems = new List<ReportItem>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    bd.ItemId,
                    COALESCE(i.Description, 'Unknown') AS ItemDesc,
                    SUM(bd.Quantity)   AS TotalQty,
                    SUM(bd.Quantity * bd.UnitPrice - bd.DiscountAmount) AS TotalRevenue
                FROM BillItems bd
                INNER JOIN Bills b ON bd.BillId = b.BillId
                LEFT  JOIN Items i ON bd.ItemId = i.ItemId
                WHERE datetime(b.CreatedAt, 'localtime') >= @from AND datetime(b.CreatedAt, 'localtime') < @to
                GROUP BY bd.ItemId, ItemDesc
                ORDER BY TotalRevenue DESC;
            ";
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                reportItems.Add(new ReportItem
                {
                    ItemId          = reader.GetString(reader.GetOrdinal("ItemId")),
                    ItemDescription = reader.GetString(reader.GetOrdinal("ItemDesc")),
                    QuantitySold    = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("TotalQty"))),
                    TotalRevenue    = Convert.ToDouble(reader.GetValue(reader.GetOrdinal("TotalRevenue")))
                });
            }

            return reportItems;
        }

        /// <summary>Gets total revenue for a date range.</summary>
        public double GetTotalRevenue(DateTime from, DateTime to)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(SubTotal), 0) FROM (
                    SELECT (SELECT SUM(Quantity * UnitPrice - DiscountAmount) FROM BillItems WHERE BillId = b.BillId) as SubTotal
                    FROM Bills b
                    WHERE datetime(CreatedAt, 'localtime') >= @from AND datetime(CreatedAt, 'localtime') < @to
                );
            ";
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets total bill count for a date range.</summary>
        public int GetTotalBillCount(DateTime from, DateTime to)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*) FROM Bills
                WHERE datetime(CreatedAt, 'localtime') >= @from AND datetime(CreatedAt, 'localtime') < @to;
            ";
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public string GetDiagnostics() => DatabaseHelper.GetDatabaseDiagnostics();

        /// <summary>Gets sale bills only (no returns) for a date range.</summary>
        public List<Bill> GetSalesOnlyByDateRange(DateTime from, DateTime to) => _billRepo.GetSalesOnlyByDateRange(from, to);

        /// <summary>Gets the total value of all returns in a date range.</summary>
        public double GetReturnsTotalByDateRange(DateTime from, DateTime to) => _billRepo.GetReturnsTotalByDateRange(from, to);

        /// <summary>Gets total outstanding customer credit (all-time).</summary>
        public double GetOutstandingCreditTotal() => _billRepo.GetOutstandingCreditTotal();

        /// <summary>
        /// Returns online payment totals grouped by sub-method (Easypaisa, JazzCash, Bank Transfer)
        /// for the given date range. Useful for the Reports summary panel.
        /// </summary>
        public Dictionary<string, double> GetOnlinePaymentBreakdown(DateTime from, DateTime to)
            => _billRepo.GetOnlinePaymentBreakdown(from, to);

        // ── Analytics for Dashboard ──

        /// <summary>Per-day sales + returns series for a date range.</summary>
        public List<(DateTime Date, double TotalSales, double TotalReturns, int BillCount)>
            GetDailySalesSeries(DateTime from, DateTime to)
            => _billRepo.GetDailySalesSeries(from, to);

        /// <summary>Top N products by revenue in a date range.</summary>
        public List<(string Name, double Revenue, int Qty)>
            GetTopProductsSeries(DateTime from, DateTime to, int topN = 5)
            => _billRepo.GetTopProductsSeries(from, to, topN);

        /// <summary>Payment method revenue breakdown for a date range.</summary>
        public Dictionary<string, double>
            GetPaymentMethodBreakdownForRange(DateTime from, DateTime to)
            => _billRepo.GetPaymentMethodBreakdownForRange(from, to);

        /// <summary>Cashier performance (bill count + revenue) for a date range.</summary>
        public List<(string CashierName, int BillCount, double Revenue)>
            GetCashierPerformance(DateTime from, DateTime to)
            => _billRepo.GetCashierPerformance(from, to);
    }
}
