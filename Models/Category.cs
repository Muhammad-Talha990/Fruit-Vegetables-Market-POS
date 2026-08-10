namespace FruitVegetableMarketPOS.Models
{
    /// <summary>
    /// Product category for grouping items on the POS.
    /// Maps to the Categories table.
    /// </summary>
    public class Category
    {
        public int CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? NameUrdu { get; set; }
        public string? IconPath { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>Bilingual label for category chips, e.g. "Fruits / پھل".</summary>
        public string DisplayLabel =>
            !string.IsNullOrWhiteSpace(NameUrdu) ? $"{Name} / {NameUrdu}" : Name;

        /// <summary>POS filter chip: Fruits (پھل), Vegetables (سبزی).</summary>
        public string ChipLabel => Name switch
        {
            "Fruits" => "Fruits (پھل)",
            "Vegetables" => "Vegetables (سبزی)",
            _ => !string.IsNullOrWhiteSpace(NameUrdu) ? $"{Name} ({NameUrdu})" : Name
        };
    }
}
