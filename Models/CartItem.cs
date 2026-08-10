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

        /// <summary>English / Urdu title for the cart item column.</summary>
        public string DisplayName =>
            string.IsNullOrWhiteSpace(NameUrdu)
                ? ItemDescription
                : $"{ItemDescription} / {NameUrdu}";

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
        /// <summary>Piece quantity in cart (unit price × qty).</summary>
        public double Quantity
        {
            get => _quantity;
            set
            {
                var next = value < 1 ? 1 : Math.Round(value, 3);
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
