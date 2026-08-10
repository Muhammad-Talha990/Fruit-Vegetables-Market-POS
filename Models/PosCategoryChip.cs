using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FruitVegetableMarketPOS.Models
{
    /// <summary>
    /// Category filter chip for the POS product grid (includes ALL).
    /// </summary>
    public class PosCategoryChip : INotifyPropertyChanged
    {
        public string Label { get; set; } = string.Empty;
        public Category? Category { get; set; }

        private bool _isSelected;
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

        public void NotifyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
