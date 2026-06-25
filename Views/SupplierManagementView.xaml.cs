using System.Windows.Controls;
using GroceryPOS.ViewModels;

namespace GroceryPOS.Views
{
    public partial class SupplierManagementView : UserControl
    {
        public SupplierManagementView()
        {
            InitializeComponent();
        }

        private void NumericOnly_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            e.Handled = !System.Linq.Enumerable.All(e.Text, char.IsDigit);
        }
    }
}
