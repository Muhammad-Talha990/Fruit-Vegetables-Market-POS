namespace FruitVegetableMarketPOS.Models
{
    /// <summary>
    /// Represents an additional phone number for a customer.
    /// Maps to the "CustomerPhones" table.
    /// </summary>
    public class CustomerPhone
    {
        public int PhoneId { get; set; }
        public int CustomerId { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
    }
}
