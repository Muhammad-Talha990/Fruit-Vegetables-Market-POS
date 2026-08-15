namespace FruitVegetableMarketPOS.Models
{
    /// <summary>Dropdown option for how many types (qism) to set — 1…10.</summary>
    public class TypeCountOption
    {
        public int Count { get; init; }
        public string Label => Count.ToString();
    }
}
