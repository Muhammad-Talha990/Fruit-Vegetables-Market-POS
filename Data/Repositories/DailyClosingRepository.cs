using System;
using Microsoft.Data.Sqlite;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Data.Repositories
{
    public class DailyClosingRepository
    {
        public DailyClosing? GetByDate(string businessDate)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM DailyClosing WHERE BusinessDate = @date;";
            cmd.Parameters.AddWithValue("@date", businessDate);

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapClosing(reader) : null;
        }

        public bool IsClosed(string businessDate)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*) FROM DailyClosing
                WHERE BusinessDate = @date AND Status = 'Closed';";
            cmd.Parameters.AddWithValue("@date", businessDate);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        public DailyClosing Upsert(DailyClosing closing)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO DailyClosing (
                    BusinessDate, TotalBills, TotalSales, CashSales, CardSales, OnlineSales,
                    CreditSales, CreditRecovered, Refunds, NetSales, ClosedAt, ClosedByUserId, Status, Notes)
                VALUES (
                    @date, @totalBills, @totalSales, @cash, @card, @online,
                    @credit, @recovered, @refunds, @net, @closedAt, @closedBy, @status, @notes)
                ON CONFLICT(BusinessDate) DO UPDATE SET
                    TotalBills      = excluded.TotalBills,
                    TotalSales      = excluded.TotalSales,
                    CashSales       = excluded.CashSales,
                    CardSales       = excluded.CardSales,
                    OnlineSales     = excluded.OnlineSales,
                    CreditSales     = excluded.CreditSales,
                    CreditRecovered = excluded.CreditRecovered,
                    Refunds         = excluded.Refunds,
                    NetSales        = excluded.NetSales,
                    ClosedAt        = excluded.ClosedAt,
                    ClosedByUserId  = excluded.ClosedByUserId,
                    Status          = excluded.Status,
                    Notes           = excluded.Notes;
                SELECT DailyClosingId FROM DailyClosing WHERE BusinessDate = @date;";
            BindClosingParameters(cmd, closing);
            closing.DailyClosingId = Convert.ToInt32(cmd.ExecuteScalar());
            return closing;
        }

        public DailyClosing CloseDay(DailyClosing closing)
        {
            closing.Status = "Closed";
            closing.ClosedAt ??= DateTimeHelper.CaptureTransactionTime();
            return Upsert(closing);
        }

        private static void BindClosingParameters(SqliteCommand cmd, DailyClosing closing)
        {
            cmd.Parameters.AddWithValue("@date", closing.BusinessDate);
            cmd.Parameters.AddWithValue("@totalBills", closing.TotalBills);
            cmd.Parameters.AddWithValue("@totalSales", closing.TotalSales);
            cmd.Parameters.AddWithValue("@cash", closing.CashSales);
            cmd.Parameters.AddWithValue("@card", closing.CardSales);
            cmd.Parameters.AddWithValue("@online", closing.OnlineSales);
            cmd.Parameters.AddWithValue("@credit", closing.CreditSales);
            cmd.Parameters.AddWithValue("@recovered", closing.CreditRecovered);
            cmd.Parameters.AddWithValue("@refunds", closing.Refunds);
            cmd.Parameters.AddWithValue("@net", closing.NetSales);
            cmd.Parameters.AddWithValue("@closedAt", closing.ClosedAt.HasValue ? (object)closing.ClosedAt.Value.ToDbString() : DBNull.Value);
            cmd.Parameters.AddWithValue("@closedBy", closing.ClosedByUserId.HasValue ? (object)closing.ClosedByUserId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@status", closing.Status);
            cmd.Parameters.AddWithValue("@notes", (object?)closing.Notes ?? DBNull.Value);
        }

        private static DailyClosing MapClosing(SqliteDataReader reader)
        {
            var closedAtOrd = reader.GetOrdinal("ClosedAt");
            var closedByOrd = reader.GetOrdinal("ClosedByUserId");
            var notesOrd = reader.GetOrdinal("Notes");

            return new DailyClosing
            {
                DailyClosingId  = reader.GetInt32(reader.GetOrdinal("DailyClosingId")),
                BusinessDate    = reader.GetString(reader.GetOrdinal("BusinessDate")),
                TotalBills      = reader.GetInt32(reader.GetOrdinal("TotalBills")),
                TotalSales      = reader.GetDouble(reader.GetOrdinal("TotalSales")),
                CashSales       = reader.GetDouble(reader.GetOrdinal("CashSales")),
                CardSales       = reader.GetDouble(reader.GetOrdinal("CardSales")),
                OnlineSales     = reader.GetDouble(reader.GetOrdinal("OnlineSales")),
                CreditSales     = reader.GetDouble(reader.GetOrdinal("CreditSales")),
                CreditRecovered = reader.GetDouble(reader.GetOrdinal("CreditRecovered")),
                Refunds         = reader.GetDouble(reader.GetOrdinal("Refunds")),
                NetSales        = reader.GetDouble(reader.GetOrdinal("NetSales")),
                ClosedAt        = reader.IsDBNull(closedAtOrd) ? null : reader.GetDateTime(closedAtOrd),
                ClosedByUserId  = reader.IsDBNull(closedByOrd) ? null : reader.GetInt32(closedByOrd),
                Status          = reader.GetString(reader.GetOrdinal("Status")),
                Notes           = reader.IsDBNull(notesOrd) ? null : reader.GetString(notesOrd)
            };
        }
    }
}
