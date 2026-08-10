using System;

namespace FruitVegetableMarketPOS.Models
{
    /// <summary>
    /// Represents a system user (admin or cashier).
    /// Maps to the "User" table in SQLite.
    /// </summary>
    public class User
    {
        public int Id { get; set; }

        public string Username { get; set; } = string.Empty;

        public string PasswordHash { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        /// <summary>"Admin" or "Cashier"</summary>
        public string Role { get; set; } = "Cashier";

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
