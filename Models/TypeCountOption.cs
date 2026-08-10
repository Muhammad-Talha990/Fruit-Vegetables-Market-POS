namespace FruitVegetableMarketPOS.Models
{
    /// <summary>Dropdown option for how many types (qism) to set — Type 1…Type 10.</summary>
    public class TypeCountOption
    {
        public int Count { get; init; }
        public string Label => $"Type {Count} / قسم {Count}";
    }
}
