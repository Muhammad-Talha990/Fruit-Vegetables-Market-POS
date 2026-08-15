using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FruitVegetableMarketPOS.Models
{
    /// <summary>One price row when adding an item to today's list (type 1…10).</summary>
    public class DailyTypePriceRow : INotifyPropertyChanged
    {
        public int Index { get; set; }

        public string Label => Index.ToString();

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

        private string _noteText = string.Empty;
        /// <summary>Optional note for this type (saved on ItemTypes.Note).</summary>
        public string NoteText
        {
            get => _noteText;
            set
            {
                if (_noteText == value) return;
                _noteText = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
