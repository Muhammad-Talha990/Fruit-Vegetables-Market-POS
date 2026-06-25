using System;

namespace GroceryPOS.Models
{
    public class Supplier
    {
        public string PhoneNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? CompanyName { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Helper for display
        public string DisplayName => string.IsNullOrEmpty(CompanyName) ? Name : $"{Name} ({CompanyName})";
    }
}
