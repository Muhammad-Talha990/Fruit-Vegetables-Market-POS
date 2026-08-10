using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using FruitVegetableMarketPOS.Models;
using FruitVegetableMarketPOS.Services;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Views;

namespace FruitVegetableMarketPOS.ViewModels
{
    /// <summary>
    /// ViewModel for the Dashboard screen.
    /// Shows today's summary statistics and recent bills.
    /// </summary>
    public class DashboardViewModel : BaseViewModel
    {
        private readonly ItemService _itemService;
        private readonly BillService _billService;
        private readonly AuthService _authService;
        private double _todaySales;
        public double TodaySales { get => _todaySales; set => SetProperty(ref _todaySales, value); }

        private int _todaySaleCount;
        public int TodaySaleCount { get => _todaySaleCount; set => SetProperty(ref _todaySaleCount, value); }

        private double _todayCredit;
        public double TodayCredit { get => _todayCredit; set => SetProperty(ref _todayCredit, value); }

        private double _todayCash;
        public double TodayCash { get => _todayCash; set => SetProperty(ref _todayCash, value); }

        private double _todayRecoveredCredit;
        public double TodayRecoveredCredit { get => _todayRecoveredCredit; set => SetProperty(ref _todayRecoveredCredit, value); }

        private double _todaySalesCash;
        public double TodaySalesCash { get => _todaySalesCash; set => SetProperty(ref _todaySalesCash, value); }

        private double _todayReturns;
        public double TodayReturns { get => _todayReturns; set => SetProperty(ref _todayReturns, value); }

        private double _todayCashRefunds;
        public double TodayCashRefunds { get => _todayCashRefunds; set => SetProperty(ref _todayCashRefunds, value); }

        private double _todayNetSales;
        public double TodayNetSales { get => _todayNetSales; set => SetProperty(ref _todayNetSales, value); }

        private double _todayCashInHand;
        public double TodayCashInHand { get => _todayCashInHand; set => SetProperty(ref _todayCashInHand, value); }

        private double _todayCashInDrawer;
        public double TodayCashInDrawer { get => _todayCashInDrawer; set => SetProperty(ref _todayCashInDrawer, value); }

        private double _todayOnlinePayments;
        public double TodayOnlinePayments { get => _todayOnlinePayments; set => SetProperty(ref _todayOnlinePayments, value); }

        private int _totalProducts;
        public int TotalProducts { get => _totalProducts; set => SetProperty(ref _totalProducts, value); }

        private string _greeting = string.Empty;
        public string Greeting { get => _greeting; set => SetProperty(ref _greeting, value); }

        public ObservableCollection<Bill> RecentSales { get; set; } = new();
        public ObservableCollection<OnlinePaymentBreakdownItem> OnlinePaymentBreakdown { get; set; } = new();

        public ICommand RefreshCommand { get; }
        public ICommand OpenBillDetailCommand { get; }

        private readonly System.Windows.Threading.DispatcherTimer _clockTimer;
        private DateTime _activeDashboardDate;
        public string CurrentTime => DateTime.Now.ToString("hh:mm:ss tt");

        public DashboardViewModel(AuthService authService, ItemService itemService, BillService billService)
        {
            _authService  = authService;
            _itemService  = itemService;
            _billService  = billService;

            var hour = DateTime.Now.Hour;
            var timeGreeting = hour < 12 ? "Good Morning" : hour < 17 ? "Good Afternoon" : "Good Evening";
            Greeting = $"{timeGreeting}, {authService.CurrentUser?.FullName ?? "User"}!";
            _activeDashboardDate = DateTime.Now.Date;

            RefreshCommand = new RelayCommand(LoadData);
            OpenBillDetailCommand = new RelayCommand<Bill>(OpenBillDetail);

            _clockTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, e) =>
            {
                OnPropertyChanged(nameof(CurrentTime));
                RefreshForNewDayIfNeeded();
            };
            _clockTimer.Start();

            SalesEvents.SalesChanged += OnSalesChanged;
            LoadData();
        }

        private void OnSalesChanged()
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(LoadData);
        }

        /// <summary>Called when Dashboard nav is opened — always refresh live totals.</summary>
        public void OnActivated() => LoadData();

        private void RefreshForNewDayIfNeeded()
        {
            var currentDate = DateTime.Now.Date;
            if (currentDate == _activeDashboardDate)
                return;

            _activeDashboardDate = currentDate;
            LoadData();
        }

        private void LoadData()
        {
            try
            {
                RefreshGreeting();

                TodaySales = _billService.GetTodayTotal();
                TodaySaleCount = _billService.GetTodayBillCount();
                TodayCredit = _billService.GetTodayTotalCredit();
                TodaySalesCash = _billService.GetTodayTotalCash();
                TodayRecoveredCredit = _billService.GetTodayRecoveredCredit();
                TodayCashRefunds = _billService.GetTodayCashRefunded();
                TodayCashInDrawer = _billService.GetTodayCashInDrawer();
                TodayOnlinePayments = _billService.GetTodayOnlinePayments();
                TodayCashInHand = TodayCashInDrawer + TodayOnlinePayments;

                Dispatch(() =>
                {
                    OnlinePaymentBreakdown.Clear();
                    var from = DateTime.Today;
                    var to = from.AddDays(1);
                    foreach (var kvp in _billService.GetOnlinePaymentBreakdown(from, to))
                    {
                        OnlinePaymentBreakdown.Add(new OnlinePaymentBreakdownItem { Method = kvp.Key, Amount = kvp.Value });
                    }
                });

                TodayReturns = _billService.GetTodayReturnsTotal();
                TodayNetSales = _billService.GetTodayNetSales();
                TodayCash = TodayCashInHand;
                TotalProducts = _itemService.GetTotalItemCount();

                Dispatch(() =>
                {
                    RecentSales.Clear();
                    foreach (var bill in _billService.GetTodayBills().Take(10))
                        RecentSales.Add(bill);
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error("Dashboard failed to load data", ex);
            }
        }

        private void RefreshGreeting()
        {
            var hour = DateTime.Now.Hour;
            var timeGreeting = hour < 12 ? "Good Morning" : hour < 17 ? "Good Afternoon" : "Good Evening";
            Greeting = $"{timeGreeting}, {_authService.CurrentUser?.FullName ?? "User"}!";
        }

        private void OpenBillDetail(Bill? bill)
        {
            if (bill == null) return;
            try
            {
                var freshBill = _billService.GetBillById(bill.BillId);
                if (freshBill == null)
                {
                    MessageBox.Show($"Bill #{bill.InvoiceNumber} not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var vm = new BillDetailViewModel(freshBill);
                var window = new BillDetailWindow
                {
                    DataContext = vm,
                    Owner = Application.Current.MainWindow
                };
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to open bill detail", ex);
                MessageBox.Show("Failed to load bill details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public override void Dispose()
        {
            _clockTimer.Stop();
            base.Dispose();
        }
    }

    public class OnlinePaymentBreakdownItem
    {
        public string Method { get; set; } = string.Empty;
        public double Amount { get; set; }
    }
}
