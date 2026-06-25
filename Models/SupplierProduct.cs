using System;

namespace GroceryPOS.Models
{
    public class SupplierProduct
    {
        public int Id { get; set; }
        public string SupplierPhone { get; set; } = string.Empty;
        public int ProductId { get; set; }
        public double? SupplyPrice { get; set; }
        public DateTime SupplyDate { get; set; } = DateTime.Now;
        public string? Notes { get; set; }

        // Navigation/Display properties
        public string? ProductName { get; set; }
        public string? SupplierName { get; set; }
    }
}
