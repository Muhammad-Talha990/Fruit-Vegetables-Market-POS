using System;
using System.Runtime.Versioning;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using FruitVegetableMarketPOS.Services;
using FruitVegetableMarketPOS.Views;

namespace FruitVegetableMarketPOS.ViewModels
{
    /// <summary>
    /// Main shell ViewModel — handles navigation between views and logout.
    /// Professional DI implementation using IServiceProvider for navigation.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class MainViewModel : BaseViewModel
    {
        private readonly AuthService _authService;
        private readonly IServiceProvider _serviceProvider;

        public event Action? LogoutRequested;

        private BaseViewModel _currentView = null!;
        public BaseViewModel CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }

        private string _currentUserName = string.Empty;
        public string CurrentUserName
        {
            get => _currentUserName;
            set => SetProperty(ref _currentUserName, value);
        }

        private string _currentUserRole = string.Empty;
        public string CurrentUserRole
        {
            get => _currentUserRole;
            set => SetProperty(ref _currentUserRole, value);
        }

        private bool _isAdmin;
        public bool IsAdmin
        {
            get => _isAdmin;
            set => SetProperty(ref _isAdmin, value);
        }

        private bool _isSidebarVisible = true;
        public bool IsSidebarVisible
        {
            get => _isSidebarVisible;
            set
            {
                if (SetProperty(ref _isSidebarVisible, value))
                    OnPropertyChanged(nameof(ShowSidebarHoverStrip));
            }
        }

        private string _selectedMenu = "Dashboard";
        public string SelectedMenu
        {
            get => _selectedMenu;
            set
            {
                if (SetProperty(ref _selectedMenu, value))
                    NavigateTo(value);
            }
        }

        /// <summary>True while Billing is the active screen.</summary>
        public bool IsBillingScreen => string.Equals(_selectedMenu, "Billing", StringComparison.OrdinalIgnoreCase);

        /// <summary>Thin left-edge strip to reopen the sidebar on Billing.</summary>
        public bool ShowSidebarHoverStrip => IsBillingScreen && !IsSidebarVisible;

        /// <summary>Customer ID to load when navigating to CustomerLedger view.</summary>
        public int PendingLedgerCustomerId { get; set; }

        public ICommand NavigateCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand ToggleSidebarCommand { get; }
        public ICommand OpenSidebarCommand { get; }
        public ICommand CloseSidebarIfBillingCommand { get; }

        public MainViewModel(AuthService authService, IServiceProvider serviceProvider)
        {
            _authService = authService;
            _serviceProvider = serviceProvider;

            CurrentUserName = _authService.CurrentUser?.FullName ?? "User";
            CurrentUserRole = _authService.CurrentUser?.Role ?? "Cashier";
            IsAdmin = _authService.IsAdmin;

            NavigateCommand = new RelayCommand(p => NavigateTo(p?.ToString() ?? "Dashboard"));
            LogoutCommand = new RelayCommand(ExecuteLogout);
            ToggleSidebarCommand = new RelayCommand(_ => IsSidebarVisible = !IsSidebarVisible);
            OpenSidebarCommand = new RelayCommand(_ => IsSidebarVisible = true);
            CloseSidebarIfBillingCommand = new RelayCommand(_ =>
            {
                if (IsBillingScreen)
                    IsSidebarVisible = false;
            });

            NavigateTo("Dashboard");
        }

        /// <summary>
        /// Logic to create the MainWindow. Called from App.xaml.cs.
        /// </summary>
        public MainWindow InitializeView()
        {
            return new MainWindow { DataContext = this };
        }

        private void NavigateTo(string view)
        {
            _selectedMenu = view;
            OnPropertyChanged(nameof(SelectedMenu));
            OnPropertyChanged(nameof(IsBillingScreen));

            // Billing: collapse sidebar for max POS space. Other screens: keep sidebar open.
            IsSidebarVisible = !string.Equals(view, "Billing", StringComparison.OrdinalIgnoreCase);
            OnPropertyChanged(nameof(ShowSidebarHoverStrip));

            CurrentView = view switch
            {
                "Dashboard"    => ActivateDashboard(),
                "Products"     => ActivateProducts(),
                "Billing"      => ActivateBilling(),
                "Reports"      => ActivateReports(),
                "Returns"      => RefreshReturnVM(),
                "Customers"    => CreateCustomerManagementVM(),
                "CustomerLedger" => CreateCustomerLedgerVM(PendingLedgerCustomerId),

                _ => ActivateDashboard()
            };
        }

        private ReportsViewModel ActivateReports()
        {
            var vm = _serviceProvider.GetRequiredService<ReportsViewModel>();
            vm.OnActivated();
            return vm;
        }

        private CustomerManagementViewModel CreateCustomerManagementVM()
        {
            var vm = _serviceProvider.GetRequiredService<CustomerManagementViewModel>();
            // Wire once — ViewModels are singletons
            if (!_customersLedgerWired)
            {
                vm.ViewLedgerRequested += customerId =>
                {
                    PendingLedgerCustomerId = customerId;
                    NavigateTo("CustomerLedger");
                };
                _customersLedgerWired = true;
            }
            vm.OnActivated();
            return vm;
        }

        private bool _customersLedgerWired;
        private bool _ledgerBackWired;

        private DashboardViewModel ActivateDashboard()
        {
            var vm = _serviceProvider.GetRequiredService<DashboardViewModel>();
            vm.OnActivated();
            return vm;
        }

        private ProductsViewModel ActivateProducts()
        {
            var vm = _serviceProvider.GetRequiredService<ProductsViewModel>();
            vm.OnActivated();
            return vm;
        }

        private BillingViewModel ActivateBilling()
        {
            var vm = _serviceProvider.GetRequiredService<BillingViewModel>();
            // Paint Billing UI first, then refresh data on the next dispatcher pass.
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => vm.OnActivated()));
            return vm;
        }

        /// <summary>Warm billing cache after login so the first open feels instant.</summary>
        public void PrefetchBilling()
        {
            var vm = _serviceProvider.GetRequiredService<BillingViewModel>();
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() => vm.Warmup()));
        }

        private CustomerLedgerViewModel CreateCustomerLedgerVM(int customerId)
        {
            var vm = _serviceProvider.GetRequiredService<CustomerLedgerViewModel>();
            if (!_ledgerBackWired)
            {
                vm.GoBackRequested += () => NavigateTo("Customers");
                _ledgerBackWired = true;
            }
            if (customerId > 0)
                vm.Load(customerId);
            else
                vm.OnActivated();
            return vm;
        }

        private ReturnViewModel RefreshReturnVM()
        {
            var vm = _serviceProvider.GetRequiredService<ReturnViewModel>();
            vm.OnActivated();
            return vm;
        }

        private void ExecuteLogout()
        {
            _authService.Logout();
            LogoutRequested?.Invoke();
        }
    }
}
