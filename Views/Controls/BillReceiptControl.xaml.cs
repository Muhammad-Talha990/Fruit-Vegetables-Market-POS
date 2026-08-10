using System.Windows;
using System.Windows.Controls;
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Views.Controls
{
    /// <summary>
    /// A reusable receipt-style bill view that mirrors the thermal print format exactly.
    /// Bind the <see cref="Bill"/> dependency property to display any bill.
    /// Internal elements bind via ElementName to avoid DataContext conflicts.
    /// </summary>
    public partial class BillReceiptControl : UserControl
    {
        public static readonly DependencyProperty BillProperty =
            DependencyProperty.Register(nameof(Bill), typeof(Bill), typeof(BillReceiptControl),
                new PropertyMetadata(null));

        public Bill? Bill
        {
            get => (Bill?)GetValue(BillProperty);
            set => SetValue(BillProperty, value);
        }

        public BillReceiptControl()
        {
            InitializeComponent();
        }
    }
}
