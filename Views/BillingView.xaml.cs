using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FruitVegetableMarketPOS.Views
{
    public partial class BillingView : UserControl
    {
        public BillingView()
        {
            InitializeComponent();
        }

        private void QuantityPickerBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TextBox box) return;
            box.Focus();
            box.SelectAll();
            e.Handled = true;
        }

        private void CartQty_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TextBox box) return;
            box.IsReadOnly = false;
            box.Background = System.Windows.Media.Brushes.White;
            box.BorderThickness = new Thickness(1);
            box.BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2E7D32")!);
            box.Focus();
            box.SelectAll();
            e.Handled = true;
        }

        private void CartQty_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox box) return;
            FinishCartQtyEdit(box);
        }

        private void CartQty_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox box) return;
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                FinishCartQtyEdit(box);
                Keyboard.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                FinishCartQtyEdit(box);
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private static void FinishCartQtyEdit(TextBox box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            box.IsReadOnly = true;
            box.Background = System.Windows.Media.Brushes.Transparent;
            box.BorderThickness = new Thickness(0);
        }
    }
}
