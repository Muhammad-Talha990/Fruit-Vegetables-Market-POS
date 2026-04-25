using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using GroceryPOS.ViewModels;

namespace GroceryPOS.Views
{
    public partial class CustomerLedgerView : UserControl
    {
        private CustomerLedgerViewModel? _viewModel;

        public CustomerLedgerView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Unloaded += OnUnloaded;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_viewModel != null)
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

            _viewModel = e.NewValue as CustomerLedgerViewModel;
            if (_viewModel != null)
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(CustomerLedgerViewModel.IsReturnDetailOpen) || _viewModel?.IsReturnDetailOpen != true)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                ReturnDetailSection?.BringIntoView();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
