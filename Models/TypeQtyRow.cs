using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace FruitVegetableMarketPOS.Models
{
    /// <summary>One price row in the add-to-cart dialog with its own quantity (starts at 0).</summary>
    public class TypeQtyRow : INotifyPropertyChanged
    {
        public ItemType Type { get; init; } = new();

        /// <summary>English / Urdu item name shown in the quantity picker (not the type label).</summary>
        public string ItemDisplayName { get; init; } = string.Empty;

        public string TypeName => Type.TypeName;
        public double Price => Type.Price;
        public string PriceDisplay => $"Rs.{Price:N0}";
        public string? Note => Type.Note;
        public bool HasNote => !string.IsNullOrWhiteSpace(Note);

        private double _quantity;
        private string _quantityText = "0";

        public double Quantity
        {
            get => _quantity;
            set
            {
                var v = value < 0 ? 0 : Math.Round(value, 3);
                if (Math.Abs(_quantity - v) < 0.0001)
                {
                    // Keep text in sync after +/- even if value unchanged after clamp
                    var formatted = FormatQty(v);
                    if (_quantityText != formatted)
                    {
                        _quantityText = formatted;
                        OnPropertyChanged(nameof(QuantityText));
                    }
                    return;
                }

                _quantity = v;
                _quantityText = FormatQty(v);
                OnPropertyChanged();
                OnPropertyChanged(nameof(QuantityText));
            }
        }

        public string QuantityText
        {
            get => _quantityText;
            set
            {
                var raw = value ?? string.Empty;
                if (_quantityText == raw) return;
                _quantityText = raw;
                OnPropertyChanged();

                var trimmed = raw.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    _quantity = 0;
                    OnPropertyChanged(nameof(Quantity));
                    return;
                }

                if (TryParseQty(trimmed, out var n))
                {
                    if (n < 0) n = 0;
                    n = Math.Round(n, 3);
                    if (Math.Abs(_quantity - n) >= 0.0001)
                    {
                        _quantity = n;
                        OnPropertyChanged(nameof(Quantity));
                    }
                }
            }
        }

        private static string FormatQty(double value) =>
            value.ToString("0.###", CultureInfo.CurrentCulture);

        private static bool TryParseQty(string raw, out double n)
        {
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out n))
                return true;
            return double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out n);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
