using System;

namespace FruitVegetableMarketPOS.Models
{
    /// <summary>
    /// Today's selling menu row (normalized).
    /// Description comes from Items; Type / Sale come from ItemTypes + BillItems (see DailyItemSetRow).
    /// </summary>
    public class DailyItemSelection
    {
        public int DailySelectionId { get; set; }
        public string BusinessDate { get; set; } = string.Empty;
        public int ItemId { get; set; }

        /// <summary>False = deactivated for today (still shown faded on the grid).</summary>
        public bool IsAvailable { get; set; } = true;

        /// <summary>Joined from Items — not stored on DailyItemSelection.</summary>
        public string? ItemDescription { get; set; }

        /// <summary>Joined Urdu name from Items.</summary>
        public string? ItemNameUrdu { get; set; }
    }

    /// <summary>
    /// Normalized daily read model: ItemId + Description + Type + Sale qty.
    /// Backed by view DailyItemSet (or equivalent query).
    /// </summary>
    public class DailyItemSetRow
    {
        public string BusinessDate { get; set; } = string.Empty;
        public int ItemId { get; set; }
        public string ItemDescription { get; set; } = string.Empty;
        public string Type { get; set; } = "Type 1";
        public double Sale { get; set; }

        public string SaleDisplay => Sale.ToString("N0");
    }
}
