using System;
using System.Collections.Generic;
using System.Linq;

namespace GroceryPOS.Models
{
    /// <summary>
    /// Represents a stock purchase transaction (master record).
    /// Maps to the StockPurchases table.
    /// A single purchase can contain multiple products (see StockPurchaseItems).
    /// </summary>
    public class StockPurchase
    {
        /// <summary>Auto-generated primary key.</summary>
        public int PurchaseId { get; set; }

        /// <summary>
        /// Timestamp captured ONCE at the start of the purchase transaction.
        /// All related InventoryLog entries share this same timestamp.
        /// </summary>
        public DateTime PurchaseAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Total cash value of the purchase (sum of Qty × CostPrice for all items).
        /// This amount is DEDUCTED from Cash in Drawer.
        /// </summary>
        public double TotalAmount { get; set; }

        /// <summary>Optional: the user who recorded the purchase.</summary>
        public int? CreatedByUserId { get; set; }

        /// <summary>Optional: path to the physical supplier bill image.</summary>
        public string? ImagePath { get; set; }

        /// <summary>Line items included in this purchase.</summary>
        public List<StockPurchaseItem> Items { get; set; } = new();

        // ── Calculated helpers ──
        /// <summary>Recomputes TotalAmount from Items list.</summary>
        public void RecalculateTotal()
        {
            TotalAmount = Math.Round(Items.Sum(i => i.LineTotal), 2);
        }
    }
}
