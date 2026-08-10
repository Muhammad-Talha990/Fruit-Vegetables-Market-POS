using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FruitVegetableMarketPOS.Models
{
    /// <summary>One type row in the add-to-cart dialog with its own quantity (starts at 0).</summary>
    public class TypeQtyRow : INotifyPropertyChanged
    {
        public ItemType Type { get; init; } = new();

        public string TypeName => Type.TypeName;
        public double Price => Type.Price;
        public string PriceDisplay => $"Rs.{Price:N0}";

        private int _quantity;
        public int Quantity
        {
            get => _quantity;
            set
            {
                var v = value < 0 ? 0 : value;
                if (_quantity == v) return;
                _quantity = v;
                OnPropertyChanged();
                OnPropertyChanged(nameof(QuantityText));
            }
        }

        public string QuantityText
        {
            get => _quantity.ToString();
            set
            {
                if (!int.TryParse((value ?? string.Empty).Trim(), out var n) || n < 0)
                    n = 0;
                Quantity = n;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
