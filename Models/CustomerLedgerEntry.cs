using System;

namespace GroceryPOS.Models
{
    /// <summary>
    /// Represents a single entry in the Customer Ledger (Sale, Payment, or Return).
    /// Used for chronological audit trails and running balance calculations.
    /// </summary>
    public class CustomerLedgerEntry
    {
        public int LedgerId { get; set; }

        public int CustomerId { get; set; }
        public Customer? Customer { get; set; }

        /// <summary>Chronological order of the entry.</summary>
        public DateTime EntryDate { get; set; } = DateTime.Now;

        /// <summary>Type of entry: SALE, PAYMENT, RETURN, ADJUSTMENT.</summary>
        public string Type { get; set; } = "SALE";

        /// <summary>Optional normalized transaction type for statement rendering.</summary>
        public string TransactionType { get; set; } = "SALE";

        /// <summary>Invoice #, Receipt #, or Payment ID for reference.</summary>
        public string? ReferenceId { get; set; }

        /// <summary>Source table name for audit traceability.</summary>
        public string? SourceTable { get; set; }

        /// <summary>Source row ID for audit traceability.</summary>
        public long? SourceId { get; set; }

        public int? BillId { get; set; }
        public int? ReturnId { get; set; }
        public int? PaymentId { get; set; }
        public int? CreatedByUserId { get; set; }
        public int SequenceNo { get; set; }

        /// <summary>Friendly description of the transaction.</summary>
        public string? Description { get; set; }

        /// <summary>Amount increasing the customer's liability (e.g., Sale total).</summary>
        public double Debit { get; set; }

        /// <summary>Amount decreasing the customer's liability (e.g., Payment or Return).</summary>
        public double Credit { get; set; }

        /// <summary>Total outstanding balance AFTER this entry.</summary>
        public double RunningBalance { get; set; }

        /// <summary>Net amount impact where Debit is positive and Credit is negative.</summary>
        public double NetAmount => Math.Round(Debit - Credit, 2);

        public bool IsDebit => Debit > 0;
        public bool IsCredit => Credit > 0;

        // UI Helpers
        public bool IsSale => string.Equals(TransactionType, "SALE", StringComparison.OrdinalIgnoreCase) || string.Equals(Type, "SALE", StringComparison.OrdinalIgnoreCase);
        public bool IsPayment => string.Equals(TransactionType, "PAYMENT", StringComparison.OrdinalIgnoreCase) || string.Equals(Type, "PAYMENT", StringComparison.OrdinalIgnoreCase) || string.Equals(TransactionType, "RECOVERY", StringComparison.OrdinalIgnoreCase);
        public bool IsReturn => string.Equals(TransactionType, "RETURN", StringComparison.OrdinalIgnoreCase) || string.Equals(Type, "RETURN", StringComparison.OrdinalIgnoreCase);

        public string ColorBrush => (TransactionType ?? Type) switch
        {
            "SALE"    => "#60A5FA", // Blue-400
            "PAYMENT" => "#22C55E", // Green-500
            "RECOVERY"=> "#22C55E",
            "RETURN"  => "#EF4444", // Red-500
            _         => "#94A3B8"  // Slate-400
        };
    }
}
