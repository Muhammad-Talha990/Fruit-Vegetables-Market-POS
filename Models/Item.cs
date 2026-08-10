using System;
using System.Collections.Generic;

namespace FruitVegetableMarketPOS.Models
{
    /// <summary>
    /// Catalog product for Fruit &amp; Vegetable Market POS.
    /// Unit prices live on ItemTypes (set daily via Billing → Add Today), not on Items.
    /// </summary>
    public class Item
    {
        /// <summary>Database Internal ID (Primary Key).</summary>
        public int Id { get; set; }

        /// <summary>Product Barcode / POS code (optional, unique if provided).</summary>
        public string? Barcode { get; set; }

        /// <summary>String representation of the database Id – used as the canonical product identifier.</summary>
        public string ItemId
        {
            get => Id.ToString();
            set { if (int.TryParse(value, out var v)) Id = v; }
        }

        /// <summary>Product description / name.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Urdu product name for bilingual POS display.</summary>
        public string? NameUrdu { get; set; }

        /// <summary>Combined English + Urdu label for receipts and lists.</summary>
        public string DisplayName =>
            !string.IsNullOrWhiteSpace(NameUrdu) ? $"{Description} ({NameUrdu})" : Description;

        /// <summary>Simple POS code (1, 2, 3…) stored in Barcode for quick add / scan.</summary>
        public string PosCode =>
            !string.IsNullOrWhiteSpace(Barcode) ? Barcode.Trim() : Id.ToString();

        /// <summary>Bilingual label for dropdowns — includes POS code.</summary>
        public string DisplayLabel =>
            !string.IsNullOrWhiteSpace(NameUrdu)
                ? $"#{PosCode} · {Description} / {NameUrdu}"
                : $"#{PosCode} · {Description}";

        /// <summary>Foreign Key linking to Categories table.</summary>
        public int? CategoryId { get; set; }

        /// <summary>Joined Category Name from Categories table.</summary>
        public string? CategoryName { get; set; }

        /// <summary>Compatibility shim for old code.</summary>
        public string? ItemCategory { get => CategoryName; set => CategoryName = value; }

        /// <summary>Soft-delete flag.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>Last update timestamp.</summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>Price/type variants loaded on demand (daily unit prices).</summary>
        public List<ItemType> Types { get; set; } = new();
    }
}
