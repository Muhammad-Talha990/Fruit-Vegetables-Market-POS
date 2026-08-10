using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FruitVegetableMarketPOS.Models
{
    /// <summary>
    /// Represents a grouped return transaction for the audit view.
    /// </summary>
    public class ReturnAuditGroup
    {
        public int ReturnId { get; set; }
        public DateTime ReturnedAt { get; set; }
        public double RefundAmount { get; set; }
        public ObservableCollection<BillReturnItemAudit> Items { get; set; } = new();
    }

    public class BillReturnItemAudit
    {
        public string ItemDescription { get; set; } = string.Empty;
        public double Quantity { get; set; }
        public double UnitPrice { get; set; }
        public double TotalPrice => Quantity * UnitPrice;
    }
}
