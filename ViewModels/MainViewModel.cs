using System;
using System.Runtime.Versioning;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using GroceryPOS.Services;
using GroceryPOS.Views;

namespace GroceryPOS.ViewModels
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
            set => SetProperty(ref _isSidebarVisible, value);
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

        /// <summary>Customer ID to load when navigating to CustomerLedger view.</summary>
        public int PendingLedgerCustomerId { get; set; }

        public ICommand NavigateCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand ToggleSidebarCommand { get; }

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
            // Auto-restore sidebar if navigating away from billing (optional, but user requested "only if we are billing")
            if (view != "Billing")
            {
                IsSidebarVisible = true;
            }

            _selectedMenu = view;
            OnPropertyChanged(nameof(SelectedMenu));

            CurrentView = view switch
            {
                "Dashboard"    => _serviceProvider.GetRequiredService<DashboardViewModel>(),
                "Products"     => ActivateProducts(),
                "Billing"      => _serviceProvider.GetRequiredService<BillingViewModel>(),
                "Reports"      => _serviceProvider.GetRequiredService<ReportsViewModel>(),
                "SupplierBills"=> _serviceProvider.GetRequiredService<SupplierBillsViewModel>(),
                "Returns"      => RefreshReturnVM(),
                "Customers"    => CreateCustomerManagementVM(),
                "Suppliers"    => _serviceProvider.GetRequiredService<SupplierManagementViewModel>(),
                "CustomerLedger" => CreateCustomerLedgerVM(PendingLedgerCustomerId),

                _ => _serviceProvider.GetRequiredService<DashboardViewModel>()
            };
        }

        private CustomerManagementViewModel CreateCustomerManagementVM()
        {
            var vm = _serviceProvider.GetRequiredService<CustomerManagementViewModel>();
            vm.ViewLedgerRequested += customerId =>
            {
                PendingLedgerCustomerId = customerId;
                NavigateTo("CustomerLedger");
            };
            return vm;
        }

        private ProductsViewModel ActivateProducts()
        {
            var vm = _serviceProvider.GetRequiredService<ProductsViewModel>();
            vm.OnActivated();
            return vm;
        }

        private CustomerLedgerViewModel CreateCustomerLedgerVM(int customerId)
        {
            var vm = _serviceProvider.GetRequiredService<CustomerLedgerViewModel>();
            vm.GoBackRequested += () => NavigateTo("Customers");
            if (customerId > 0) vm.Load(customerId);
            return vm;
        }

        private ReturnViewModel RefreshReturnVM()
        {
            var vm = _serviceProvider.GetRequiredService<ReturnViewModel>();
            vm.ClearForm();
            return vm;
        }

        private void ExecuteLogout()
        {
            _authService.Logout();
            LogoutRequested?.Invoke();
        }
    }
}
