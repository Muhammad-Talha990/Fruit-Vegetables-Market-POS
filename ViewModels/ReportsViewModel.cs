using System;
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
    /// Supports Daily, Monthly, and Product-wise reports using the new Bill schema.
    /// </summary>
    public class ReportsViewModel : BaseViewModel
    {
        private readonly ReportService _reportService;
        private readonly IStockService _stockService;
        private readonly PrintService _printService;
        private readonly AuthService _authService;
        private readonly IReturnService _returnService;

        public ObservableCollection<Bill> SalesReport { get; set; } = new();
        public ObservableCollection<ReportItem> ProductReport { get; set; } = new();
        public ObservableCollection<Item> LowStockReport { get; set; } = new();

        private List<Bill> _currentRawBills = new();

        private string _searchQuery = "";
        public string SearchQuery
        {
            get => _searchQuery;
            set { if (SetProperty(ref _searchQuery, value)) ApplyFilters(); }
        }

        public List<string> AvailableBillFilters { get; } = new() { "All Bills", "Normal Bills", "Credit Bills" };

        private string _selectedBillFilter = "All Bills";
        public string SelectedBillFilter
        {
            get => _selectedBillFilter;
            set { if (SetProperty(ref _selectedBillFilter, value)) ApplyFilters(); }
        }

        private DateTime _fromDate = DateTime.Today;
        public DateTime FromDate
        {
            get => _fromDate;
            set 
            { 
                if (SetProperty(ref _fromDate, value))
                {
                    // Ensure FromDate is not after ToDate
                    if (_fromDate > ToDate)
                        SetProperty(ref _toDate, _fromDate, nameof(ToDate));
                    
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
                    // Ensure ToDate is not before FromDate
                    if (_toDate < FromDate)
                        SetProperty(ref _fromDate, _toDate, nameof(FromDate));

                    GenerateReport();
                }
            }
        }

        private string _selectedReportType = "Daily";
        public string SelectedReportType
        {
            get => _selectedReportType;
            set 
            { 
                if (SetProperty(ref _selectedReportType, value))
                    GenerateReport();
            }
        }

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
        
        private string _fromDateLabel = "Target Date";
        public string FromDateLabel { get => _fromDateLabel; set => SetProperty(ref _fromDateLabel, value); }

        private bool _isRevenueVisible = true;
        public bool IsRevenueVisible { get => _isRevenueVisible; set => SetProperty(ref _isRevenueVisible, value); }

        // --- Bill Detail Overlay ---
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

        public ICommand ExportReportCommand { get; }
        public ICommand ViewBillCommand { get; }
        public ICommand PrintBillCommand { get; }
        public ICommand CloseBillDetailCommand { get; }

        public ReportsViewModel(ReportService reportService, IStockService stockService, PrintService printService, AuthService authService, IReturnService returnService)
        {
            _reportService = reportService;
            _stockService = stockService;
            _printService = printService;
            _authService = authService;
            _returnService = returnService;

            ExportReportCommand = new RelayCommand(ExportReport);
            ViewBillCommand = new RelayCommand(obj => ViewBill(obj as Bill));
            PrintBillCommand = new RelayCommand(obj => PrintBill(obj as Bill));
            CloseBillDetailCommand = new RelayCommand(_ => CloseBillDetail());
            
            _stockService.StockChanged += GenerateReport;
            GenerateReport();
        }

        public override void Dispose()
        {
            if (_stockService != null)
                _stockService.StockChanged -= GenerateReport;
            base.Dispose();
        }

        public void GenerateReport()
        {
            try
            {
                DateTime start, end;

                var type = SelectedReportType?.Trim();

                // 1. Determine UI State
                if (string.Equals(type, "Custom Range", StringComparison.OrdinalIgnoreCase))
                {
                    IsFromDateVisible = true;
                    IsToDateVisible = true;
                    FromDateLabel = "From Date";
                    IsRevenueVisible = true;
                }
                else if (string.Equals(type, "Product-wise", StringComparison.OrdinalIgnoreCase))
                {
                    IsFromDateVisible = true;
                    IsToDateVisible = true;
                    FromDateLabel = "Start Date";
                    IsRevenueVisible = true;
                }
                else if (string.Equals(type, "Low Stock", StringComparison.OrdinalIgnoreCase))
                {
                    IsFromDateVisible = false;
                    IsToDateVisible = false;
                    IsRevenueVisible = false;
                }
                else
                {
                    // Daily, Weekly, Monthly use a single date picker to define the period
                    IsFromDateVisible = true;
                    IsToDateVisible = false;
                    
                    if (string.Equals(type, "Monthly", StringComparison.OrdinalIgnoreCase))
                        FromDateLabel = "Selected Month";
                    else if (string.Equals(type, "Weekly", StringComparison.OrdinalIgnoreCase))
                        FromDateLabel = "Selected Week";
                    else
                        FromDateLabel = "Report Date";

                    IsRevenueVisible = true;
                }

                // 2. Determine Date Range based on FromDate (the anchor)
                if (string.Equals(type, "Daily", StringComparison.OrdinalIgnoreCase))
                {
                    start = FromDate.Date;
                    end = start.AddDays(1);
                }
                else if (string.Equals(type, "Weekly", StringComparison.OrdinalIgnoreCase))
                {
                    int diff = (7 + (FromDate.DayOfWeek - DayOfWeek.Monday)) % 7;
                    start = FromDate.AddDays(-1 * diff).Date;
                    end = start.AddDays(7);
                }
                else if (string.Equals(type, "Monthly", StringComparison.OrdinalIgnoreCase))
                {
                    start = new DateTime(FromDate.Year, FromDate.Month, 1);
                    end = start.AddMonths(1);
                }
                else // Custom or Product-wise
                {
                    start = FromDate.Date;
                    end = ToDate.Date.AddDays(1);
                }

                // 3. Fetch and Populate Data
                if (string.Equals(type, "Product-wise", StringComparison.OrdinalIgnoreCase))
                {
                    ShowSalesGrid = false;
                    ShowProductGrid = true;
                    ShowLowStockGrid = false;
                    var data = _reportService.GetProductWiseReport(start, end);
                    Dispatch(() =>
                    {
                        ProductReport.Clear();
                        foreach (var r in data) ProductReport.Add(r);
                        TotalRevenue = data.Sum(r => r.TotalRevenue);
                        TotalSalesCount = data.Sum(r => r.QuantitySold);
                    });
                }
                else if (string.Equals(type, "Low Stock", StringComparison.OrdinalIgnoreCase))
                {
                    ShowSalesGrid = false;
                    ShowProductGrid = false;
                    ShowLowStockGrid = true;
                    var data = _stockService.GetLowStockItems();
                    Dispatch(() =>
                    {
                        LowStockReport.Clear();
                        foreach (var i in data) LowStockReport.Add(i);
                        TotalRevenue = 0; // Not applicable for low stock
                        TotalSalesCount = data.Count;
                    });
                }
                else
                {
                    ShowSalesGrid = true;
                    ShowProductGrid = false;
                    ShowLowStockGrid = false;
                    
                    // Fetch all bills for the period (Sales + Returns)
                    _currentRawBills = _reportService.GetByDateRange(start, end);

                    // Calculations should still reflect overall revenue for Sales
                    var salesData = _currentRawBills.Where(b => b.Type == "Sale").ToList();
                    
                    double returnsTotal = _reportService.GetReturnsTotalByDateRange(start, end);
                    double creditTotal  = _reportService.GetOutstandingCreditTotal();
                    
                    Dispatch(() =>
                    {
                        TotalRevenue    = salesData.Sum(s => s.GrandTotal);
                        TotalSalesCount = salesData.Count;
                        TotalReturns    = returnsTotal;
                        NetSales        = TotalRevenue - returnsTotal;
                        OutstandingCredit = creditTotal;

                        ApplyFilters();

                        // Weekly breakdown log (monthly report only)
                        if (string.Equals(type, "Monthly", StringComparison.OrdinalIgnoreCase))
                        {
                            var weeklyGroups = salesData.GroupBy(b =>
                            {
                                int d = (7 + (b.BillDateTime.DayOfWeek - DayOfWeek.Monday)) % 7;
                                return b.BillDateTime.AddDays(-1 * d).Date;
                            }).OrderBy(g => g.Key);
                            foreach (var week in weeklyGroups)
                                AppLogger.Info($"Week starting {week.Key:yyyy-MM-dd}: Rs.{week.Sum(b => b.GrandTotal):N2} ({week.Count()} bills)");
                        }
                    });
                }

                // 3. Log System Diagnostics
                AppLogger.Info(_reportService.GetDiagnostics());
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error generating report", ex);
            }
        }

        private void ApplyFilters()
        {
            if (_currentRawBills == null) return;

            var filtered = _currentRawBills.AsEnumerable();

            // 1. Filter by Bill Type Dropdown
            if (SelectedBillFilter == "Normal Bills")
                filtered = filtered.Where(b => b.Type == "Sale" && b.RemainingAmount <= 0);
            else if (SelectedBillFilter == "Credit Bills")
                filtered = filtered.Where(b => b.Type == "Sale" && b.RemainingAmount > 0);
            // "All Bills" implies no filtering

            // 2. Filter by Search Query (Invoice Number or Customer Name)
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var q = SearchQuery.Trim().ToLower();
                filtered = filtered.Where(b => b.InvoiceNumber.ToLower().Contains(q) || (b.Customer?.Name?.ToLower().Contains(q) ?? false));
            }

            var finalResults = filtered.ToList();

            Dispatch(() =>
            {
                SalesReport.Clear();
                foreach (var b in finalResults)
                    SalesReport.Add(b);
            });
        }
        private void ExportReport()
        {
            try
            {
                if (ShowSalesGrid && SalesReport.Count == 0) return;
                if (ShowProductGrid && ProductReport.Count == 0) return;
                if (ShowLowStockGrid && LowStockReport.Count == 0) return;

                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv",
                    FileName = $"Report_{SelectedReportType}_{DateTime.Now:yyyyMMdd_HHmm}.csv"
                };

                if (sfd.ShowDialog() == true)
                {
                    var csv = new System.Text.StringBuilder();

                    if (ShowSalesGrid)
                    {
                        csv.AppendLine("Invoice #,Cashier,Sub Total,Discount,Tax,Grand Total,Date/Time");
                        foreach (var b in SalesReport)
                        {
                            csv.AppendLine($"{b.InvoiceNumber},{b.User?.FullName ?? "Unknown"},{b.SubTotal},{b.DiscountAmount},{b.TaxAmount},{b.GrandTotal},{b.BillDateTime:yyyy-MM-dd HH:mm}");
                        }
                        
                        // Add TOTAL row
                        double totalSub = 0, totalDisc = 0, totalTax = 0, totalGrand = 0;
                        foreach (var b in SalesReport)
                        {
                            totalSub += b.SubTotal;
                            totalDisc += b.DiscountAmount;
                            totalTax += b.TaxAmount;
                            totalGrand += b.GrandTotal;
                        }
                        csv.AppendLine($"TOTAL,,{totalSub},{totalDisc},{totalTax},{totalGrand},");
                    }
                    else if (ShowProductGrid)
                    {
                        csv.AppendLine("Product Name,Quantity Sold,Total Revenue");
                        foreach (var p in ProductReport)
                        {
                            csv.AppendLine($"\"{p.ItemDescription}\",{p.QuantitySold},{p.TotalRevenue}");
                        }

                        // Add TOTAL row
                        int totalQty = 0;
                        double totalRev = 0;
                        foreach (var p in ProductReport)
                        {
                            totalQty += p.QuantitySold;
                            totalRev += p.TotalRevenue;
                        }
                        csv.AppendLine($"TOTAL,{totalQty},{totalRev}");
                    }
                    else if (ShowLowStockGrid)
                    {
                        csv.AppendLine("Barcode,Product Name,Category,Current Stock,Threshold");
                        foreach (var i in LowStockReport)
                        {
                            csv.AppendLine($"{i.Barcode},\"{i.Description}\",\"{i.ItemCategory}\",{i.StockQuantity},{i.MinStockThreshold}");
                        }
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
        private async void ViewBill(Bill? bill)
        {
            if (bill == null) return;
            
            if (bill.IsReturn && bill.ParentBillId.HasValue)
            {
                try 
                {
                    var (original, returns) = await _returnService.GetBillWithReturnHistory(bill.ParentBillId.Value);
                    bill.ParentBill = original;
                    bill.ReturnHistory = new ObservableCollection<Bill>(returns.Where(r => r.BillId != bill.BillId));
                    
                    double previousReturnsTotal = returns.Where(r => r.BillId < bill.BillId).Sum(r => Math.Abs(r.GrandTotal));
                    bill.RemainingDueAfterThisReturn = original.GrandTotal - previousReturnsTotal - Math.Abs(bill.GrandTotal);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Failed to fetch return metadata for view", ex);
                }
            }

            SelectedHistoryBill = bill;
            IsBillDetailOpen = true;
        }

        private async void PrintBill(Bill? bill)
        {
            if (bill == null) return;

            if (bill.IsReturn && bill.ParentBillId.HasValue && bill.ParentBill == null)
            {
                try 
                {
                    var (original, returns) = await _returnService.GetBillWithReturnHistory(bill.ParentBillId.Value);
                    bill.ParentBill = original;
                    bill.ReturnHistory = new ObservableCollection<Bill>(returns.Where(r => r.BillId != bill.BillId));

                    double previousReturnsTotal = returns.Where(r => r.BillId < bill.BillId).Sum(r => Math.Abs(r.GrandTotal));
                    bill.RemainingDueAfterThisReturn = original.GrandTotal - previousReturnsTotal - Math.Abs(bill.GrandTotal);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Failed to fetch return metadata for print", ex);
                }
            }
            
            bool isOnline = _printService.IsPrinterOnline();

            if (isOnline)
            {
                bool printSuccess = _printService.PrintReceipt(bill, _authService.CurrentUser?.FullName ?? "System Admin");
                if (!printSuccess)
                {
                    System.Windows.MessageBox.Show("Failed to communicate with the printer. Please check the connection.", "Print Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            else
            {
                System.Windows.MessageBox.Show("Printer is currently unavailable or offline.\nPlease ensure the printer is connected and turned on.", "Printer Offline", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void CloseBillDetail()
        {
            IsBillDetailOpen = false;
            SelectedHistoryBill = null;
        }
    }
}
