using System.Windows.Controls;

namespace GroceryPOS.Views
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
            PanelLowStock.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void TabAudit_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (PanelOverview == null) return;
            PanelOverview.Visibility = System.Windows.Visibility.Collapsed;
            PanelAudit.Visibility = System.Windows.Visibility.Visible;
            PanelProducts.Visibility = System.Windows.Visibility.Collapsed;
            PanelLowStock.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void TabProducts_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (PanelOverview == null) return;
            PanelOverview.Visibility = System.Windows.Visibility.Collapsed;
            PanelAudit.Visibility = System.Windows.Visibility.Collapsed;
            PanelProducts.Visibility = System.Windows.Visibility.Visible;
            PanelLowStock.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void TabLowStock_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (PanelOverview == null) return;
            PanelOverview.Visibility = System.Windows.Visibility.Collapsed;
            PanelAudit.Visibility = System.Windows.Visibility.Collapsed;
            PanelProducts.Visibility = System.Windows.Visibility.Collapsed;
            PanelLowStock.Visibility = System.Windows.Visibility.Visible;
        }
    }
}
