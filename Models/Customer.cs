using System;
using System.Collections.Generic;

namespace GroceryPOS.Models
{
    /// <summary>
    /// Represents a customer in the system.
    /// Maps to the "Customers" table in the normalized 3NF schema.
    /// </summary>
    public class Customer
    {
        public int CustomerId { get; set; }

        /// <summary>Full name of the customer.</summary>
        public string FullName { get; set; } = string.Empty;

        /// <summary>Backward-compat alias — maps to FullName.</summary>
        public string Name { get => FullName; set => FullName = value; }

        /// <summary>Main phone number.</summary>
        public string Phone { get; set; } = string.Empty;

        /// <summary>Backward-compat alias — maps to Phone.</summary>
        public string PrimaryPhone { get => Phone; set => Phone = value; }

        /// <summary>Deprecated: mapped to null for now or consolidation.</summary>
        public string? SecondaryPhone { get; set; }

        /// <summary>Full address.</summary>
        public string? Address { get; set; }

        /// <summary>Deprecated: use Address.</summary>
        public string? Address2 { get; set; }
        /// <summary>Deprecated: use Address.</summary>
        public string? Address3 { get; set; }

        /// <summary>False = soft-deleted. Only active customers appear in billing search.</summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // ── Calculated Properties ──
        public int BillCount { get; set; }
        public string BillCountDisplay => BillCount == 1 ? "1 bill" : $"{BillCount} bills";
        public double TotalAmount { get; set; }
        public DateTime? LastVisitDate { get; set; }
        
        /// <summary>Dynamic serial number for UI display.</summary>
        public int SerialNumber { get; set; }

        /// <summary>
        /// Total outstanding credit (sum of RemainingAmount on unpaid bills).
        /// Populated by queries — not stored in DB.
        /// </summary>
        public double PendingCredit { get; set; }

        /// <summary>Display helper for status badge.</summary>
        public string StatusLabel => IsActive ? "Active" : "Inactive";

        /// <summary>Display helper — pending credit formatted.</summary>
        public string PendingCreditDisplay =>
            PendingCredit > 0 ? $"Rs. {PendingCredit:N0}" : "—";
    }
}
