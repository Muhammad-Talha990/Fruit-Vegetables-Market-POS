using System;
using System.Runtime.Versioning;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using GroceryPOS.Models;
using GroceryPOS.Services;
using GroceryPOS.Helpers;

namespace GroceryPOS.ViewModels
{
    /// <summary>
    /// ViewModel for the Reports screen.
    /// Supports Daily, Weekly, Monthly, Custom, Product-wise, and Low-Stock reports.
    /// Also powers the Analytics Dashboard with chart data.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class ReportsViewModel : BaseViewModel
    {
        private readonly ReportService _reportService;
        private readonly IStockService _stockService;
        private readonly PrintService _printService;
        private readonly AuthService _authService;
        private readonly IReturnService _returnService;

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
        public ObservableCollection<Item>           LowStockReport      { get; } = new();
        public ObservableCollection<ChartDataPoint> DailySalesChart     { get; } = new();
        public ObservableCollection<ChartDataPoint> TopProductsChart    { get; } = new();
        public ObservableCollection<CashierStat>    CashierPerformance  { get; } = new();
        public ObservableCollection<PaymentMethodStat> PaymentMethodStats { get; } = new();

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

        private bool _showLowStockGrid;
        public bool ShowLowStockGrid { get => _showLowStockGrid; set => SetProperty(ref _showLowStockGrid, value); }

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

        // ── Constructor ────────────────────────────────────────────────────────
        public ReportsViewModel(ReportService reportService, IStockService stockService,
                                PrintService printService, AuthService authService,
                                IReturnService returnService)
        {
            _reportService  = reportService;
            _stockService   = stockService;
            _printService   = printService;
            _authService    = authService;
            _returnService  = returnService;

            ExportReportCommand    = new RelayCommand(ExportReport);
            ViewBillCommand        = new RelayCommand(obj => ViewBill(obj as Bill));
            PrintBillCommand       = new RelayCommand(obj => PrintBill(obj as Bill));
            CloseBillDetailCommand = new RelayCommand(_ => CloseBillDetail());
            RefreshCommand         = new RelayCommand(_ => GenerateReport());

            _stockService.StockChanged += GenerateReport;
            GenerateReport();
        }

        public override void Dispose()
        {
            if (_stockService != null)
                _stockService.StockChanged -= GenerateReport;
            base.Dispose();
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
                else if (string.Equals(type, "Low Stock", StringComparison.OrdinalIgnoreCase))
                    LoadLowStockReport();
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
            if (type == "Custom Range" || type == "Product-wise")
            {
                IsFromDateVisible = true;
                IsToDateVisible   = true;
                FromDateLabel     = type == "Custom Range" ? "From Date" : "Start Date";
                IsRevenueVisible  = true;
            }
            else if (type == "Low Stock")
            {
                IsFromDateVisible = false;
                IsToDateVisible   = false;
                IsRevenueVisible  = false;
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
            ShowLowStockGrid = false;
            var data = _reportService.GetProductWiseReport(start, end);
            Dispatch(() =>
            {
                ProductReport.Clear();
                foreach (var r in data) ProductReport.Add(r);
                TotalRevenue    = data.Sum(r => r.TotalRevenue);
                TotalSalesCount = data.Sum(r => r.QuantitySold);
            });
        }

        private void LoadLowStockReport()
        {
            ShowSalesGrid    = false;
            ShowProductGrid  = false;
            ShowLowStockGrid = true;
            var data = _stockService.GetLowStockItems();
            Dispatch(() =>
            {
                LowStockReport.Clear();
                foreach (var i in data) LowStockReport.Add(i);
                TotalRevenue    = 0;
                TotalSalesCount = data.Count;
            });
        }

        private void LoadSalesReport(string type, DateTime start, DateTime end)
        {
            ShowSalesGrid    = true;
            ShowProductGrid  = false;
            ShowLowStockGrid = false;

            _currentRawBills = _reportService.GetByDateRange(start, end);

            var salesData   = _currentRawBills.Where(b => b.Type == "Sale").ToList();
            var returnData  = _currentRawBills.Where(b => b.Type == "Return").ToList();

            double returnsTotal = _reportService.GetReturnsTotalByDateRange(start, end);
            double creditTotal  = _reportService.GetOutstandingCreditTotal();

            // Load analytics chart data
            var dailySeries     = _reportService.GetDailySalesSeries(start, end);
            var topProducts     = _reportService.GetTopProductsSeries(start, end, 5);
            var paymentBreakdown = _reportService.GetPaymentMethodBreakdownForRange(start, end);
            var cashierStats    = _reportService.GetCashierPerformance(start, end);

            Dispatch(() =>
            {
                TotalRevenue      = salesData.Sum(s => s.GrandTotal);
                TotalSalesCount   = salesData.Count;
                TotalReturns      = returnsTotal;
                TotalReturnCount  = returnData.Count;
                NetSales          = TotalRevenue - returnsTotal;
                OutstandingCredit = creditTotal;
                AvgOrderValue     = TotalSalesCount > 0 ? TotalRevenue / TotalSalesCount : 0;

                ApplyFilters();

                // ── Daily Sales Chart ──
                DailySalesChart.Clear();
                foreach (var d in dailySeries)
                {
                    string label = type == "Monthly"
                        ? d.Date.ToString("MMM dd")
                        : type == "Weekly"
                            ? d.Date.ToString("ddd")
                            : d.Date.ToString("HH:mm");

                    DailySalesChart.Add(new ChartDataPoint
                    {
                        Label          = label,
                        Value          = d.TotalSales,
                        SecondaryValue = d.TotalReturns
                    });
                }

                // If daily (single day), use hourly mock-up from existing bills
                if (type == "Daily" && dailySeries.Count <= 1 && salesData.Count > 0)
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
                            Value = grp.Sum(b => b.GrandTotal)
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
                    // Truncate long product names
                    string name = p.Name.Length > 14 ? p.Name.Substring(0, 12) + "…" : p.Name;
                    TopProductsChart.Add(new ChartDataPoint
                    {
                        Label    = name,
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

        private void ApplyFilters()
        {
            if (_currentRawBills == null) return;

            var filtered = _currentRawBills.AsEnumerable();

            filtered = SelectedBillFilter switch
            {
                "Sales Only"   => filtered.Where(b => b.Type == "Sale"),
                "Returns Only" => filtered.Where(b => b.Type == "Return"),
                "Credit Bills" => filtered.Where(b => b.Type == "Sale" && b.RemainingAmount > 0),
                "Paid Bills"   => filtered.Where(b => b.Type == "Sale" && b.RemainingAmount <= 0),
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
                if (ShowLowStockGrid && LowStockReport.Count == 0) return;

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
                    else if (ShowProductGrid)
                    {
                        csv.AppendLine("Product Name,Quantity Sold,Total Revenue");
                        foreach (var p in ProductReport)
                            csv.AppendLine($"\"{p.ItemDescription}\",{p.QuantitySold},{p.TotalRevenue}");
                        csv.AppendLine($"TOTAL,{ProductReport.Sum(p => p.QuantitySold)},{ProductReport.Sum(p => p.TotalRevenue)}");
                    }
                    else if (ShowLowStockGrid)
                    {
                        csv.AppendLine("Barcode,Product Name,Category,Current Stock,Threshold");
                        foreach (var i in LowStockReport)
                            csv.AppendLine($"{i.Barcode},\"{i.Description}\",\"{i.ItemCategory}\",{i.StockQuantity},{i.MinStockThreshold}");
                        csv.AppendLine($"TOTAL ITEMS,{LowStockReport.Count},,,,");
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
