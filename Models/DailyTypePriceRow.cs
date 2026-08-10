using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FruitVegetableMarketPOS.Models
{
    /// <summary>One price row when adding an item to today's list (Type N / قسم N).</summary>
    public class DailyTypePriceRow : INotifyPropertyChanged
    {
        public int Index { get; set; }

        public string Label => $"Type {Index} · قسم {Index}";

        private string _priceText = string.Empty;
        public string PriceText
        {
            get => _priceText;
            set
            {
                if (_priceText == value) return;
                _priceText = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
