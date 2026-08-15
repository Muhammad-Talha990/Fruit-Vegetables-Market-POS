using System;
using System.Runtime.Versioning;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using FruitVegetableMarketPOS.Models;
using FruitVegetableMarketPOS.Services;
using FruitVegetableMarketPOS.Helpers;

namespace FruitVegetableMarketPOS.ViewModels
{
    /// <summary>
    /// ViewModel for the Reports screen.
    /// Supports Daily, Weekly, Monthly, Custom, and Product-wise reports.
    /// Also powers the Analytics Dashboard with chart data.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class ReportsViewModel : BaseViewModel
    {
        private readonly ReportService _reportService;
        private readonly PrintService _printService;
        private readonly AuthService _authService;
        private readonly IReturnService _returnService;
        private readonly DailyClosingService _dailyClosingService;
        private readonly DailyItemSelectionService _dailyItemSelectionService;

        // ── Tab state ──────────────────────────────────────────────────────────
        private int _selectedTabIndex = 0;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set { if (SetProperty(ref _selectedTabIndex, value)) GenerateReport(); }
        }

        // ── Collections ────────────────────────────────────────────────────────
        public ObservableCollection<Bill>           SalesReport         { get; } = new();
        public ObservableCollection<ReportItem>     ProductReport       { get; } = new();
        public ObservableCollection<DailyItemSetRow> DailyItemHistory { get; } = new();
        public ObservableCollection<ChartDataPoint> DailySalesChart     { get; } = new();
        public ObservableCollection<ChartDataPoint> TopProductsChart    { get; } = new();
        public ObservableCollection<CashierStat>    CashierPerformance  { get; } = new();
        public ObservableCollection<PaymentMethodStat> PaymentMethodStats { get; } = new();

        private DailyClosing? _dailyClosingPreview;
        public DailyClosing? DailyClosingPreview
        {
            get => _dailyClosingPreview;
            set => SetProperty(ref _dailyClosingPreview, value);
        }

        private bool _showDailyItemGrid;
        public bool ShowDailyItemGrid { get => _showDailyItemGrid; set => SetProperty(ref _showDailyItemGrid, value); }

        private bool _showDailyClosingPanel;
        public bool ShowDailyClosingPanel { get => _showDailyClosingPanel; set => SetProperty(ref _showDailyClosingPanel, value); }

        private bool _showDailySaleQtyGrid;
        public bool ShowDailySaleQtyGrid { get => _showDailySaleQtyGrid; set => SetProperty(ref _showDailySaleQtyGrid, value); }

        private string _dailyClosingStatus = "";
        public string DailyClosingStatus { get => _dailyClosingStatus; set => SetProperty(ref _dailyClosingStatus, value); }

        private List<Bill> _currentRawBills = new();

        // ── Search & Filter ────────────────────────────────────────────────────
        private string _searchQuery = "";
        public string SearchQuery
        {
            get => _searchQuery;
            set { if (SetProperty(ref _searchQuery, value)) ApplyFilters(); }
        }

        public List<string> AvailableBillFilters { get; } = new()
            { "All Transactions", "Sales Only", "Returns Only", "Credit Bills", "Paid Bills" };

        private string _selectedBillFilter = "All Transactions";
        public string SelectedBillFilter
        {
            get => _selectedBillFilter;
            set { if (SetProperty(ref _selectedBillFilter, value)) ApplyFilters(); }
        }

        // ── Date range ─────────────────────────────────────────────────────────
        private DateTime _fromDate = DateTime.Today;
        public DateTime FromDate
        {
            get => _fromDate;
            set
            {
                if (SetProperty(ref _fromDate, value))
                {
                    if (_fromDate > ToDate) SetProperty(ref _toDate, _fromDate, nameof(ToDate));
                    GenerateReport();
                }
            }
        }

        private DateTime _toDate = DateTime.Today;
        public DateTime ToDate
        {
            get => _toDate;
            set
            {
                if (SetProperty(ref _toDate, value))
                {
                    if (_toDate < FromDate) SetProperty(ref _fromDate, _toDate, nameof(FromDate));
                    GenerateReport();
                }
            }
        }

        private string _selectedReportType = "Daily";
        public string SelectedReportType
        {
            get => _selectedReportType;
            set { if (SetProperty(ref _selectedReportType, value)) GenerateReport(); }
        }

        // ── KPI Summary ────────────────────────────────────────────────────────
        private double _totalRevenue;
        public double TotalRevenue { get => _totalRevenue; set => SetProperty(ref _totalRevenue, value); }

        private int _totalSalesCount;
        public int TotalSalesCount { get => _totalSalesCount; set => SetProperty(ref _totalSalesCount, value); }

        private double _totalReturns;
        public double TotalReturns { get => _totalReturns; set => SetProperty(ref _totalReturns, value); }

        private double _netSales;
        public double NetSales { get => _netSales; set => SetProperty(ref _netSales, value); }

        private double _outstandingCredit;
        public double OutstandingCredit { get => _outstandingCredit; set => SetProperty(ref _outstandingCredit, value); }

        private double _avgOrderValue;
        public double AvgOrderValue { get => _avgOrderValue; set => SetProperty(ref _avgOrderValue, value); }

        private int _totalReturnCount;
        public int TotalReturnCount { get => _totalReturnCount; set => SetProperty(ref _totalReturnCount, value); }

        // ── Visibility Flags ───────────────────────────────────────────────────
        private bool _showSalesGrid = true;
        public bool ShowSalesGrid { get => _showSalesGrid; set => SetProperty(ref _showSalesGrid, value); }

        private bool _showProductGrid;
        public bool ShowProductGrid { get => _showProductGrid; set => SetProperty(ref _showProductGrid, value); }

        private bool _isToDateVisible;
        public bool IsToDateVisible { get => _isToDateVisible; set => SetProperty(ref _isToDateVisible, value); }

        private bool _isFromDateVisible = true;
        public bool IsFromDateVisible { get => _isFromDateVisible; set => SetProperty(ref _isFromDateVisible, value); }

        private string _fromDateLabel = "Report Date";
        public string FromDateLabel { get => _fromDateLabel; set => SetProperty(ref _fromDateLabel, value); }

        private bool _isRevenueVisible = true;
        public bool IsRevenueVisible { get => _isRevenueVisible; set => SetProperty(ref _isRevenueVisible, value); }

        // ── Bill Detail Overlay ────────────────────────────────────────────────
        private Bill? _selectedHistoryBill;
        public Bill? SelectedHistoryBill
        {
            get => _selectedHistoryBill;
            set => SetProperty(ref _selectedHistoryBill, value);
        }

        private bool _isBillDetailOpen;
        public bool IsBillDetailOpen
        {
            get => _isBillDetailOpen;
            set => SetProperty(ref _isBillDetailOpen, value);
        }

        // ── Commands ───────────────────────────────────────────────────────────
        public ICommand ExportReportCommand     { get; }
        public ICommand ViewBillCommand         { get; }
        public ICommand PrintBillCommand        { get; }
        public ICommand CloseBillDetailCommand  { get; }
        public ICommand RefreshCommand          { get; }
        public ICommand CloseDayCommand         { get; }

        // ── Constructor ────────────────────────────────────────────────────────
        public ReportsViewModel(ReportService reportService,
                                PrintService printService, AuthService authService,
                                IReturnService returnService,
                                DailyClosingService dailyClosingService,
                                DailyItemSelectionService dailyItemSelectionService)
        {
            _reportService  = reportService;
            _printService   = printService;
            _authService    = authService;
            _returnService  = returnService;
            _dailyClosingService = dailyClosingService;
            _dailyItemSelectionService = dailyItemSelectionService;

            ExportReportCommand    = new RelayCommand(ExportReport);
            ViewBillCommand        = new RelayCommand(obj => ViewBill(obj as Bill));
            PrintBillCommand       = new RelayCommand(obj => PrintBill(obj as Bill));
            CloseBillDetailCommand = new RelayCommand(_ => CloseBillDetail());
            RefreshCommand         = new RelayCommand(_ => GenerateReport());
            CloseDayCommand        = new RelayCommand(_ => ExecuteCloseDay(), _ => _authService.IsAdmin && !(DailyClosingPreview?.IsClosed ?? false));

            AppEvents.DataChanged += OnAppDataChanged;
            GenerateReport();
        }

        /// <summary>Reload live figures whenever Reports is opened.</summary>
        public void OnActivated() => GenerateReport();

        private void OnAppDataChanged()
        {
            AppEvents.InvokeOnUi(GenerateReport);
        }
        // ── Main Report Generator ──────────────────────────────────────────────
        public void GenerateReport()
        {
            try
            {
                var type = SelectedReportType?.Trim() ?? "Daily";
                ConfigureUIState(type);
                var (start, end) = GetDateRange(type);

                if (string.Equals(type, "Product-wise", StringComparison.OrdinalIgnoreCase))
                    LoadProductReport(start, end);
                else if (string.Equals(type, "Type-wise", StringComparison.OrdinalIgnoreCase))
                    LoadTypeReport(start, end);
                else if (string.Equals(type, "Category-wise", StringComparison.OrdinalIgnoreCase))
                    LoadCategoryReport(start, end);
                else if (string.Equals(type, "Daily Sale Qty", StringComparison.OrdinalIgnoreCase))
                    LoadDailySaleQtyReport();
                else if (string.Equals(type, "Daily Items", StringComparison.OrdinalIgnoreCase))
                    LoadDailyItemsReport();
                else if (string.Equals(type, "Daily Closing", StringComparison.OrdinalIgnoreCase))
                    LoadDailyClosingReport();
                else
                    LoadSalesReport(type, start, end);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error generating report", ex);
            }
        }

        private void ConfigureUIState(string type)
        {
            ShowDailyItemGrid = false;
            ShowDailyClosingPanel = false;
            ShowDailySaleQtyGrid = false;

            if (type is "Custom Range" or "Product-wise" or "Type-wise" or "Category-wise")
            {
                IsFromDateVisible = true;
                IsToDateVisible   = true;
                FromDateLabel     = type == "Custom Range" ? "From Date" : "Start Date";
                IsRevenueVisible  = true;
            }
            else if (type is "Daily Items" or "Daily Closing" or "Daily Sale Qty")
            {
                IsFromDateVisible = true;
                IsToDateVisible   = false;
                FromDateLabel     = "Business Date";
                IsRevenueVisible  = type is "Daily Closing" or "Daily Sale Qty";
                ShowDailyItemGrid = type == "Daily Items";
                ShowDailyClosingPanel = type == "Daily Closing";
                ShowDailySaleQtyGrid = type == "Daily Sale Qty";
            }
            else
            {
                IsFromDateVisible = true;
                IsToDateVisible   = false;
                IsRevenueVisible  = true;
                FromDateLabel     = type switch
                {
                    "Monthly" => "Selected Month",
                    "Weekly"  => "Selected Week",
                    _         => "Report Date"
                };
            }
        }

        private (DateTime start, DateTime end) GetDateRange(string type)
        {
            DateTime start, end;
            if (type == "Daily")
            {
                start = FromDate.Date;
                end   = start.AddDays(1);
            }
            else if (type == "Weekly")
            {
                int diff = (7 + (FromDate.DayOfWeek - DayOfWeek.Monday)) % 7;
                start = FromDate.AddDays(-diff).Date;
                end   = start.AddDays(7);
            }
            else if (type == "Monthly")
            {
                start = new DateTime(FromDate.Year, FromDate.Month, 1);
                end   = start.AddMonths(1);
            }
            else // Custom or Product-wise
            {
                start = FromDate.Date;
                end   = ToDate.Date.AddDays(1);
            }
            return (start, end);
        }

        private void LoadProductReport(DateTime start, DateTime end)
        {
            ShowSalesGrid    = false;
            ShowProductGrid  = true;
            ShowDailyItemGrid = false;
            ShowDailyClosingPanel = false;
            ShowDailySaleQtyGrid = false;
            var data = _reportService.GetProductWiseReport(start, end);
            Dispatch(() =>
            {
                ProductReport.Clear();
                foreach (var r in data) ProductReport.Add(r);
                TotalRevenue    = data.Sum(r => r.TotalRevenue);
                TotalSalesCount = data.Count;
            });
        }

        private void LoadTypeReport(DateTime start, DateTime end)
        {
            ShowSalesGrid = false;
            ShowProductGrid = true;
            ShowDailyItemGrid = false;
            ShowDailyClosingPanel = false;
            ShowDailySaleQtyGrid = false;
            var data = _reportService.GetTypeWiseReport(start, end);
            Dispatch(() =>
            {
                ProductReport.Clear();
                foreach (var r in data) ProductReport.Add(r);
                TotalRevenue = data.Sum(r => r.TotalRevenue);
                TotalSalesCount = data.Count;
            });
        }

        private void LoadCategoryReport(DateTime start, DateTime end)
        {
            ShowSalesGrid = false;
            ShowProductGrid = true;
            ShowDailyItemGrid = false;
            ShowDailyClosingPanel = false;
            ShowDailySaleQtyGrid = false;
            var data = _reportService.GetCategoryWiseReport(start, end);
            Dispatch(() =>
            {
                ProductReport.Clear();
                foreach (var r in data) ProductReport.Add(r);
                TotalRevenue = data.Sum(r => r.TotalRevenue);
                TotalSalesCount = data.Count;
            });
        }

        private void LoadDailyItemsReport()
        {
            ShowSalesGrid = false;
            ShowProductGrid = false;
            ShowDailyItemGrid = true;
            ShowDailyClosingPanel = false;
            ShowDailySaleQtyGrid = false;
            var businessDate = DateTimeHelper.GetBusinessDate(FromDate);
            var rows = _dailyItemSelectionService.GetDailyItemSetForDate(businessDate);
            Dispatch(() =>
            {
                DailyItemHistory.Clear();
                foreach (var row in rows) DailyItemHistory.Add(row);
                TotalSalesCount = rows.Count;
                TotalRevenue = rows.Sum(r => r.Sale);
            });
        }

        /// <summary>
        /// Per-item sale qty for the selected business date (temp daily column).
        /// Columns: Item ID · Description · Type · Unit Price · Sale.
        /// </summary>
        private void LoadDailySaleQtyReport()
        {
            ShowSalesGrid = false;
            ShowProductGrid = false;
            ShowDailyItemGrid = false;
            ShowDailyClosingPanel = false;
            ShowDailySaleQtyGrid = true;

            var data = _reportService.GetDailySaleQtyReport(FromDate.Date);
            Dispatch(() =>
            {
                ProductReport.Clear();
                foreach (var r in data) ProductReport.Add(r);
                TotalSalesCount = data.Count;
                TotalRevenue = data.Sum(r => r.Amount);
                NetSales = TotalRevenue;
            });
        }

        private void LoadDailyClosingReport()
        {
            ShowSalesGrid = false;
            ShowProductGrid = false;
            ShowDailyItemGrid = false;
            ShowDailyClosingPanel = true;
            ShowDailySaleQtyGrid = false;
            var businessDate = DateTimeHelper.GetBusinessDate(FromDate);
            var preview = _dailyClosingService.GetByDate(businessDate) ?? _dailyClosingService.ComputeForDate(businessDate);
            Dispatch(() =>
            {
                DailyClosingPreview = preview;
                DailyClosingStatus = preview.IsClosed
                    ? $"Closed at {preview.ClosedAt:dd-MMM-yyyy HH:mm}"
                    : "Day is still OPEN — review totals then close.";
                TotalRevenue = preview.TotalSales;
                TotalSalesCount = preview.TotalBills;
                TotalReturns = preview.Refunds;
                NetSales = preview.NetSales;
                (CloseDayCommand as RelayCommand)?.RaiseCanExecuteChanged();
            });
        }

        private void ExecuteCloseDay()
        {
            try
            {
                if (!_authService.IsAdmin)
                {
                    System.Windows.MessageBox.Show("Only Admin can close the business day.", "Permission",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                var businessDate = DateTimeHelper.GetBusinessDate(FromDate);
                if (_dailyClosingService.IsClosed(businessDate))
                {
                    System.Windows.MessageBox.Show("This day is already closed.", "Daily Closing",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }

                var confirm = System.Windows.MessageBox.Show(
                    $"Close business day {businessDate}?\n\nThis stores today's sales summary. Historical bills remain available.",
                    "Confirm Daily Closing",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);
                if (confirm != System.Windows.MessageBoxResult.Yes) return;

                var closed = _dailyClosingService.CloseDay(businessDate, _authService.CurrentUser?.Id);
                DailyClosingPreview = closed;
                DailyClosingStatus = $"Closed at {closed.ClosedAt:dd-MMM-yyyy HH:mm}";
                (CloseDayCommand as RelayCommand)?.RaiseCanExecuteChanged();
                System.Windows.MessageBox.Show(
                    $"Day closed.\n\nBills: {closed.TotalBills}\nGross: Rs.{closed.TotalSales:N0}\nNet: Rs.{closed.NetSales:N0}",
                    "Daily Closing",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Daily closing failed", ex);
                System.Windows.MessageBox.Show(ex.Message, "Daily Closing Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void LoadSalesReport(string type, DateTime start, DateTime end)
        {
            ShowSalesGrid    = true;
            ShowProductGrid  = false;
            ShowDailyItemGrid = false;
            ShowDailyClosingPanel = false;
            ShowDailySaleQtyGrid = false;

            _currentRawBills = _reportService.GetByDateRange(start, end)
                .Where(b => !string.Equals(b.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Bills table is sales-only; returns are in BillReturns.
            var salesData = _currentRawBills.ToList();
            double returnsTotal = _reportService.GetReturnsTotalByDateRange(start, end);
            int returnsCount = _reportService.GetReturnsCountByDateRange(start, end);
            double creditTotal = _reportService.GetOutstandingCreditTotal();

            // Analytics — isolate failures so KPIs still show if one chart query fails
            var dailySeries = SafeQuery(
                () => _reportService.GetDailySalesSeries(start, end),
                new List<(DateTime Date, double TotalSales, double TotalReturns, int BillCount)>(),
                "GetDailySalesSeries");
            var topProducts = SafeQuery(
                () => _reportService.GetTopProductsSeries(start, end, 5),
                new List<(string Name, double Revenue, int Qty)>(),
                "GetTopProductsSeries");
            var paymentBreakdown = SafeQuery(
                () => _reportService.GetPaymentMethodBreakdownForRange(start, end),
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                "GetPaymentMethodBreakdownForRange");
            var cashierStats = SafeQuery(
                () => _reportService.GetCashierPerformance(start, end),
                new List<(string CashierName, int BillCount, double Revenue)>(),
                "GetCashierPerformance");

            Dispatch(() =>
            {
                TotalRevenue      = Math.Round(salesData.Sum(s => s.GrandTotal), 2);
                TotalSalesCount   = salesData.Count;
                TotalReturns      = Math.Round(returnsTotal, 2);
                TotalReturnCount  = returnsCount;
                NetSales          = Math.Round(TotalRevenue - returnsTotal, 2);
                OutstandingCredit = Math.Round(creditTotal, 2);
                AvgOrderValue     = TotalSalesCount > 0 ? Math.Round(TotalRevenue / TotalSalesCount, 2) : 0;

                ApplyFilters();

                // ── Daily Sales Chart ──
                DailySalesChart.Clear();
                foreach (var d in dailySeries)
                {
                    string label = type == "Monthly"
                        ? d.Date.ToString("MMM dd")
                        : type == "Weekly"
                            ? d.Date.ToString("ddd")
                            : d.Date.ToString("dd MMM");

                    DailySalesChart.Add(new ChartDataPoint
                    {
                        Label          = label,
                        Value          = d.TotalSales,
                        SecondaryValue = d.TotalReturns
                    });
                }

                // Single-day view: show hourly buckets from real bill timestamps
                if (type == "Daily" && salesData.Count > 0)
                {
                    DailySalesChart.Clear();
                    var hourlyGroups = salesData
                        .GroupBy(b => b.BillDateTime.Hour)
                        .OrderBy(g => g.Key);
                    foreach (var grp in hourlyGroups)
                    {
                        DailySalesChart.Add(new ChartDataPoint
                        {
                            Label = $"{grp.Key:00}:00",
                            Value = Math.Round(grp.Sum(b => b.GrandTotal), 2)
                        });
                    }
                }

                // ── Top Products Chart ──
                TopProductsChart.Clear();
                var productColors = new[]
                {
                    System.Windows.Media.Color.FromRgb(20, 184, 166),
                    System.Windows.Media.Color.FromRgb(99, 102, 241),
                    System.Windows.Media.Color.FromRgb(245, 158, 11),
                    System.Windows.Media.Color.FromRgb(239, 68, 68),
                    System.Windows.Media.Color.FromRgb(34, 197, 94)
                };
                int colorIdx = 0;
                foreach (var p in topProducts)
                {
                    // Keep full product name — chart wraps labels instead of truncating
                    TopProductsChart.Add(new ChartDataPoint
                    {
                        Label    = p.Name,
                        Value    = p.Revenue,
                        BarColor = productColors[colorIdx % productColors.Length]
                    });
                    colorIdx++;
                }

                // ── Payment Breakdown ──
                PaymentMethodStats.Clear();
                double total = paymentBreakdown.Values.Sum();
                foreach (var kv in paymentBreakdown)
                {
                    PaymentMethodStats.Add(new PaymentMethodStat
                    {
                        Method  = kv.Key,
                        Amount  = kv.Value,
                        Percent = total > 0 ? kv.Value / total * 100 : 0
                    });
                }

                // ── Cashier Stats ──
                CashierPerformance.Clear();
                foreach (var c in cashierStats)
                {
                    CashierPerformance.Add(new CashierStat
                    {
                        CashierName = c.CashierName,
                        BillCount   = c.BillCount,
                        Revenue     = c.Revenue
                    });
                }
            });
        }

        private static T SafeQuery<T>(Func<T> query, T fallback, string name)
        {
            try { return query(); }
            catch (Exception ex)
            {
                AppLogger.Error($"Reports analytics query failed: {name}", ex);
                return fallback;
            }
        }

        private void ApplyFilters()
        {
            if (_currentRawBills == null) return;

            var filtered = _currentRawBills.AsEnumerable();

            filtered = SelectedBillFilter switch
            {
                "Sales Only"   => filtered,
                "Returns Only" => filtered.Where(_ => false), // returns are not bill rows
                "Credit Bills" => filtered.Where(b => b.RemainingAmount > 0),
                "Paid Bills"   => filtered.Where(b => b.RemainingAmount <= 0),
                _              => filtered
            };

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var q = SearchQuery.Trim().ToLower();
                filtered = filtered.Where(b =>
                    b.InvoiceNumber.ToLower().Contains(q)
                    || (b.Customer?.FullName?.ToLower().Contains(q) ?? false)
                    || (b.User?.FullName?.ToLower().Contains(q) ?? false));
            }

            var finalResults = filtered.OrderByDescending(b => b.CreatedAt).ToList();

            Dispatch(() =>
            {
                SalesReport.Clear();
                foreach (var b in finalResults) SalesReport.Add(b);
            });
        }

        // ── Export ─────────────────────────────────────────────────────────────
        private void ExportReport()
        {
            try
            {
                if (ShowSalesGrid    && SalesReport.Count    == 0) return;
                if (ShowProductGrid  && ProductReport.Count  == 0) return;
                if (ShowDailySaleQtyGrid && ProductReport.Count == 0) return;

                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Filter   = "CSV files (*.csv)|*.csv",
                    FileName = $"Report_{SelectedReportType}_{DateTime.Now:yyyyMMdd_HHmm}.csv"
                };

                if (sfd.ShowDialog() == true)
                {
                    var csv = new System.Text.StringBuilder();

                    if (ShowSalesGrid)
                    {
                        csv.AppendLine("Invoice #,Type,Customer,Cashier,Payment Method,Grand Total,Paid,Remaining,Status,Date/Time");
                        foreach (var b in SalesReport)
                        {
                            csv.AppendLine($"{b.InvoiceNumber},{b.Type},{b.Customer?.FullName ?? "Walk-in"},{b.User?.FullName ?? "Unknown"},{b.PaymentMethod},{b.GrandTotal},{b.PaidAmount},{b.RemainingAmount},{b.PaymentStatus},{b.BillDateTime:yyyy-MM-dd HH:mm}");
                        }
                        csv.AppendLine($"TOTAL,,,,,,{SalesReport.Sum(b => b.GrandTotal)},{SalesReport.Sum(b => b.PaidAmount)},{SalesReport.Sum(b => b.RemainingAmount)},,");
                    }
                    else if (ShowDailySaleQtyGrid)
                    {
                        csv.AppendLine("Item ID,Item Description,Type,Unit Price,Sale,Amount");
                        foreach (var p in ProductReport)
                            csv.AppendLine($"{p.ItemId},\"{p.ItemDescription}\",\"{p.TypeName}\",{p.UnitPrice},{p.Sale},{p.Amount}");
                        csv.AppendLine($",,,,{ProductReport.Sum(p => p.Sale)},{ProductReport.Sum(p => p.Amount)}");
                    }
                    else if (ShowProductGrid)
                    {
                        csv.AppendLine("Product Name,Quantity Sold,Total Revenue");
                        foreach (var p in ProductReport)
                            csv.AppendLine($"\"{p.ItemDescription}\",{p.QuantitySold},{p.TotalRevenue}");
                        csv.AppendLine($"TOTAL,{ProductReport.Sum(p => p.QuantitySold)},{ProductReport.Sum(p => p.TotalRevenue)}");
                    }

                    System.IO.File.WriteAllText(sfd.FileName, csv.ToString());
                    AppLogger.Info($"Report exported to {sfd.FileName}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error exporting report", ex);
            }
        }

        // ── View / Print Bill ──────────────────────────────────────────────────
        private async void ViewBill(Bill? bill)
        {
            if (bill == null) return;
            if (bill.IsReturn && bill.ParentBillId.HasValue)
            {
                try
                {
                    var (original, returns) = await _returnService.GetBillWithReturnHistory(bill.ParentBillId.Value);
                    bill.ParentBill    = original;
                    bill.ReturnHistory = new ObservableCollection<Bill>(returns.Where(r => r.BillId != bill.BillId));
                    double prevReturns = returns.Where(r => r.BillId < bill.BillId).Sum(r => Math.Abs(r.GrandTotal));
                    bill.RemainingDueAfterThisReturn = original.GrandTotal - prevReturns - Math.Abs(bill.GrandTotal);
                }
                catch (Exception ex) { AppLogger.Error("Failed to fetch return metadata for view", ex); }
            }
            SelectedHistoryBill = bill;
            IsBillDetailOpen    = true;
        }

        private async void PrintBill(Bill? bill)
        {
            if (bill == null) return;
            if (bill.IsReturn && bill.ParentBillId.HasValue && bill.ParentBill == null)
            {
                try
                {
                    var (original, returns) = await _returnService.GetBillWithReturnHistory(bill.ParentBillId.Value);
                    bill.ParentBill    = original;
                    bill.ReturnHistory = new ObservableCollection<Bill>(returns.Where(r => r.BillId != bill.BillId));
                    double prevReturns = returns.Where(r => r.BillId < bill.BillId).Sum(r => Math.Abs(r.GrandTotal));
                    bill.RemainingDueAfterThisReturn = original.GrandTotal - prevReturns - Math.Abs(bill.GrandTotal);
                }
                catch (Exception ex) { AppLogger.Error("Failed to fetch return metadata for print", ex); }
            }

            bool isOnline = _printService.IsPrinterOnline();
            if (isOnline)
            {
                bool ok = _printService.PrintReceipt(bill, _authService.CurrentUser?.FullName ?? "System Admin");
                if (!ok)
                    System.Windows.MessageBox.Show("Failed to communicate with the printer.", "Print Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            else
            {
                System.Windows.MessageBox.Show("Printer is currently unavailable or offline.", "Printer Offline",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void CloseBillDetail()
        {
            IsBillDetailOpen    = false;
            SelectedHistoryBill = null;
        }
    }

    // ── Helper DTOs ────────────────────────────────────────────────────────────

    public class CashierStat
    {
        public string CashierName { get; set; } = "";
        public int    BillCount   { get; set; }
        public double Revenue     { get; set; }
        public string DisplayRevenue => $"Rs. {Revenue:N0}";
    }

    public class PaymentMethodStat
    {
        public string Method  { get; set; } = "";
        public double Amount  { get; set; }
        public double Percent { get; set; }
        public string DisplayAmount  => $"Rs. {Amount:N0}";
        public string DisplayPercent => $"{Percent:N1}%";
    }
}
