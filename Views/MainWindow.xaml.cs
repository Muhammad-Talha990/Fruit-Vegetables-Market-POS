using System.Windows;
using System.Windows.Input;
using FruitVegetableMarketPOS.ViewModels;

namespace FruitVegetableMarketPOS.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private MainViewModel? Vm => DataContext as MainViewModel;

        private void HoverStrip_MouseEnter(object sender, MouseEventArgs e)
        {
            if (Vm == null) return;
            Vm.IsSidebarVisible = true;
        }

        private void OverlaySidebar_MouseLeave(object sender, MouseEventArgs e)
        {
            if (Vm == null || !Vm.IsBillingScreen) return;
            // Keep open if pointer moved onto the thin strip (re-open zone)
            Vm.IsSidebarVisible = false;
        }
    }
}
