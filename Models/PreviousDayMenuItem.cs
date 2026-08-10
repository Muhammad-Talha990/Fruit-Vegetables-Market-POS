using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FruitVegetableMarketPOS.Models
{
    /// <summary>Checkbox row when copying yesterday's POS menu into today.</summary>
    public class PreviousDayMenuItem : INotifyPropertyChanged
    {
        public int ItemId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? NameUrdu { get; set; }
        public string DisplayLabel =>
            string.IsNullOrWhiteSpace(NameUrdu) ? Name : $"{Name} / {NameUrdu}";

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
