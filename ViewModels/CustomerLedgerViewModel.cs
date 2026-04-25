using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GroceryPOS.Helpers;
using GroceryPOS.Models;
using GroceryPOS.Services;
using GroceryPOS.Data.Repositories;

namespace GroceryPOS.ViewModels
{
    /// <summary>
    /// ViewModel for the Customer Ledger screen.
    /// Shows all bills for a customer with credit summary and allows recording payments.
    /// </summary>
    public class CustomerLedgerViewModel : BaseViewModel
    {
        public class BillAuditTimelineEntry
        {
            public int StepNo { get; set; }
            public DateTime Date { get; set; }
            public string Type { get; set; } = string.Empty;
            public string Method { get; set; } = string.Empty;
            public double Amount { get; set; }
            public string Note { get; set; } = string.Empty;
            public bool IsReturn { get; set; }
            public bool IsRefund { get; set; }
            public bool ShowRegularAmount => !IsReturn && !IsRefund;
            public int SortOrder { get; set; }
            public int SourceOrderId { get; set; }
            public ReturnAuditGroup? ReturnGroup { get; set; }
            public double BalanceImpact { get; set; }
            public double RemainingBalanceAfter { get; set; }
            public string ReturnHoverDetail { get; set; } = string.Empty;
            public string RemainingBalanceLabel => $"Rs. {RemainingBalanceAfter:N0}";
            public string DisplayAmount => IsReturn ? $"-Rs. {Amount:N0}" : $"Rs. {Amount:N0}";
        }

        private readonly CreditService _creditService;
        private readonly CustomerService _customerService;
        private readonly PrintService _printService;
        private readonly AuthService _authService;
        private readonly IStockService _stockService;
        private readonly IReturnService _returnService;
        private readonly AccountService _accountService;
        private readonly CustomerLedgerRepository _ledgerRepo = new();
        private readonly BillRepository _billRepo = new();

        // ── Selected customer ──
        private Customer? _customer;
        public Customer? Customer
        {
            get => _customer;
            private set => SetProperty(ref _customer, value);
        }

        // ── Ledger data ──
        public ObservableCollection<Bill> LedgerEntries { get; } = new();

        // ── Summary footer ──
        private double _totalCredit;
        public double TotalCredit
        {
            get => _totalCredit;
            private set => SetProperty(ref _totalCredit, value);
        }

        private double _totalPaid;
        public double TotalPaid
        {
            get => _totalPaid;
            private set => SetProperty(ref _totalPaid, value);
        }

        private double _totalPending;
        public double TotalPending
        {
            get => _totalPending;
            private set => SetProperty(ref _totalPending, value);
        }

        // ── Record Payment panel ──
        private bool _isPaymentPanelOpen;
        public bool IsPaymentPanelOpen
        {
            get => _isPaymentPanelOpen;
            set => SetProperty(ref _isPaymentPanelOpen, value);
        }

        private Bill? _selectedBill;
        public Bill? SelectedBill
        {
            get => _selectedBill;
            set
            {
                if (SetProperty(ref _selectedBill, value))
                {
                    OnPropertyChanged(nameof(HasSelectedBill));
                    OnPropertyChanged(nameof(SelectedBillRemaining));
                }
            }
        }
        public bool HasSelectedBill => SelectedBill != null;
        public double SelectedBillRemaining => SelectedBill?.RemainingAmount ?? 0;

        public ObservableCollection<BillAuditTimelineEntry> BillAuditTimeline { get; } = new();

        private ReturnAuditGroup? _selectedReturnDetail;
        public ReturnAuditGroup? SelectedReturnDetail
        {
            get => _selectedReturnDetail;
            set
            {
                if (SetProperty(ref _selectedReturnDetail, value))
                    OnPropertyChanged(nameof(IsReturnDetailOpen));
            }
        }
        public bool IsReturnDetailOpen => SelectedReturnDetail != null;

        private bool _isBillDetailOpen;
        public bool IsBillDetailOpen
        {
            get => _isBillDetailOpen;
            set => SetProperty(ref _isBillDetailOpen, value);
        }

        private string _paymentAmountText = string.Empty;
        public string PaymentAmountText
        {
            get => _paymentAmountText;
            set => SetProperty(ref _paymentAmountText, value);
        }

        private string _paymentNote = string.Empty;
        public string PaymentNote
        {
            get => _paymentNote;
            set => SetProperty(ref _paymentNote, value);
        }

        private string _paymentError = string.Empty;
        public string PaymentError
        {
            get => _paymentError;
            set => SetProperty(ref _paymentError, value);
        }

        // ── Payment Method Selection ──
        public List<string> PaymentMethods { get; } = new() { "Cash", "Online" };

        private string _selectedPaymentMethod = "Cash";
        public string SelectedPaymentMethod
        {
            get => _selectedPaymentMethod;
            set
            {
                if (SetProperty(ref _selectedPaymentMethod, value))
                {
                    OnPropertyChanged(nameof(IsCashPayment));
                    OnPropertyChanged(nameof(IsOnlinePayment));
                }
            }
        }

        public bool IsCashPayment => SelectedPaymentMethod == "Cash";
        public bool IsOnlinePayment => SelectedPaymentMethod == "Online";

        // ── Online Payment Accounts ──
        private ObservableCollection<Account> _activeAccounts = new();
        public ObservableCollection<Account> ActiveAccounts
        {
            get => _activeAccounts;
            set => SetProperty(ref _activeAccounts, value);
        }

        private Account? _selectedAccount;
        public Account? SelectedAccount
        {
            get => _selectedAccount;
            set => SetProperty(ref _selectedAccount, value);
        }

        public List<string> OnlinePaymentMethods { get; } = new() { "Easypaisa", "JazzCash", "Bank Transfer" };

        private string? _selectedOnlineMethod;
        public string? SelectedOnlineMethod
        {
            get => _selectedOnlineMethod;
            set => SetProperty(ref _selectedOnlineMethod, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // ── Commands ──
        public ICommand RefreshCommand { get; }
        public ICommand OpenPaymentPanelCommand { get; }
        public ICommand ClosePaymentPanelCommand { get; }
        public ICommand RecordPaymentCommand { get; }
        public ICommand PayFullRemainingCommand { get; }
        public ICommand ViewBillCommand { get; }
        public ICommand PrintBillCommand { get; }
        public ICommand PrintLedgerCommand { get; }
        public ICommand OpenReturnDetailCommand { get; }
        public ICommand CloseReturnDetailCommand { get; }
        public ICommand CloseBillDetailCommand { get; }
        public ICommand CloseSidebarCommand { get; }

        /// <summary>Raised when the user wants to go back to Customer Management.</summary>
        public event Action? GoBackRequested;
        public ICommand GoBackCommand { get; }

        public CustomerLedgerViewModel(CreditService creditService, CustomerService customerService, PrintService printService, AuthService authService, IStockService stockService, IReturnService returnService, AccountService accountService)
        {
            _creditService  = creditService;
            _customerService = customerService;
            _printService = printService;
            _authService = authService;
            _stockService = stockService;
            _returnService = returnService;
            _accountService = accountService;

            // Real-time refresh whenever stock/billing events occur
            _stockService.StockChanged += OnDataChanged;

            RefreshCommand          = new RelayCommand(_ => Refresh());
            OpenPaymentPanelCommand = new RelayCommand(obj => OpenPaymentPanel(obj as Bill));
            ClosePaymentPanelCommand= new RelayCommand(_ => ClosePaymentPanel());
            RecordPaymentCommand    = new RelayCommand(_ => RecordPayment());
            PayFullRemainingCommand = new RelayCommand(_ => PayFullRemaining());
            ViewBillCommand         = new RelayCommand(obj => ViewBill(obj as Bill));
            PrintBillCommand        = new RelayCommand(obj => PrintBill(obj as Bill));
            PrintLedgerCommand      = new RelayCommand(_ => PrintLedger());
            OpenReturnDetailCommand = new RelayCommand(obj => OpenReturnDetail(obj as BillAuditTimelineEntry));
            CloseReturnDetailCommand= new RelayCommand(_ => SelectedReturnDetail = null);
            CloseBillDetailCommand  = new RelayCommand(_ => CloseBillDetail());
            CloseSidebarCommand     = new RelayCommand(_ => CloseSidebar());
            GoBackCommand           = new RelayCommand(_ => GoBackRequested?.Invoke());

            LoadActiveAccounts();
        }

        private void LoadActiveAccounts()
        {
            try
            {
                var accounts = _accountService.GetActiveAccounts();
                ActiveAccounts = new ObservableCollection<Account>(accounts);
                if (ActiveAccounts.Any())
                    SelectedAccount = ActiveAccounts.First();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load active accounts", ex);
            }
        }

        // ────────────────────────────────────────────
        //  LOAD
        // ────────────────────────────────────────────

        public void Load(int customerId)
        {
            try
            {
                // Reset UI state from any previous customer
                SelectedBill = null;
                IsPaymentPanelOpen = false;
                IsBillDetailOpen = false;
                StatusMessage = string.Empty;
                Customer = _customerService.GetCustomerById(customerId);
                if (Customer == null)
                {
                    StatusMessage = "Customer not found.";
                    ShowPopupError("Customer not found.");
                    return;
                }
                LoadLedger();
            }
            catch (Exception ex)
            {
                AppLogger.Error("CustomerLedgerViewModel.Load failed", ex);
                StatusMessage = "⚠ Failed to load ledger.";
                ShowPopupError("Failed to load ledger.");
            }
        }

        private void LoadLedger()
        {
            if (Customer == null) return;

            try
            {
                var bills = _billRepo.GetBillsByCustomerId(Customer.CustomerId);
                LedgerEntries.Clear();
                foreach (var bill in bills)
                    LedgerEntries.Add(bill);

                // Summary calculations for CREDIT ledger (not gross sales):
                TotalCredit = Math.Round(LedgerEntries.Sum(e => Math.Max(0, e.NetTotal - e.InitialPayment)), 2);
                TotalPending = Math.Round(LedgerEntries.Sum(e => e.RemainingAmount), 2);
                TotalPaid = Math.Round(Math.Max(0, TotalCredit - TotalPending), 2);

                // Refresh the customer's pending credit too
                Customer.PendingCredit = TotalPending;
                OnPropertyChanged(nameof(TotalPending));
                OnPropertyChanged(nameof(TotalPaid));
                OnPropertyChanged(nameof(TotalCredit));
                OnPropertyChanged(nameof(Customer));

                var snapshot = _ledgerRepo.GetIntegritySnapshot(Customer.CustomerId);
                if (Math.Abs(snapshot.Drift) > 0.01)
                {
                    StatusMessage = $"⚠ Ledger drift detected: Rs. {snapshot.Drift:N2}. Rebuilding running balances...";
                    _ledgerRepo.RebuildRunningBalances(Customer.CustomerId);
                    var refreshedBills = _billRepo.GetBillsByCustomerId(Customer.CustomerId);
                    LedgerEntries.Clear();
                    foreach (var bill in refreshedBills)
                        LedgerEntries.Add(bill);

                    TotalCredit = Math.Round(LedgerEntries.Sum(e => Math.Max(0, e.NetTotal - e.InitialPayment)), 2);
                    TotalPending = Math.Round(LedgerEntries.Sum(e => e.RemainingAmount), 2);
                    TotalPaid = Math.Round(Math.Max(0, TotalCredit - TotalPending), 2);
                    Customer.PendingCredit = TotalPending;

                    OnPropertyChanged(nameof(TotalPending));
                    OnPropertyChanged(nameof(TotalPaid));
                    OnPropertyChanged(nameof(TotalCredit));
                    OnPropertyChanged(nameof(Customer));
                    StatusMessage = "Ledger audit recalculated successfully.";
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("CustomerLedgerViewModel.LoadLedger failed", ex);
                StatusMessage = "⚠ Failed to refresh ledger.";
                ShowPopupError("Failed to refresh ledger.");
            }
        }

        private void Refresh()
        {
            ClosePaymentPanel();
            LoadLedger();
            StatusMessage = string.Empty;
        }

        // ────────────────────────────────────────────
        //  PAYMENT RECORDING
        // ────────────────────────────────────────────

        private void OpenPaymentPanel(Bill? bill)
        {
            if (bill == null || !bill.HasPendingCredit) return;
            // Re-fetch from DB to get latest PaidAmount/RemainingAmount
            var fresh = _creditService.GetBillById(bill.BillId);
            if (fresh != null)
            {
                fresh.Customer = Customer;
                bill = fresh;
            }
            SelectedBill       = bill;
            PaymentAmountText  = string.Empty;
            PaymentNote        = string.Empty;
            PaymentError       = string.Empty;
            IsPaymentPanelOpen = true;
        }

        private void ClosePaymentPanel()
        {
            IsPaymentPanelOpen = false;
            PaymentAmountText  = string.Empty;
            PaymentError       = string.Empty;
            PaymentNote        = string.Empty;
            SelectedPaymentMethod = "Cash";
            SelectedOnlineMethod = null;
            SelectedAccount = ActiveAccounts.FirstOrDefault();
        }

        private void PayFullRemaining()
        {
            if (SelectedBill != null)
                PaymentAmountText = SelectedBill.RemainingAmount.ToString("F2");
        }

        private void RecordPayment()
        {
            try
            {
                PaymentError = string.Empty;

                if (SelectedBill == null)
                {
                    PaymentError = "No bill selected.";
                    return;
                }

                if (!double.TryParse(PaymentAmountText, out double amount) || amount <= 0)
                {
                    PaymentError = "Please enter a valid amount greater than zero.";
                    return;
                }

                if (IsOnlinePayment && SelectedAccount == null)
                {
                    PaymentError = "Please select a payment account for online payment.";
                    return;
                }

                // Capture details before the service call, as it triggers a refresh that nulls SelectedBill
                string invoiceNumber = SelectedBill.InvoiceNumber;
                int billId = SelectedBill.BillId;

                var updatedBill = _creditService.RecordPayment(SelectedBill.BillId, amount, PaymentNote, SelectedPaymentMethod);

                // Print payment receipt
                try
                {
                    updatedBill.Customer = Customer;
                    _printService.PrintPaymentReceipt(updatedBill, amount, _authService.CurrentUser?.FullName ?? "Cashier");
                }
                catch (Exception pex)
                {
                    AppLogger.Error("Payment receipt print failed (Ledger)", pex);
                }

                string methodDisplay = IsOnlinePayment ? $"{SelectedPaymentMethod} ({SelectedAccount?.DisplayName})" : SelectedPaymentMethod;
                StatusMessage = $"✓ Payment of Rs. {amount:N2} ({methodDisplay}) recorded successfully for Bill #{invoiceNumber}.";
                
                // Real-time UI Sync:
                // 1. Refresh the grid and total summaries
                LoadLedger(); 
                
                // 2. Re-fetch current bill but KEEP it selected to keep sidebar open
                var refreshedBill = _creditService.GetBillById(billId);
                if (refreshedBill != null)
                {
                    refreshedBill.Customer = Customer;
                    SelectedBill = refreshedBill;
                }

                // Keep user informed with a popup confirmation
                MessageBox.Show(StatusMessage, "Payment Recorded", MessageBoxButton.OK, MessageBoxImage.Information);
                OnPropertyChanged(nameof(StatusMessage));

                ClosePaymentPanel();
            }
            catch (Exception ex)
            {
                PaymentError = ex.Message;
                AppLogger.Error("CustomerLedgerViewModel.RecordPayment failed", ex);
            }
        }
        private void ViewBill(Bill? bill)
        {
            if (bill == null) return;

            try 
            {
                // Deep load Audit Logs (Items, Payments, Returns)
                _billRepo.LoadAuditLogs(bill);
                SelectedBill = bill;
                BuildBillAuditTimeline(bill);
                SelectedReturnDetail = null;
                IsBillDetailOpen = true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load bill audit details", ex);
                ShowPopupError("Failed to load detailed bill history.");
            }
        }

        private void PrintBill(Bill? bill)
        {
            if (bill == null) return;

            bool isOnline = _printService.IsPrinterOnline();
            if (isOnline)
            {
                bool printSuccess = _printService.PrintReceipt(bill, _authService.CurrentUser?.FullName ?? "System Admin");
                if (!printSuccess)
                {
                    System.Windows.MessageBox.Show("Failed to communicate with the printer.", "Print Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            else
            {
                System.Windows.MessageBox.Show("Printer is offline.", "Printer Offline", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void PrintLedger()
        {
            if (Customer == null)
            {
                ShowPopupError("No ledger entries found to print.");
                return;
            }

            var timeline = _ledgerRepo.GetLedgerTimeline(Customer.CustomerId);
            if (timeline.Count == 0)
            {
                ShowPopupError("No ledger entries found to print.");
                return;
            }

            var ok = _printService.PrintCustomerLedgerStatement(Customer, timeline, null, null);
            if (!ok)
            {
                ShowPopupError("Failed to print customer ledger statement.");
            }
        }

        private void CloseBillDetail()
        {
            IsBillDetailOpen = false;
            SelectedReturnDetail = null;
            BillAuditTimeline.Clear();
        }

        private void CloseSidebar()
        {
            SelectedBill = null;
        }

        private void BuildBillAuditTimeline(Bill bill)
        {
            BillAuditTimeline.Clear();
            // Start from original bill total, then apply each timeline event in sequence.
            // This keeps row-1 (sale creation) balance accurate before returns.
            double runningPending = Math.Max(0, bill.GrandTotal);

            foreach (var payment in bill.PaymentLogs)
            {
                bool isRefund = string.Equals(payment.TransactionType, "Refund", StringComparison.OrdinalIgnoreCase);
                if (isRefund)
                {
                    // Refund is now represented inside the RETURN row note to avoid duplicate rows.
                    continue;
                }
                BillAuditTimeline.Add(new BillAuditTimelineEntry
                {
                    Date = payment.PaidAt,
                    Type = string.Equals(payment.TransactionType, "Sale", StringComparison.OrdinalIgnoreCase) ? "Sale Created" : "Payment",
                    Method = string.IsNullOrWhiteSpace(payment.PaymentMethod) ? "Cash" : payment.PaymentMethod,
                    Amount = Math.Abs(payment.AmountPaid),
                    Note = BuildPaymentNote(payment, bill),
                    IsReturn = false, // keep refund as cash detail row
                    IsRefund = isRefund,
                    SortOrder = isRefund ? 2 : 3,
                    SourceOrderId = payment.PaymentId,
                    ReturnGroup = null,
                    BalanceImpact = Math.Abs(payment.AmountPaid)
                });
            }

            foreach (var ret in bill.ReturnLogs)
            {
                var returnAmount = ret.Items.Sum(i => i.TotalPrice);
                BillAuditTimeline.Add(new BillAuditTimelineEntry
                {
                    Date = ret.ReturnedAt,
                    Type = "Return",
                    Method = "Cash",
                    Amount = Math.Abs(returnAmount),
                    Note = string.Empty,
                    IsReturn = true,
                    IsRefund = false,
                    SortOrder = 1, // for same timestamp: show return before refund cash detail
                    SourceOrderId = ret.ReturnId,
                    ReturnGroup = ret,
                    BalanceImpact = 0
                });
            }

            var ordered = BillAuditTimeline
                .OrderBy(x => x.Date)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.SourceOrderId)
                .ToList();

            // Build "remaining after this row" in strict chronological order.
            int stepNo = 1;
            foreach (var row in ordered)
            {
                if (row.IsReturn)
                {
                    // Business rule: return always clears pending credit first, then cash back.
                    var creditAdjusted = Math.Min(runningPending, row.Amount);
                    var cashReturned = Math.Max(0, row.Amount - creditAdjusted);
                    row.BalanceImpact = creditAdjusted;
                    runningPending = Math.Max(0, runningPending - creditAdjusted);

                    row.Note = creditAdjusted > 0
                        ? $"returned Rs. -{row.Amount:N0}{Environment.NewLine}credit adjusted Rs. -{creditAdjusted:N0}" +
                          (cashReturned > 0 ? $"{Environment.NewLine}cash returned Rs. -{cashReturned:N0}" : "")
                        : $"return amount Rs. -{row.Amount:N0}";

                    row.ReturnHoverDetail = BuildReturnHoverDetail(row, creditAdjusted, cashReturned);
                }
                else
                {
                    var paymentApplied = Math.Min(runningPending, Math.Max(0, row.BalanceImpact));
                    runningPending = Math.Max(0, runningPending - paymentApplied);
                }

                row.StepNo = stepNo++;
                row.RemainingBalanceAfter = runningPending;
            }

            BillAuditTimeline.Clear();
            foreach (var row in ordered)
                BillAuditTimeline.Add(row);
        }

        private static string BuildPaymentNote(CreditPayment payment, Bill bill)
        {
            bool isSale = string.Equals(payment.TransactionType, "Sale", StringComparison.OrdinalIgnoreCase);
            if (isSale)
            {
                var totalBill = Math.Max(0, bill.GrandTotal);
                var paidAtSale = Math.Min(totalBill, Math.Max(0, Math.Abs(payment.AmountPaid)));
                var openingCredit = Math.Max(0, totalBill - paidAtSale);
                var saleNote = $"total bill Rs. {totalBill:N0}{Environment.NewLine}paid at sale Rs. {paidAtSale:N0}{Environment.NewLine}opening credit Rs. {openingCredit:N0}";
                if (!string.IsNullOrWhiteSpace(payment.DisplayNote))
                    saleNote = $"{saleNote}{Environment.NewLine}note: {payment.DisplayNote}";
                return saleNote;
            }

            var note = $"payment received Rs. {Math.Abs(payment.AmountPaid):N0}{Environment.NewLine}method {(string.IsNullOrWhiteSpace(payment.PaymentMethod) ? "Cash" : payment.PaymentMethod)}";
            if (!string.IsNullOrWhiteSpace(payment.DisplayNote))
                note = $"{note}{Environment.NewLine}note: {payment.DisplayNote}";
            return note;
        }

        private static string BuildReturnHoverDetail(BillAuditTimelineEntry row, double creditAdjusted, double cashReturned)
        {
            var lines = new List<string>
            {
                $"Return Time: {row.Date:dd/MM/yyyy HH:mm}",
                $"Returned Amount: Rs. -{row.Amount:N0}",
                $"Credit Adjusted: Rs. -{creditAdjusted:N0}"
            };

            if (cashReturned > 0)
                lines.Add($"Cash Returned: Rs. -{cashReturned:N0}");

            var items = row.ReturnGroup?.Items?.Select(i => $"{i.ItemDescription} x{Math.Abs(i.Quantity):N0}").ToList();
            if (items != null && items.Count > 0)
                lines.Add($"Items: {string.Join(", ", items)}");

            return string.Join(Environment.NewLine, lines);
        }

        private void OpenReturnDetail(BillAuditTimelineEntry? row)
        {
            if (row?.IsReturn != true || row.ReturnGroup == null) return;
            SelectedReturnDetail = row.ReturnGroup;
        }

        private void OnDataChanged()
        {
            if (Customer != null)
                Dispatch(() => LoadLedger());
        }

        public override void Dispose()
        {
            if (_stockService != null)
                _stockService.StockChanged -= OnDataChanged;
            base.Dispose();
        }
    }
}
