using System;

namespace GroceryPOS.Models
{
    /// <summary>
    /// Represents a return transaction for a specific product from an original bill.
    /// Maps to "BILL_RETURNS" table.
    /// </summary>
    public class BillReturn
    {
        /// <summary>Database Internal ID.</summary>
        public int Id { get; set; }

        /// <summary>Foreign Key to the original bill.</summary>
        public int BillId { get; set; }

        /// <summary>Display ID for return (e.g., Return 1).</summary>
        public string? ReturnBillId { get; set; }

        /// <summary>Total amount refunded or adjusted in this transaction.</summary>
        public double RefundAmount { get; set; }

        /// <summary>The date of the return transaction.</summary>
        public DateTime ReturnedAt { get; set; } = DateTime.Now;

        /// <summary>Compatibility shim for old code.</summary>
        public string ReturnDate { get => ReturnedAt.ToString("yyyy-MM-dd HH:mm:ss"); set => ReturnedAt = DateTime.TryParse(value, out var d) ? d : DateTime.Now; }

        /// <summary>The product barcode (deprecated in 3NF, but kept for shim).</summary>
        public string ProductId { get; set; } = string.Empty;

        /// <summary>Quantity being returned (total for this transaction).</summary>
        public int ReturnQuantity { get; set; }

        /// <summary>Unit price of the returned item.</summary>
        public double UnitPrice { get; set; }

        /// <summary>Item description, helpful for UI.</summary>
        public string? ProductDescription { get; set; }

        /// <summary>Computed total amount (UnitPrice × ReturnQuantity).</summary>
        public double TotalAmount => UnitPrice * ReturnQuantity;

        /// <summary>Amount issued as store credit (not yet refunded in cash/online).</summary>
        public double StoreCreditIssued { get; set; } = 0;

        /// <summary>When the store credit was refunded (if applicable).</summary>
        public DateTime? StoreCreditRefundedAt { get; set; }
    }
}
