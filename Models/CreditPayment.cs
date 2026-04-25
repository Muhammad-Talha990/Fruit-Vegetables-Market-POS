using System;

namespace GroceryPOS.Models
{
    /// <summary>
    /// Represents one payment installment against a credit (udhar) bill.
    /// Maps to the "CreditPayments" table.
    /// </summary>
    public class CreditPayment
    {
        public int PaymentId { get; set; }
        public int BillId { get; set; }

        /// <summary>Amount paid in this installment.</summary>
        public double AmountPaid { get; set; }

        /// <summary>When this payment was recorded.</summary>
        public DateTime PaidAt { get; set; } = DateTime.Now;

        /// <summary>Payment method used (Cash or Online).</summary>
        public string PaymentMethod { get; set; } = "Cash";

        /// <summary>Type of transaction (Payment or Refund).</summary>
        public string TransactionType { get; set; } = "Payment";

        /// <summary>Optional cashier note for this payment.</summary>
        public string? Note { get; set; }

        public string DisplayNote
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Note)) return Note;
                if (TransactionType == "Payment") return "payment received";
                if (TransactionType == "Refund") return "cash refunded";
                return string.Empty;
            }
        }
    }
}
