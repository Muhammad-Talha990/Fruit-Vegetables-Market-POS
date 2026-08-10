using System;

namespace FruitVegetableMarketPOS.Models
{
    /// <summary>
    /// Represents a payment account (Bank, Easypaisa, JazzCash) for receiving online payments.
    /// </summary>
    public class Account
    {
        public int Id { get; set; }
        public string AccountTitle { get; set; } = string.Empty;
        
        /// <summary>
        /// Type of account (e.g., Bank, Easypaisa, JazzCash).
        /// </summary>
        public string AccountType { get; set; } = string.Empty;
        
        public string? BankName { get; set; }
        public string? BranchName { get; set; }
        public string? AccountNumber { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Helper property for UI display (e.g., "Meezan Bank - Main Branch").
        /// </summary>
        public string DisplayName => string.IsNullOrWhiteSpace(BankName) 
            ? $"{AccountTitle} ({AccountType})" 
            : $"{BankName} - {AccountTitle}";
    }
}
