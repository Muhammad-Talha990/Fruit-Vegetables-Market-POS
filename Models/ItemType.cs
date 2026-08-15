using System;

namespace FruitVegetableMarketPOS.Models
{
    /// <summary>
    /// A price/type variant for an item (e.g. Type 1, Type 2).
    /// Maps to the ItemTypes table.
    /// </summary>
    public class ItemType
    {
        public int TypeId { get; set; }
        public int ItemId { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public double Price { get; set; }
        /// <summary>Optional note for this type/price variant (shown when selecting from menu).</summary>
        public string? Note { get; set; }
        public int SortOrder { get; set; } = 1;
        public bool IsActive { get; set; } = true;
        public DateTime? CreatedAt { get; set; }

        public bool HasNote => !string.IsNullOrWhiteSpace(Note);
    }
}
