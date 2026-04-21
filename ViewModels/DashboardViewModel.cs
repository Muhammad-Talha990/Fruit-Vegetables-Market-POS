using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using GroceryPOS.Models;
using GroceryPOS.Services;

namespace GroceryPOS.ViewModels
{
    /// <summary>
    /// ViewModel for the Dashboard screen.
    /// Shows today's summary statistics and recent bills.
    /// </summary>
    public class DashboardViewModel : BaseViewModel
    {
        private readonly ItemService _itemService;
        private readonly BillService _billService;
        private readonly IStockService _stockService;

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

        private int _lowStockCount;
        public int LowStockCount { get => _lowStockCount; set => SetProperty(ref _lowStockCount, value); }

        private string _greeting = string.Empty;
        public string Greeting { get => _greeting; set => SetProperty(ref _greeting, value); }

        public ObservableCollection<Bill> RecentSales { get; set; } = new();
        public ObservableCollection<Item> LowStockItems { get; set; } = new();

        public ICommand RefreshCommand { get; }

        private readonly System.Windows.Threading.DispatcherTimer _clockTimer;
        private DateTime _activeDashboardDate;
        public string CurrentTime => DateTime.Now.ToString("hh:mm:ss tt");

        public DashboardViewModel(AuthService authService, ItemService itemService, BillService billService, IStockService stockService)
        {
            _itemService = itemService;
            _billService = billService;
            _stockService = stockService;

            var hour = DateTime.Now.Hour;
            var timeGreeting = hour < 12 ? "Good Morning" : hour < 17 ? "Good Afternoon" : "Good Evening";
            Greeting = $"{timeGreeting}, {authService.CurrentUser?.FullName ?? "User"}!";
            _activeDashboardDate = DateTime.Now.Date;

            RefreshCommand = new RelayCommand(LoadData);

            // Live clock
            _clockTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, e) =>
            {
                OnPropertyChanged(nameof(CurrentTime));
                RefreshForNewDayIfNeeded();
            };
            _clockTimer.Start();

            // Real-time updates
            _stockService.StockChanged += LoadData;

            LoadData();
        }

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
            TodaySales = _billService.GetTodayTotal();
            TodaySaleCount = _billService.GetTodayBillCount();
            TodayCredit = _billService.GetTodayTotalCredit();
            TodaySalesCash = _billService.GetTodayTotalCash();
            TodayRecoveredCredit = _billService.GetTodayRecoveredCredit();
            TodayCashRefunds = _billService.GetTodayCashRefunded();
            // TodaySalesCash now includes refunds (negative payments), so we don't subtract them again here.
            TodayCashInHand = TodaySalesCash + TodayRecoveredCredit;

            TodayCashInDrawer = _billService.GetTodayCashInDrawer();
            TodayOnlinePayments = _billService.GetTodayOnlinePayments();

            TodayReturns = _billService.GetTodayReturnsTotal();
            TodayNetSales = _billService.GetTodayNetSales();
            TodayCash = TodaySalesCash + TodayRecoveredCredit;
            TotalProducts = _itemService.GetTotalItemCount();
            LowStockCount = _stockService.GetLowStockCount();

            Dispatch(() =>
            {
                RecentSales.Clear();
                foreach (var bill in _billService.GetTodayBills().Take(10))
                    RecentSales.Add(bill);

                LowStockItems.Clear();
                foreach (var item in _stockService.GetLowStockItems().Take(10))
                    LowStockItems.Add(item);
            });
        }
        public override void Dispose()
        {
            _clockTimer.Stop();
            if (_stockService != null)
                _stockService.StockChanged -= LoadData;
            base.Dispose();
        }
    }
}
