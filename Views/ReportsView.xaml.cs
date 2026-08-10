using System.Windows.Controls;

namespace FruitVegetableMarketPOS.Views
{
    public partial class ReportsView : UserControl
    {
        public ReportsView()
        {
            InitializeComponent();
        }

        private void TabOverview_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (PanelOverview == null) return;
            PanelOverview.Visibility = System.Windows.Visibility.Visible;
            PanelAudit.Visibility = System.Windows.Visibility.Collapsed;
            PanelProducts.Visibility = System.Windows.Visibility.Collapsed;
            if (DataContext is ViewModels.ReportsViewModel vm &&
                (vm.ShowDailyItemGrid || vm.ShowDailyClosingPanel || vm.ShowDailySaleQtyGrid))
            {
                // Keep special report panels driven by VM visibility flags.
            }
        }

        private void TabAudit_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (PanelOverview == null) return;
            PanelOverview.Visibility = System.Windows.Visibility.Collapsed;
            PanelAudit.Visibility = System.Windows.Visibility.Visible;
            PanelProducts.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void TabProducts_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (PanelOverview == null) return;
            PanelOverview.Visibility = System.Windows.Visibility.Collapsed;
            PanelAudit.Visibility = System.Windows.Visibility.Collapsed;
            PanelProducts.Visibility = System.Windows.Visibility.Visible;
            if (DataContext is ViewModels.ReportsViewModel vm &&
                (vm.SelectedReportType is "Product-wise" or "Type-wise" or "Category-wise") == false)
            {
                vm.SelectedReportType = "Product-wise";
            }
        }
    }
}
