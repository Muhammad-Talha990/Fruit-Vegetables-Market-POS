using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Windows;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Models;
using FruitVegetableMarketPOS.Services;

namespace FruitVegetableMarketPOS.ViewModels
{
    [SupportedOSPlatform("windows")]
    public class ReportsViewModel : BaseViewModel
    {
        private readonly ReportService _reportService;
        private readonly PrintService _printService;
        private readonly AuthService _authService;
        private readonly AccountService _accountService;
        private readonly IReturnService _returnService;

        public ObservableCollection<Bill> SalesReport { get; } = new();
        public ICollectionView BillsView { get; }
        public ObservableCollection<ItemSalesRow> ItemReport { get; } = new();
        public ObservableCollection<ItemLineDetail> ItemLines { get; } = new();
        public ObservableCollection<CustomerSalesRow> CustomerReport { get; } = new();
        public ObservableCollection<PaymentMethodRow> PaymentMethodStats { get; } = new();
        public ObservableCollection<ReceiptLedgerRow> ReceiptLedger { get; } = new();
        public ObservableCollection<AccountReceiptRow> AccountReport { get; } = new();
        public ObservableCollection<MonthlyBucket> MonthlyReport { get; } = new();
        public ObservableCollection<ChartDataPoint> DailySalesChart { get; } = new();
        public ObservableCollection<ChartDataPoint> MonthlyRevenueChart { get; } = new();
        public ObservableCollection<ChartDataPoint> QuantityChart { get; } = new();
        public ObservableCollection<ChartDataPoint> TopQtyChart { get; } = new();
        public ObservableCollection<ChartDataPoint> TopRevenueChart { get; } = new();
        public ObservableCollection<ChartDataPoint> AccountReceiptChart { get; } = new();
        public ObservableCollection<ChartDataPoint> PaymentChart { get; } = new();
        public ObservableCollection<Account> AccountOptions { get; } = new();
        public List<string> DatePresets { get; } = new()
        {
            "Daily", "Weekly", "Monthly", "Yearly", "Custom Range"
        };
        public List<string> PaymentOptions { get; } = new() { "All", "Cash", "Online" };
        public List<string> ReceiptKindOptions { get; } = new() { "All", "Bills only", "Payments only" };
        public List<string> Sections { get; } = new()
        {
            "Overview", "Sales", "Items", "Bills", "Customers", "Payments", "Accounts"
        };

        public event Action<int>? ViewLedgerRequested;

        private const int PageSize = 5000;
        private int _currentPage = 1;
        private bool _liveFilters;
        private bool _suppressRefresh;
        private bool _overlayRequested;
        private int _loadSerial;
        private DispatcherTimer? _refreshDebounce;
        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (SetProperty(ref _currentPage, Math.Max(1, value)))
                    ApplySearch();
            }
        }

        public int TotalPages { get; private set; } = 1;
        public string PageLabel => $"Page {CurrentPage} of {TotalPages}";
        public bool CanGoPrev => CurrentPage > 1;
        public bool CanGoNext => CurrentPage < TotalPages;
        public bool ShowPagination => false;

        private string _selectedSection = "Overview";
        public string SelectedSection
        {
            get => _selectedSection;
            set
            {
                if (SetProperty(ref _selectedSection, value))
                {
                    _currentPage = 1;
                    OnPropertyChanged(nameof(CurrentPage));
                    OnPropertyChanged(nameof(SectionDescription));
                    OnPropertyChanged(nameof(ShowOverview));
                    OnPropertyChanged(nameof(ShowSales));
                    OnPropertyChanged(nameof(ShowItems));
                    OnPropertyChanged(nameof(ShowBills));
                    OnPropertyChanged(nameof(ShowCustomers));
                    OnPropertyChanged(nameof(ShowPayments));
                    OnPropertyChanged(nameof(ShowAccounts));
                    OnPropertyChanged(nameof(ShowStackedLayout));
                    OnPropertyChanged(nameof(ShowPagination));
                    ApplySearch();
                }
            }
        }

        public bool ShowOverview => SelectedSection == "Overview";
        public bool ShowSales => SelectedSection == "Sales";
        public bool ShowItems => SelectedSection == "Items";
        public bool ShowBills => SelectedSection == "Bills";
        public bool ShowCustomers => SelectedSection == "Customers";
        public bool ShowPayments => SelectedSection == "Payments";
        public bool ShowAccounts => SelectedSection == "Accounts";
        public bool ShowStackedLayout => ShowOverview || ShowSales;

        public string SectionDescription => SelectedSection switch
        {
            "Sales" => "Gross, discount, net sales and bill-level history for the selected period.",
            "Items" => "Quantity sold and historical line revenue.",
            "Bills" => "Every sale bill in the period, with payment method and remaining credit.",
            "Customers" => "Purchase totals and outstanding balance from the existing customer ledger.",
            "Payments" => "Cash vs online receipts, including credit recoveries in Bill Payments.",
            "Accounts" => "Online money received in each payment account selected at billing.",
            _ => "High-level business performance from actual bills and line items."
        };

        private string _selectedPreset = "Monthly";
        public string SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (!SetProperty(ref _selectedPreset, value)) return;
                IsCustomRange = string.Equals(value, "Custom Range", StringComparison.OrdinalIgnoreCase);
                if (!IsCustomRange) ApplyPreset(value);
            }
        }

        private bool _isCustomRange;
        public bool IsCustomRange { get => _isCustomRange; set => SetProperty(ref _isCustomRange, value); }

        private DateTime _fromDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        public DateTime FromDate
        {
            get => _fromDate;
            set
            {
                if (!SetProperty(ref _fromDate, value)) return;
                if (_fromDate > ToDate) ToDate = _fromDate;
                else if (IsCustomRange && _liveFilters) RefreshQuiet();
            }
        }

        private DateTime _toDate = DateTime.Today;
        public DateTime ToDate
        {
            get => _toDate;
            set
            {
                if (!SetProperty(ref _toDate, value)) return;
                if (_toDate < FromDate) FromDate = _toDate;
                else if (IsCustomRange && _liveFilters) RefreshQuiet();
            }
        }

        private string _selectedPayment = "All";
        public string SelectedPayment
        {
            get => _selectedPayment;
            set { if (SetProperty(ref _selectedPayment, value) && _liveFilters) RefreshQuiet(); }
        }

        private string _selectedReceiptKind = "All";
        public string SelectedReceiptKind
        {
            get => _selectedReceiptKind;
            set { if (SetProperty(ref _selectedReceiptKind, value)) ApplySearch(); }
        }

        private Account? _selectedAccountFilter;
        public Account? SelectedAccountFilter
        {
            get => _selectedAccountFilter;
            set { if (SetProperty(ref _selectedAccountFilter, value) && _liveFilters) RefreshQuiet(); }
        }

        private string _searchQuery = "";
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (!SetProperty(ref _searchQuery, value)) return;
                _currentPage = 1;
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(IsSearchEmpty));
                ApplySearch();
            }
        }
        public bool IsSearchEmpty => string.IsNullOrEmpty(SearchQuery);

        private ReportKpis _kpis = new();
        public ReportKpis Kpis { get => _kpis; set => SetProperty(ref _kpis, value); }

        private string _periodLabel = "";
        public string PeriodLabel { get => _periodLabel; set => SetProperty(ref _periodLabel, value); }

        private string _activeFiltersText = "";
        public string ActiveFiltersText { get => _activeFiltersText; set => SetProperty(ref _activeFiltersText, value); }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set { if (SetProperty(ref _isBusy, value)) CommandManager.InvalidateRequerySuggested(); } }

        private string _statusMessage = "";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private bool _hasError;
        public bool HasError { get => _hasError; set => SetProperty(ref _hasError, value); }

        private ItemSalesRow? _selectedItem;
        public ItemSalesRow? SelectedItem { get => _selectedItem; set => SetProperty(ref _selectedItem, value); }

        private bool _isItemDetailOpen;
        public bool IsItemDetailOpen { get => _isItemDetailOpen; set => SetProperty(ref _isItemDetailOpen, value); }

        private AccountReceiptRow? _selectedAccountRow;
        public AccountReceiptRow? SelectedAccountRow { get => _selectedAccountRow; set => SetProperty(ref _selectedAccountRow, value); }

        public ObservableCollection<Bill> AccountBills { get; } = new();
        private bool _isAccountDetailOpen;
        public bool IsAccountDetailOpen { get => _isAccountDetailOpen; set => SetProperty(ref _isAccountDetailOpen, value); }

        private CustomerSalesRow? _selectedCustomer;
        public CustomerSalesRow? SelectedCustomer { get => _selectedCustomer; set => SetProperty(ref _selectedCustomer, value); }
        public ObservableCollection<Bill> CustomerBills { get; } = new();
        private bool _isCustomerDetailOpen;
        public bool IsCustomerDetailOpen { get => _isCustomerDetailOpen; set => SetProperty(ref _isCustomerDetailOpen, value); }

        public string ItemDetailFooter { get; private set; } = "";
        public string TrendChartTitle { get; private set; } = "Monthly revenue";
        public string QuantityChartTitle { get; private set; } = "Monthly quantity sold";
        public string TrendChartHint { get; private set; } = "";
        public string DailySalesHint { get; private set; } = "";
        public bool HasSalesData => Kpis.BillCount > 0;

        private Bill? _selectedHistoryBill;
        public Bill? SelectedHistoryBill { get => _selectedHistoryBill; set => SetProperty(ref _selectedHistoryBill, value); }
        private bool _isBillDetailOpen;
        public bool IsBillDetailOpen { get => _isBillDetailOpen; set => SetProperty(ref _isBillDetailOpen, value); }

        private List<Bill> _rawBills = new();
        private List<ItemSalesRow> _rawItems = new();
        private List<CustomerSalesRow> _rawCustomers = new();
        private List<ReceiptLedgerRow> _rawReceipts = new();
        private List<AccountReceiptRow> _rawAccounts = new();

        public ICommand RefreshCommand { get; }
        public ICommand ResetFiltersCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand PrintCommand { get; }
        public ICommand SavePdfCommand { get; }
        public ICommand SelectSectionCommand { get; }
        public ICommand OpenItemCommand { get; }
        public ICommand CloseItemCommand { get; }
        public ICommand OpenAccountCommand { get; }
        public ICommand CloseAccountCommand { get; }
        public ICommand OpenCustomerCommand { get; }
        public ICommand CloseCustomerCommand { get; }
        public ICommand OpenLedgerCommand { get; }
        public ICommand ViewBillCommand { get; }
        public ICommand OpenLineBillCommand { get; }
        public ICommand PrintBillCommand { get; }
        public ICommand CloseBillDetailCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand PrevPageCommand { get; }

        public ReportsViewModel(ReportService reportService, PrintService printService,
            AuthService authService, AccountService accountService, IReturnService returnService)
        {
            _reportService = reportService;
            _printService = printService;
            _authService = authService;
            _accountService = accountService;
            _returnService = returnService;

            BillsView = CollectionViewSource.GetDefaultView(SalesReport);
            BillsView.Filter = obj => obj is Bill bill && MatchesBill(bill, SearchQuery?.Trim() ?? "");
            CollectionViewSource.GetDefaultView(ItemReport).Filter =
                obj => obj is ItemSalesRow item && MatchesItem(item, SearchQuery?.Trim() ?? "");
            CollectionViewSource.GetDefaultView(CustomerReport).Filter =
                obj => obj is CustomerSalesRow customer && MatchesCustomer(customer, SearchQuery?.Trim() ?? "");
            CollectionViewSource.GetDefaultView(ReceiptLedger).Filter =
                obj => obj is ReceiptLedgerRow receipt && MatchesReceipt(receipt, SearchQuery?.Trim() ?? "");
            CollectionViewSource.GetDefaultView(AccountReport).Filter =
                obj => obj is AccountReceiptRow account && MatchesAccount(account, SearchQuery?.Trim() ?? "");

            RefreshCommand = new RelayCommand(_ => RefreshQuiet());
            ResetFiltersCommand = new RelayCommand(_ => ResetFilters(), _ => !IsBusy);
            ExportCsvCommand = new RelayCommand(_ => ExportCsv(), _ => !IsBusy && !HasError);
            PrintCommand = new RelayCommand(_ => PrintReport(), _ => !IsBusy && !HasError);
            SavePdfCommand = new RelayCommand(_ => SaveReportPdf(), _ => !IsBusy && !HasError);
            SelectSectionCommand = new RelayCommand(p => { if (p is string s) SelectedSection = s; });
            OpenItemCommand = new RelayCommand(p => OpenItem(p as ItemSalesRow));
            CloseItemCommand = new RelayCommand(_ => { IsItemDetailOpen = false; SelectedItem = null; ItemLines.Clear(); });
            OpenAccountCommand = new RelayCommand(p => OpenAccount(p as AccountReceiptRow));
            CloseAccountCommand = new RelayCommand(_ => { IsAccountDetailOpen = false; AccountBills.Clear(); });
            OpenCustomerCommand = new RelayCommand(p => OpenCustomer(p as CustomerSalesRow));
            CloseCustomerCommand = new RelayCommand(_ => { IsCustomerDetailOpen = false; CustomerBills.Clear(); SelectedCustomer = null; });
            OpenLedgerCommand = new RelayCommand(p =>
            {
                var raw = (p as CustomerSalesRow)?.CustomerId ?? SelectedCustomer?.CustomerId;
                if (int.TryParse(raw, out var id) && id > 0)
                    ViewLedgerRequested?.Invoke(id);
            });
            ViewBillCommand = new RelayCommand(p => ViewBill(p as Bill));
            OpenLineBillCommand = new RelayCommand(p =>
            {
                if (p is ItemLineDetail line)
                    ViewBill(_rawBills.FirstOrDefault(b => b.BillId == line.InternalBillId)
                             ?? _reportService.GetBillById(line.InternalBillId)
                             ?? new Bill { BillId = line.InternalBillId });
            });
            PrintBillCommand = new RelayCommand(p => PrintBill(p as Bill));
            CloseBillDetailCommand = new RelayCommand(_ => { IsBillDetailOpen = false; SelectedHistoryBill = null; });
            NextPageCommand = new RelayCommand(_ => CurrentPage++, _ => CanGoNext);
            PrevPageCommand = new RelayCommand(_ => CurrentPage--, _ => CanGoPrev);

            AppEvents.DataChanged += OnAppDataChanged;
            LoadAccountOptions();
            ApplyPreset(SelectedPreset);
            _liveFilters = true;
        }

        public void OnActivated()
        {
            try
            {
                _suppressRefresh = true;
                if (!IsCustomRange)
                    ApplyPresetDates(SelectedPreset);
                var keepId = SelectedAccountFilter?.Id;
                LoadAccountOptions(keepId);
            }
            finally
            {
                _suppressRefresh = false;
            }
            RefreshImmediate();
        }

        private void OnAppDataChanged() => AppEvents.InvokeOnUi(() => RefreshQuiet());

        private void LoadAccountOptions(int? keepId = null)
        {
            keepId ??= SelectedAccountFilter?.Id;
            AccountOptions.Clear();
            AccountOptions.Add(new Account { Id = 0, AccountTitle = "All accounts", AccountType = "" });
            foreach (var a in _accountService.GetOnlinePaymentAccounts())
                AccountOptions.Add(a);
            SelectedAccountFilter = AccountOptions.FirstOrDefault(a => a.Id == keepId) ?? AccountOptions.FirstOrDefault();
        }

        private void ApplyPreset(string preset)
        {
            ApplyPresetDates(preset);
            RefreshQuiet();
        }

        private void ApplyPresetDates(string preset)
        {
            var today = DateTime.Today;
            int mondayOffset = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
            var weekStart = today.AddDays(-mondayOffset);
            _suppressRefresh = true;
            try
            {
                (FromDate, ToDate) = preset switch
                {
                    "Daily" => (today, today),
                    "Weekly" => (weekStart, today),
                    "Monthly" => (new DateTime(today.Year, today.Month, 1), today),
                    "Yearly" => (new DateTime(today.Year, 1, 1), today),
                    _ => (FromDate, ToDate)
                };
            }
            finally
            {
                _suppressRefresh = false;
            }
        }

        private void ResetFilters()
        {
            _liveFilters = false;
            SelectedPayment = "All";
            SelectedAccountFilter = AccountOptions.FirstOrDefault();
            SelectedReceiptKind = "All";
            SearchQuery = "";
            _liveFilters = true;
            SelectedPreset = "Monthly";
        }

        private (DateTime start, DateTime end) Range() => (FromDate.Date, ToDate.Date.AddDays(1));
        private string? PaymentFilter => SelectedPayment == "All" ? null : SelectedPayment;
        private int? AccountFilter => SelectedAccountFilter == null || SelectedAccountFilter.Id <= 0 ? null : SelectedAccountFilter.Id;

        public void Refresh() => RefreshImmediate(showOverlay: true);

        private void RefreshQuiet(bool showOverlay = false)
        {
            if (_suppressRefresh) return;
            if (showOverlay) _overlayRequested = true;

            _refreshDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _refreshDebounce.Tick -= DebouncedRefresh;
            _refreshDebounce.Tick += DebouncedRefresh;
            _refreshDebounce.Stop();
            _refreshDebounce.Start();
        }

        private void DebouncedRefresh(object? sender, EventArgs e)
        {
            _refreshDebounce?.Stop();
            var overlay = _overlayRequested;
            _overlayRequested = false;
            RefreshImmediate(overlay);
        }

        private void RefreshImmediate(bool showOverlay = false)
        {
            if (_suppressRefresh) return;
            _refreshDebounce?.Stop();
            if (showOverlay) IsBusy = true;
            var serial = ++_loadSerial;
            LoadReportData(serial);
        }

        private void LoadReportData(int serial)
        {
            try
            {
                var (start, end) = Range();
                PeriodLabel = $"{FromDate:dd MMM yyyy}  –  {ToDate:dd MMM yyyy}";
                ActiveFiltersText = BuildFilterSummary();

                var kpis = _reportService.GetKpis(start, end, PaymentFilter, AccountFilter);
                var prevLen = Math.Max(1, (ToDate.Date - FromDate.Date).Days + 1);
                var prevEnd = FromDate.Date;
                var prevStart = prevEnd.AddDays(-prevLen);
                var prev = _reportService.GetKpis(prevStart, prevEnd, PaymentFilter, AccountFilter);
                kpis.PreviousNetSales = prev.NetSales;
                kpis.PeriodLabel = PeriodLabel;

                var monthStart = new DateTime(start.Year, start.Month, 1);
                var monthEnd = new DateTime(end.Year, end.Month, 1);
                if (monthEnd <= monthStart)
                    monthStart = monthEnd.AddMonths(-5);

                var monthRows = _reportService.GetMonthlyBuckets(monthStart, end, PaymentFilter, AccountFilter);
                var monthMap = monthRows.ToDictionary(m => m.MonthKey, StringComparer.Ordinal);
                var monthlyRev = new List<ChartDataPoint>();
                var qtyChart = new List<ChartDataPoint>();
                for (var cursor = monthStart; cursor <= monthEnd; cursor = cursor.AddMonths(1))
                {
                    var key = cursor.ToString("yyyy-MM");
                    monthMap.TryGetValue(key, out var bucket);
                    monthlyRev.Add(new ChartDataPoint { Label = cursor.ToString("MMM"), Value = bucket?.Revenue ?? 0 });
                    qtyChart.Add(new ChartDataPoint
                    {
                        Label = cursor.ToString("MMM"),
                        Value = bucket?.Quantity ?? 0,
                        FormatAsCurrency = false
                    });
                }

                var days = _reportService.GetDailySeries(start, end, PaymentFilter, AccountFilter);
                var billed = days.Count(d => d.Revenue > 0.01 || d.Quantity > 0.01 || d.Bills > 0);
                var dailyHint = billed == 0
                    ? "No billed days in this period"
                    : $"{billed} day{(billed == 1 ? "" : "s")} with sales";
                var daily = days.Select(d => new ChartDataPoint { Label = LabelFor(d.Date), Value = d.Revenue }).ToList();

                var trendTitle = "Monthly revenue";
                var qtyTitle = "Monthly quantity sold";
                var trendHint = monthStart.Year == monthEnd.Year
                    ? $"{monthStart:MMM} – {monthEnd:MMM yyyy}"
                    : $"{monthStart:MMM yyyy} – {monthEnd:MMM yyyy}";

                var items = _reportService.GetItemSales(start, end, null, PaymentFilter, AccountFilter);
                var topRev = items.OrderByDescending(i => i.Revenue).Take(6)
                    .Select(row => new ChartDataPoint { Label = row.ItemName, Value = row.Revenue }).ToList();
                var topQty = items.OrderByDescending(i => i.QuantitySold).Take(5)
                    .Select(row => new ChartDataPoint { Label = row.ItemName, Value = row.QuantitySold, FormatAsCurrency = false }).ToList();
                var bills = _reportService.GetSaleBills(start, end, PaymentFilter, AccountFilter);
                var payments = _reportService.GetPaymentBreakdown(start, end, PaymentFilter, AccountFilter);
                var receipts = _reportService.GetReceiptLedger(start, end, PaymentFilter, AccountFilter);
                var accounts = _reportService.GetAccountReceipts(start, end, PaymentFilter, AccountFilter);
                var customers = _reportService.GetCustomerSales(start, end, PaymentFilter, AccountFilter);
                var months = _reportService.GetMonthlyBuckets(start, end, PaymentFilter, AccountFilter);

                if (serial != _loadSerial) return;

                HasError = false;
                Kpis = kpis;
                OnPropertyChanged(nameof(HasSalesData));
                TrendChartTitle = trendTitle;
                QuantityChartTitle = qtyTitle;
                TrendChartHint = trendHint;
                DailySalesHint = dailyHint;
                OnPropertyChanged(nameof(TrendChartTitle));
                OnPropertyChanged(nameof(QuantityChartTitle));
                OnPropertyChanged(nameof(TrendChartHint));
                OnPropertyChanged(nameof(DailySalesHint));

                DailySalesChart.Clear();
                foreach (var p in daily) DailySalesChart.Add(p);
                MonthlyRevenueChart.Clear();
                foreach (var p in monthlyRev) MonthlyRevenueChart.Add(p);
                QuantityChart.Clear();
                foreach (var p in qtyChart) QuantityChart.Add(p);
                TopRevenueChart.Clear();
                foreach (var p in topRev) TopRevenueChart.Add(p);
                TopQtyChart.Clear();
                foreach (var p in topQty) TopQtyChart.Add(p);
                AccountReceiptChart.Clear();
                foreach (var a in accounts.OrderByDescending(x => x.AmountReceived).Take(8))
                {
                    AccountReceiptChart.Add(new ChartDataPoint
                    {
                        Label = AccountChartLabel(a, accounts),
                        Value = a.AmountReceived
                    });
                }

                _rawItems = items;
                _rawBills = bills;
                _rawCustomers = customers;
                _rawReceipts = receipts;
                _rawAccounts = accounts;

                SalesReport.Clear();
                foreach (var b in bills) SalesReport.Add(b);
                ItemReport.Clear();
                foreach (var i in items) ItemReport.Add(i);
                CustomerReport.Clear();
                foreach (var c in customers) CustomerReport.Add(c);
                ReceiptLedger.Clear();
                foreach (var r in receipts) ReceiptLedger.Add(r);
                AccountReport.Clear();
                foreach (var a in accounts) AccountReport.Add(a);

                PaymentMethodStats.Clear();
                PaymentChart.Clear();
                foreach (var p in payments)
                {
                    PaymentMethodStats.Add(p);
                    PaymentChart.Add(new ChartDataPoint { Label = p.Method, Value = p.Amount });
                }

                MonthlyReport.Clear();
                foreach (var m in months)
                    MonthlyReport.Add(m);

                _currentPage = 1;
                OnPropertyChanged(nameof(CurrentPage));
                ApplySearch();
                StatusMessage = Kpis.BillCount == 0 ? "No sales found for the selected period." : "";
            }
            catch (Exception ex)
            {
                if (serial != _loadSerial) return;
                HasError = true;
                StatusMessage = "Unable to load the report. Please try again.";
                AppLogger.Error("Reports refresh failed", ex);
            }
            finally
            {
                if (serial == _loadSerial)
                    IsBusy = false;
            }
        }

        private static string ShortMonth(string label)
        {
            var space = label.IndexOf(' ');
            return space > 0 ? label[..3] : label;
        }

        private static string AccountChartLabel(AccountReceiptRow row, IReadOnlyList<AccountReceiptRow> all)
        {
            var type = row.AccountType?.Trim() ?? "";
            if (string.IsNullOrEmpty(type))
                return row.AccountName;
            var typeUsed = all.Count(a => string.Equals(a.AccountType?.Trim(), type, StringComparison.OrdinalIgnoreCase));
            return typeUsed > 1 ? row.AccountName : type;
        }

        private string LabelFor(DateTime d)
        {
            var days = (ToDate.Date - FromDate.Date).Days;
            if (days <= 7) return d.ToString("ddd d");
            return d.ToString("d MMM");
        }

        private void ApplySearch()
        {
            CollectionViewSource.GetDefaultView(SalesReport).Refresh();
            CollectionViewSource.GetDefaultView(ItemReport).Refresh();
            CollectionViewSource.GetDefaultView(CustomerReport).Refresh();
            CollectionViewSource.GetDefaultView(ReceiptLedger).Refresh();
            CollectionViewSource.GetDefaultView(AccountReport).Refresh();

            var q = SearchQuery?.Trim() ?? "";
            var billCount = _rawBills.Count(b => MatchesBill(b, q));
            var itemCount = _rawItems.Count(i => MatchesItem(i, q));
            var customerCount = _rawCustomers.Count(c => MatchesCustomer(c, q));
            var receiptCount = _rawReceipts.Count(r => MatchesReceipt(r, q));
            var accountCount = _rawAccounts.Count(a => MatchesAccount(a, q));
            var sourceCount = SelectedSection switch
            {
                "Items" => itemCount,
                "Customers" => customerCount,
                "Payments" => receiptCount,
                "Accounts" => accountCount,
                _ => billCount
            };
            TotalPages = Math.Max(1, (int)Math.Ceiling(sourceCount / (double)PageSize));
            if (_currentPage > TotalPages) _currentPage = TotalPages;
            OnPropertyChanged(nameof(CurrentPage));
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(PageLabel));
            OnPropertyChanged(nameof(CanGoPrev));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(ShowPagination));
            ActiveFiltersText = BuildFilterSummary();
        }

        private static bool MatchesBill(Bill b, string q)
        {
            if (string.IsNullOrEmpty(q)) return true;
            if (MatchesCustomerName(b.CustomerDisplayName, q)) return true;
            if (MatchesCustomerName(b.Customer?.Name, q)) return true;
            if (MatchesCustomerName(b.Customer?.FullName, q)) return true;
            return Contains(b.InvoiceNumber, q)
                || Contains(b.InvoiceDisplay, q)
                || Contains(b.BillId.ToString(), q)
                || Contains(b.CustomerId?.ToString(), q)
                || Contains(b.Customer?.Phone, q)
                || Contains(b.Customer?.SecondaryPhone, q)
                || Contains(b.PaymentMethod, q)
                || Contains(b.OnlinePaymentMethod, q);
        }

        private static bool MatchesCustomerName(string? name, string q)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var part in name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool MatchesItem(ItemSalesRow i, string q) =>
            string.IsNullOrEmpty(q)
            || Contains(i.ItemName, q)
            || Contains(i.NameUrdu, q);

        private bool MatchesReceipt(ReceiptLedgerRow r, string q)
        {
            if (SelectedReceiptKind == "Bills only" && r.IsPayment) return false;
            if (SelectedReceiptKind == "Payments only" && !r.IsPayment) return false;
            if (string.IsNullOrEmpty(q)) return true;
            return Contains(r.InvoiceDisplay, q)
                || Contains(r.BillId, q)
                || Contains(r.PaymentId, q)
                || MatchesCustomerName(r.CustomerName, q)
                || Contains(r.Method, q)
                || Contains(r.TypeLabel, q);
        }

        private static bool MatchesCustomer(CustomerSalesRow c, string q) =>
            string.IsNullOrEmpty(q) || MatchesCustomerName(c.CustomerName, q) || Contains(c.CustomerId, q);

        private static bool MatchesAccount(AccountReceiptRow a, string q) =>
            string.IsNullOrEmpty(q)
            || Contains(a.AccountName, q)
            || Contains(a.AccountType, q);

        private static bool Contains(string? text, string q) =>
            !string.IsNullOrEmpty(text) && text.Contains(q, StringComparison.OrdinalIgnoreCase);

        private string BuildFilterSummary()
        {
            var parts = new List<string>();
            if (SelectedPayment != "All") parts.Add($"Payment: {SelectedPayment}");
            if (AccountFilter.HasValue) parts.Add($"Account: {SelectedAccountFilter!.DisplayName}");
            if (!string.IsNullOrWhiteSpace(SearchQuery)) parts.Add($"Search: {SearchQuery.Trim()}");
            if (SelectedSection == "Payments" && SelectedReceiptKind != "All")
                parts.Add($"Show: {SelectedReceiptKind}");
            return string.Join("  ·  ", parts);
        }

        private void OpenItem(ItemSalesRow? row)
        {
            if (row == null) return;
            SelectedItem = row;
            ItemLines.Clear();
            var (start, end) = Range();
            foreach (var line in _reportService.GetItemLineDetails(start, end, row.ItemKey, PaymentFilter, AccountFilter))
                ItemLines.Add(line);
            var qty = ItemLines.Sum(l => l.Quantity);
            var rev = ItemLines.Sum(l => l.LineTotal);
            ItemDetailFooter = $"Total Quantity: {qty:0.###}     Total Revenue: Rs. {rev:N0}";
            OnPropertyChanged(nameof(ItemDetailFooter));
            IsItemDetailOpen = true;
        }

        private void OpenAccount(AccountReceiptRow? row)
        {
            if (row == null) return;
            SelectedAccountRow = row;
            AccountBills.Clear();
            var (start, end) = Range();
            var bills = _reportService.GetSaleBills(start, end, "Online", row.AccountId);
            if (!row.AccountId.HasValue)
                bills = bills.FindAll(b => !b.AccountId.HasValue);
            foreach (var b in bills)
                AccountBills.Add(b);
            IsAccountDetailOpen = true;
        }

        private void OpenCustomer(CustomerSalesRow? row)
        {
            if (row == null) return;
            SelectedCustomer = row;
            CustomerBills.Clear();
            var (start, end) = Range();
            var id = string.IsNullOrWhiteSpace(row.CustomerId) ? "" : row.CustomerId;
            foreach (var b in _reportService.GetSaleBills(start, end, PaymentFilter, AccountFilter, id))
                CustomerBills.Add(b);
            IsCustomerDetailOpen = true;
        }

        private void ViewBill(Bill? bill)
        {
            if (bill == null) return;
            var full = _reportService.GetBillById(bill.BillId) ?? bill;
            full.Customer ??= bill.Customer;
            full.ApplyReceiptMeta(_authService.CurrentUser?.FullName ?? "Cashier");
            SelectedHistoryBill = full;
            IsBillDetailOpen = true;
        }

        private void PrintBill(Bill? bill)
        {
            if (bill == null) return;
            try
            {
                var full = _reportService.GetBillById(bill.BillId) ?? bill;
                if (!_printService.PrintReceipt(full, _authService.CurrentUser?.FullName ?? "Cashier"))
                    ShowPopupError("Failed to communicate with the printer.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Reports bill print failed", ex);
                ShowPopupError("Failed to print bill.");
            }
        }

        private IEnumerable<Bill> FilteredBills() => _rawBills.Where(b => MatchesBill(b, SearchQuery?.Trim() ?? ""));

        private IEnumerable<ItemSalesRow> FilteredItems() => _rawItems.Where(i => MatchesItem(i, SearchQuery?.Trim() ?? ""));

        private IEnumerable<CustomerSalesRow> FilteredCustomers() =>
            _rawCustomers.Where(c => MatchesCustomer(c, SearchQuery?.Trim() ?? ""));

        private IEnumerable<ReceiptLedgerRow> FilteredReceipts() =>
            _rawReceipts.Where(r => MatchesReceipt(r, SearchQuery?.Trim() ?? ""));

        private IEnumerable<AccountReceiptRow> FilteredAccounts() =>
            _rawAccounts.Where(a => MatchesAccount(a, SearchQuery?.Trim() ?? ""));

        private void ExportCsv()
        {
            try
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel CSV (*.csv)|*.csv",
                    FileName = $"PMC_{SelectedSection}_{FromDate:yyyyMMdd}_{ToDate:yyyyMMdd}.csv"
                };
                if (sfd.ShowDialog() != true) return;

                var csv = new StringBuilder();
                csv.AppendLine("PMC — Pak Madinah Commission Agents");
                csv.AppendLine($"{SelectedSection} report");
                csv.AppendLine(PeriodLabel);
                if (!string.IsNullOrWhiteSpace(ActiveFiltersText))
                    csv.AppendLine(ActiveFiltersText);
                csv.AppendLine($"Generated,{DateTime.Now:yyyy-MM-dd HH:mm}");
                csv.AppendLine();
                csv.AppendLine($"Gross Sales,{Kpis.GrossSales:N2}");
                csv.AppendLine($"Discounts,{Kpis.Discounts:N2}");
                csv.AppendLine($"Net Sales,{Kpis.NetSales:N2}");
                csv.AppendLine($"Bills,{Kpis.BillCount}");
                csv.AppendLine($"Quantity Sold,{Kpis.QuantitySold:0.###}");
                csv.AppendLine($"Average Bill,{Kpis.AverageBillValue:N2}");
                csv.AppendLine($"Cash Received,{Kpis.CashReceived:N2}");
                csv.AppendLine($"Online Received,{Kpis.OnlineReceived:N2}");
                csv.AppendLine();

                if (SelectedSection is "Items" or "Overview")
                {
                    csv.AppendLine("Item,Quantity Sold,Revenue,Bills,Sales Lines");
                    foreach (var i in FilteredItems())
                        csv.AppendLine($"\"{i.ItemName.Replace("\"", "\"\"")}\",{i.QuantitySold},{i.Revenue:N2},{i.BillCount},{i.LineCount}");
                }
                if (SelectedSection is "Bills" or "Sales" or "Overview")
                {
                    csv.AppendLine("DateTime,Invoice,Customer,Subtotal,Previous Credit,Total,Received,Pending Credit,Status,Payment ");
                    foreach (var b in FilteredBills())
                        csv.AppendLine($"{b.BillDateTime:yyyy-MM-dd HH:mm},{b.BillId},\"{(b.CustomerDisplayName ?? "").Replace("\"", "\"\"")}\",{b.SubTotal:N2},{b.PreviousBalance:N2},{b.RowTotalBanam:N2},{b.AppliedReceived:N2},{b.Credit:N2},{b.ReportStatusLabel},{b.PaymentDisplayText}");
                }
                if (SelectedSection == "Customers")
                {
                    csv.AppendLine("Customer,Bills,Purchases,Payments,Outstanding,Last Transaction");
                    foreach (var c in FilteredCustomers())
                        csv.AppendLine($"\"{c.CustomerName.Replace("\"", "\"\"")}\",{c.BillCount},{c.TotalPurchases:N2},{c.Payments:N2},{c.Outstanding:N2},{c.LastTransactionDisplay}");
                }
                if (SelectedSection == "Payments")
                {
                    csv.AppendLine("Type,Invoice,DateTime,Customer,Method,Received");
                    foreach (var r in FilteredReceipts())
                        csv.AppendLine($"{r.TypeLabel},{r.InvoiceDisplay},{r.DateTime:yyyy-MM-dd HH:mm},\"{r.CustomerName.Replace("\"", "\"\"")}\",\"{r.Method.Replace("\"", "\"\"")}\",{r.Received:N2}");
                }
                if (SelectedSection == "Accounts")
                {
                    csv.AppendLine("Account/Method,Transactions,Amount");
                    foreach (var a in FilteredAccounts())
                        csv.AppendLine($"\"{a.AccountName.Replace("\"", "\"\"")}\",{a.TransactionCount},{a.AmountReceived:N2}");
                    foreach (var p in PaymentMethodStats)
                        csv.AppendLine($"\"{p.Method}\",{p.TransactionCount},{p.Amount:N2}");
                }

                File.WriteAllText(sfd.FileName, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                ShowPopupSuccess("Report exported.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Report CSV export failed", ex);
                ShowPopupError("Unable to export the report.");
            }
        }

        private List<(string Label, string Value)> ExportKpis() => new()
        {
            ("Total Sales", Kpis.GrossSalesDisplay),
            ("Customer Credits", Kpis.OutstandingCreditDisplay),
            ("Cash Received", Kpis.CashReceivedDisplay),
            ("Online Payments", Kpis.OnlineReceivedDisplay),
            ("Total Received", Kpis.TotalReceivedDisplay),
            ("Discount", Kpis.DiscountDisplay),
            ("Bills", Kpis.BillCount.ToString()),
            ("Quantity Sold", Kpis.QuantityDisplay)
        };

        private (string[] Headers, List<string[]> Rows) BuildExportTable()
        {
            var rows = new List<string[]>();
            switch (SelectedSection)
            {
                case "Items":
                    foreach (var i in FilteredItems())
                        rows.Add(new[] { i.ItemName, i.QuantityDisplay, i.RevenueDisplay, i.BillCount.ToString(), i.LineCount.ToString() });
                    return (new[] { "Item", "Quantity Sold", "Revenue", "Bills", "Sales Lines" }, rows);

                case "Customers":
                    foreach (var c in FilteredCustomers())
                        rows.Add(new[] { c.CustomerName, c.BillCount.ToString(), c.PurchasesDisplay, c.PaymentsDisplay, c.OutstandingDisplay, c.LastTransactionDisplay });
                    return (new[] { "Customer", "Bills", "Purchases", "Payments", "Credit Due", "Last Transaction" }, rows);

                case "Accounts":
                    foreach (var a in FilteredAccounts())
                        rows.Add(new[] { a.AccountName, a.AccountType ?? "", a.TransactionCount.ToString(), a.AmountDisplay });
                    return (new[] { "Account", "Type", "Transactions", "Amount Received" }, rows);

                case "Payments":
                    foreach (var r in FilteredReceipts())
                        rows.Add(new[] { r.TypeLabel, r.InvoiceDisplay, r.DateDisplay, r.CustomerName, r.Method, r.ReceivedDisplay });
                    return (new[] { "Type", "Invoice", "Date", "Customer", "Method", "Received" }, rows);

                case "Bills":
                    foreach (var b in FilteredBills())
                        rows.Add(new[]
                        {
                            b.BillDateTime.ToString("dd MMM yyyy HH:mm"),
                            b.InvoiceDisplay,
                            b.CustomerDisplayName ?? "",
                            $"Rs. {b.SubTotal:N0}",
                            $"Rs. {b.PreviousBalance:N0}",
                            $"Rs. {b.RowTotalBanam:N0}",
                            $"Rs. {b.AppliedReceived:N0}",
                            $"Rs. {b.Credit:N0}",
                            b.ReportStatusLabel,
                            b.PaymentDisplayText
                        });
                    return (new[] { "DateTime", "Invoice", "Customer", "Subtotal", "Previous Credit", "Total", "Received", "Pending", "Status", "Payment" }, rows);

                default:
                    foreach (var b in FilteredBills())
                        rows.Add(new[]
                        {
                            b.InvoiceDisplay,
                            b.BillDateTime.ToString("dd MMM yyyy HH:mm"),
                            b.CustomerDisplayName ?? "",
                            $"Rs. {b.GrandTotal:N0}",
                            $"Rs. {b.AppliedReceived:N0}",
                            b.PaymentDisplayText
                        });
                    return (new[] { "Invoice", "Date", "Customer", "Net", "Received", "Payment" }, rows);
            }
        }

        private void SaveReportPdf()
        {
            try
            {
                var (headers, rows) = BuildExportTable();
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "PDF (*.pdf)|*.pdf",
                    FileName = $"PMC_{SelectedSection}_{SelectedPreset.Replace(" ", "")}_{FromDate:yyyyMMdd}_{ToDate:yyyyMMdd}.pdf"
                };
                if (sfd.ShowDialog() != true) return;

                ReportPdfService.Save(sfd.FileName, new ReportPdfModel
                {
                    Title = $"{SelectedSection} report  ·  {SelectedPreset}",
                    Period = PeriodLabel,
                    Filters = ActiveFiltersText,
                    Kpis = ExportKpis(),
                    Headers = headers,
                    Rows = rows
                });
                ShowPopupSuccess("PDF saved.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Report PDF save failed", ex);
                ShowPopupError("Unable to save the PDF.");
            }
        }

        private void PrintReport()
        {
            try
            {
                var (headers, rows) = BuildExportTable();
                var doc = new FlowDocument
                {
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    FontSize = 11,
                    PagePadding = new Thickness(40),
                    ColumnWidth = double.PositiveInfinity
                };
                doc.Blocks.Add(new Paragraph(new Run("PMC — Pak Madinah Commission Agents")) { FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) });
                doc.Blocks.Add(new Paragraph(new Run($"{SelectedSection} report  ·  {SelectedPreset}")) { FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
                var meta = string.IsNullOrWhiteSpace(ActiveFiltersText)
                    ? $"{PeriodLabel}   ·   Generated {DateTime.Now:dd MMM yyyy HH:mm}"
                    : $"{PeriodLabel}   ·   {ActiveFiltersText}   ·   Generated {DateTime.Now:dd MMM yyyy HH:mm}";
                doc.Blocks.Add(new Paragraph(new Run(meta)) { Foreground = System.Windows.Media.Brushes.DimGray, Margin = new Thickness(0, 0, 0, 8) });
                doc.Blocks.Add(new Paragraph(new Run(
                    $"Sales {Kpis.GrossSalesDisplay}  ·  Cash {Kpis.CashReceivedDisplay}  ·  Online {Kpis.OnlineReceivedDisplay}  ·  Total Received {Kpis.TotalReceivedDisplay}  ·  Credit {Kpis.OutstandingCreditDisplay}  ·  Discount {Kpis.DiscountDisplay}"))
                { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });

                var table = new Table { CellSpacing = 0 };
                for (int i = 0; i < headers.Length; i++)
                    table.Columns.Add(new TableColumn());
                table.RowGroups.Add(new TableRowGroup());

                var head = new TableRow();
                foreach (var c in headers)
                {
                    var cell = new TableCell(new Paragraph(new Run(c)) { FontWeight = FontWeights.Bold, Margin = new Thickness(0, 2, 8, 2) });
                    cell.BorderBrush = System.Windows.Media.Brushes.Black;
                    cell.BorderThickness = new Thickness(0, 0, 0, 1);
                    head.Cells.Add(cell);
                }
                table.RowGroups[0].Rows.Add(head);

                foreach (var r in rows)
                {
                    var row = new TableRow();
                    foreach (var c in r)
                        row.Cells.Add(new TableCell(new Paragraph(new Run(c)) { Margin = new Thickness(0, 2, 8, 2) }));
                    table.RowGroups[0].Rows.Add(row);
                }

                doc.Blocks.Add(table);
                doc.Blocks.Add(new Paragraph(new Run("Figures follow the selected period and filters. Opening-balance bills are excluded from sales."))
                {
                    FontSize = 9,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    Margin = new Thickness(0, 16, 0, 0)
                });

                var dlg = new System.Windows.Controls.PrintDialog();
                if (dlg.ShowDialog() == true)
                {
                    doc.PageHeight = dlg.PrintableAreaHeight;
                    doc.PageWidth = dlg.PrintableAreaWidth;
                    dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, $"PMC {SelectedSection} report");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Report print failed", ex);
                ShowPopupError("Unable to print the report.");
            }
        }
    }

    public class CashierStat
    {
        public string CashierName { get; set; } = "";
        public int BillCount { get; set; }
        public double Revenue { get; set; }
        public string DisplayRevenue => $"Rs. {Revenue:N0}";
    }

    public class PaymentMethodStat
    {
        public string Method { get; set; } = "";
        public double Amount { get; set; }
        public double Percent { get; set; }
        public string DisplayAmount => $"Rs. {Amount:N0}";
        public string DisplayPercent => $"{Percent:N1}%";
    }
}
