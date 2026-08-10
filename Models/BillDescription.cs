using System.ComponentModel;
using System.Linq;

namespace FruitVegetableMarketPOS.Models
{
    /// <summary>
    /// Represents a single line item on a bill.
    /// Maps to the "BillItems" table in the normalized 3NF schema.
    /// </summary>
    public class BillDescription : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Database Internal ID (Primary Key).</summary>
        public int BillItemId { get; set; }

        /// <summary>Backward-compat alias.</summary>
        public int Id { get => BillItemId; set => BillItemId = value; }

        /// <summary>Foreign key to Bills table.</summary>
        public int BillId { get; set; }

        /// <summary>Internal Database ID of the Item.</summary>
        public int ItemInternalId { get; set; }

        /// <summary>Product ID as string (= ItemInternalId.ToString()).</summary>
        public string ItemId { get; set; } = string.Empty;

        /// <summary>Product barcode (may be null for items without barcode).</summary>
        public string? Barcode { get; set; }

        private double _quantity;
        /// <summary>Quantity of this item sold.</summary>
        public double Quantity
        {
            get => _quantity;
            set
            {
                if (_quantity != value)
                {
                    _quantity = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Quantity)));
                    TotalPrice = _quantity * UnitPrice - DiscountAmount;
                }
            }
        }

        /// <summary>Unit price at time of sale (frozen for history).</summary>
        public double UnitPrice { get; set; }

        private double _discountAmount;
        /// <summary>Flat discount applied to this specific line item.</summary>
        public double DiscountAmount
        {
            get => _discountAmount;
            set
            {
                if (_discountAmount != value)
                {
                    _discountAmount = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DiscountAmount)));
                    TotalPrice = Quantity * UnitPrice - _discountAmount;
                }
            }
        }

        private double _totalPrice;
        /// <summary>Line total: (Quantity × UnitPrice) - DiscountAmount.</summary>
        public double TotalPrice
        {
            get => _totalPrice;
            set
            {
                if (_totalPrice != value)
                {
                    _totalPrice = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalPrice)));
                }
            }
        }

        /// <summary>Item description, populated by JOIN or lookup.</summary>
        public string ItemDescription { get; set; } = string.Empty;

        /// <summary>Urdu product name for bilingual receipts (optional; looked up if missing).</summary>
        public string? NameUrdu { get; set; }

        /// <summary>Snapshot of item name frozen at sale time (BillItems.ItemName).</summary>
        public string ItemName { get; set; } = string.Empty;

        /// <summary>Selected ItemType ID frozen at sale time.</summary>
        public int? TypeId { get; set; }

        /// <summary>Selected type name frozen at sale time.</summary>
        public string? TypeName { get; set; }

        /// <summary>Unit of measure frozen at sale time.</summary>
        public string Unit { get; set; } = "KG";

        /// <summary>Display name with type for receipts/UI (uses frozen snapshots).</summary>
        public string DisplayName
        {
            get
            {
                var name = !string.IsNullOrWhiteSpace(ItemName) ? ItemName : ItemDescription;
                if (!string.IsNullOrWhiteSpace(TypeName) &&
                    !name.Contains(TypeName, System.StringComparison.OrdinalIgnoreCase))
                    return $"{name} - {TypeName}";
                return name;
            }
        }

        /// <summary>English line for bilingual receipts — item name only (no type).</summary>
        public string ReceiptEnglishName
        {
            get
            {
                var name = !string.IsNullOrWhiteSpace(ItemName) ? ItemName.Trim() : (ItemDescription ?? "").Trim();
                var slash = name.IndexOf(" / ", System.StringComparison.Ordinal);
                if (slash > 0) name = name.Substring(0, slash).Trim();
                var dashType = name.IndexOf(" - Type ", System.StringComparison.OrdinalIgnoreCase);
                if (dashType > 0) name = name.Substring(0, dashType).Trim();
                return name;
            }
        }

        /// <summary>Urdu line for bilingual receipts, e.g. "پیاز - قسم 1" (includes type).</summary>
        public string? ReceiptUrduName
        {
            get
            {
                var nameUr = NameUrdu?.Trim();
                if (string.IsNullOrWhiteSpace(nameUr))
                {
                    var raw = !string.IsNullOrWhiteSpace(ItemName) ? ItemName : ItemDescription;
                    var slash = raw?.IndexOf(" / ", System.StringComparison.Ordinal) ?? -1;
                    if (slash > 0)
                        nameUr = raw!.Substring(slash + 3).Trim();
                }
                if (string.IsNullOrWhiteSpace(nameUr)) return null;

                var type = TypeName?.Trim();
                if (!string.IsNullOrWhiteSpace(type))
                {
                    var slashType = type.IndexOf(" / ", System.StringComparison.Ordinal);
                    if (slashType > 0)
                        return $"{nameUr} - {type.Substring(slashType + 3).Trim()}";

                    var digits = new string(type.Where(char.IsDigit).ToArray());
                    if (!string.IsNullOrEmpty(digits))
                        return $"{nameUr} - قسم {digits}";
                }
                return nameUr;
            }
        }

        /// <summary>Quantity with unit for receipts (e.g. 1.500 KG).</summary>
        public string QuantityDisplay => $"{Quantity:0.###} {Unit}".Trim();
    }
}
