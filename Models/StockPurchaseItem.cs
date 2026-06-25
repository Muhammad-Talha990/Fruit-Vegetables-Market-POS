using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GroceryPOS.Models
{
    /// <summary>
    /// A single line item inside a StockPurchase cart.
    /// Maps to the StockPurchaseItems table.
    /// Implements INotifyPropertyChanged so quantity / cost price edits
    /// immediately refresh cart totals in the ViewModel.
    /// </summary>
    public class StockPurchaseItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public int PurchaseId { get; set; }

        public int ItemId { get; set; }
        public string ItemDescription { get; set; } = string.Empty;
        public string? Barcode { get; set; }

        private double _quantity = 1;
        /// <summary>Quantity of units being purchased. Must be > 0.</summary>
        public double Quantity
        {
            get => _quantity;
            set
            {
                if (_quantity != value && value > 0)
                {
                    _quantity = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LineTotal));
                }
            }
        }

        private double _costPrice;
        /// <summary>
        /// Per-unit cost price for this purchase. 
        /// Pre-filled from Items.CostPrice but editable by the user.
        /// </summary>
        public double CostPrice
        {
            get => _costPrice;
            set
            {
                if (_costPrice != value && value >= 0)
                {
                    _costPrice = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LineTotal));
                }
            }
        }

        /// <summary>Computed: Quantity × CostPrice.</summary>
        public double LineTotal => Math.Round(Quantity * CostPrice, 2);

        // ── INotifyPropertyChanged ──
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
