using System;
using System.ComponentModel;

namespace FruitVegetableMarketPOS.Models
{
    /// <summary>
    /// Represents a single item in the billing cart.
    /// </summary>
    public class CartItem : INotifyPropertyChanged
    {
        /// <summary>Product ID (FK to Item.Id).</summary>
        public string ItemId { get; set; } = string.Empty;

        /// <summary>Selected ItemType ID (FK to ItemTypes.TypeId).</summary>
        public int? TypeId { get; set; }

        /// <summary>Selected type name for display.</summary>
        public string? TypeName { get; set; }

        /// <summary>Sold as pieces (no KG / weight unit).</summary>
        public string Unit { get; set; } = "piece";

        /// <summary>Product barcode for display (may be empty).</summary>
        public string? Barcode { get; set; }

        /// <summary>Item description for display.</summary>
        public string ItemDescription { get; set; } = string.Empty;

        /// <summary>Urdu name for display on the cart line.</summary>
        public string? NameUrdu { get; set; }

        /// <summary>English / Urdu title for the cart item column (item name only, no type).</summary>
        public string DisplayName
        {
            get
            {
                var name = (ItemDescription ?? string.Empty).Trim();
                var dashType = name.IndexOf(" - Type ", StringComparison.OrdinalIgnoreCase);
                if (dashType > 0)
                    name = name.Substring(0, dashType).Trim();

                var dashGeneric = name.LastIndexOf(" - ", StringComparison.Ordinal);
                if (dashGeneric > 0 && (name.IndexOf("Type", dashGeneric, StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("قسم", dashGeneric, StringComparison.OrdinalIgnoreCase) >= 0))
                    name = name.Substring(0, dashGeneric).Trim();

                if (name.Contains(" / ", StringComparison.Ordinal))
                    return name;

                return string.IsNullOrWhiteSpace(NameUrdu)
                    ? name
                    : $"{name} / {NameUrdu.Trim()}";
            }
        }

        private double _unitPrice;
        /// <summary>Unit price from the selected type (Type N price).</summary>
        public double UnitPrice
        {
            get => _unitPrice;
            set
            {
                if (Math.Abs(_unitPrice - value) < 0.0001) return;
                _unitPrice = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UnitPrice)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalPrice)));
            }
        }

        private double _quantity = 1;
        /// <summary>Quantity in cart (supports decimals, e.g. 0.5 / 1.25).</summary>
        public double Quantity
        {
            get => _quantity;
            set
            {
                var next = value <= 0 ? 0.001 : Math.Round(value, 3);
                if (Math.Abs(_quantity - next) < 0.0001) return;
                _quantity = next;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Quantity)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalPrice)));
            }
        }

        /// <summary>Line total: UnitPrice × Quantity.</summary>
        public double TotalPrice => UnitPrice * Quantity;

        private bool _isCopied;
        /// <summary>Indicates if this item was copied from a previous bill.</summary>
        public bool IsCopied
        {
            get => _isCopied;
            set
            {
                if (_isCopied != value)
                {
                    _isCopied = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCopied)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
