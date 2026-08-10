using System;

namespace FruitVegetableMarketPOS.Models
{
    /// <summary>
    /// End-of-day sales summary for a business date.
    /// Maps to the DailyClosing table.
    /// </summary>
    public class DailyClosing
    {
        public int DailyClosingId { get; set; }
        public string BusinessDate { get; set; } = string.Empty;
        public int TotalBills { get; set; }
        public double TotalSales { get; set; }
        public double CashSales { get; set; }
        public double CardSales { get; set; }
        public double OnlineSales { get; set; }
        public double CreditSales { get; set; }
        public double CreditRecovered { get; set; }
        public double Refunds { get; set; }
        public double NetSales { get; set; }
        public DateTime? ClosedAt { get; set; }
        public int? ClosedByUserId { get; set; }
        public string Status { get; set; } = "Open";
        public string? Notes { get; set; }

        public bool IsClosed =>
            string.Equals(Status, "Closed", StringComparison.OrdinalIgnoreCase);
    }
}
