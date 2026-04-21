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
        // ── Helper model for the combined timeline ──
        public class BillHistoryEvent
        {
            public DateTime Date { get; set; }
            public string Type { get; set; } = "PAYMENT"; // SALE, PAYMENT, RETURN
            public string Description { get; set; } = string.Empty;
            public double Amount { get; set; }
            public string TypeColor => Type switch {
                "SALE" => "#3B82F6",
                "PAYMENT" => "#22C55E",
                "RETURN" => "#F59E0B",
                _ => "#94A3B8"
            };
        }
        private readonly CreditService _creditService;
        private readonly CustomerService _customerService;
        private readonly PrintService _printService;
        private readonly AuthService _authService;
        private readonly IStockService _stockService;
        private readonly IReturnService _returnService;
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
                    LoadBillTimeline();
                }
            }
        }
        public bool HasSelectedBill => SelectedBill != null;
        public double SelectedBillRemaining => SelectedBill?.RemainingAmount ?? 0;


        private bool _isBillDetailOpen;
        public bool IsBillDetailOpen
        {
            get => _isBillDetailOpen;
            set => SetProperty(ref _isBillDetailOpen, value);
        }

        public ObservableCollection<BillHistoryEvent> Timeline { get; } = new();

        private void LoadBillTimeline()
        {
            Timeline.Clear();
            if (SelectedBill == null) return;

            try
            {
                // 1. Deep load details (Items, Payments, Returns)
                _billRepo.LoadAuditLogs(SelectedBill);

                var events = new List<BillHistoryEvent>();

                // A. The original SALE
                events.Add(new BillHistoryEvent {
                    Date = SelectedBill.BillDateTime,
                    Type = "SALE",
                    Description = $"Invoice #{SelectedBill.InvoiceNumber} created",
                    Amount = SelectedBill.GrandTotal
                });

                // B. Payments (Initial or Installments)
                var history = _creditService.GetPaymentHistory(SelectedBill.BillId);
                double initialPayment = Math.Round(SelectedBill.InitialPayment, 2);

                if (initialPayment > 0)
                {
                    events.Add(new BillHistoryEvent {
                        Date = SelectedBill.BillDateTime,
                        Type = "PAYMENT",
                        Description = "initial payment",
                        Amount = initialPayment
                    });
                }

                foreach (var p in history)
                {
                    bool isRefund = string.Equals(p.TransactionType, "Refund", StringComparison.OrdinalIgnoreCase);
                    events.Add(new BillHistoryEvent {
                        Date = p.PaidAt,
                        Type = isRefund ? "RETURN" : "PAYMENT",
                        Description = string.IsNullOrEmpty(p.Note)
                            ? (isRefund ? "cash refunded" : "adjust credits")
                            : p.Note,
                        Amount = p.AmountPaid
                    });
                }

                // C. Returns
                foreach (var ret in SelectedBill.ReturnLogs)
                {
                    // For returns, we want to show the TOTAL VALUE of returned items, 
                    // not just the cash refund (which is often 0 for credit bills).
                    double returnTotalValue = ret.Items.Sum(i => i.TotalPrice);

                    events.Add(new BillHistoryEvent {
                        Date = ret.ReturnedAt,
                        Type = "RETURN",
                        Description = "item returned",
                        Amount = returnTotalValue
                    });
                }

                // Sort everything chronologically and add to UI
                foreach (var ev in events.OrderBy(x => x.Date))
                {
                    Timeline.Add(ev);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("CustomerLedgerViewModel.LoadBillTimeline failed", ex);
            }
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
        public ICommand CloseBillDetailCommand { get; }
        public ICommand CloseSidebarCommand { get; }

        /// <summary>Raised when the user wants to go back to Customer Management.</summary>
        public event Action? GoBackRequested;
        public ICommand GoBackCommand { get; }

        public CustomerLedgerViewModel(CreditService creditService, CustomerService customerService, PrintService printService, AuthService authService, IStockService stockService, IReturnService returnService)
        {
            _creditService  = creditService;
            _customerService = customerService;
            _printService = printService;
            _authService = authService;
            _stockService = stockService;
            _returnService = returnService;

            // Real-time refresh whenever stock/billing events occur
            _stockService.StockChanged += OnDataChanged;

            RefreshCommand          = new RelayCommand(_ => Refresh());
            OpenPaymentPanelCommand = new RelayCommand(obj => OpenPaymentPanel(obj as Bill));
            ClosePaymentPanelCommand= new RelayCommand(_ => ClosePaymentPanel());
            RecordPaymentCommand    = new RelayCommand(_ => RecordPayment());
            PayFullRemainingCommand = new RelayCommand(_ => PayFullRemaining());
            ViewBillCommand         = new RelayCommand(obj => ViewBill(obj as Bill));
            PrintBillCommand        = new RelayCommand(obj => PrintBill(obj as Bill));
            CloseBillDetailCommand  = new RelayCommand(_ => CloseBillDetail());
            CloseSidebarCommand     = new RelayCommand(_ => CloseSidebar());
            GoBackCommand           = new RelayCommand(_ => GoBackRequested?.Invoke());
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
                // - Credit Given: only the receivable portion after initial checkout payment.
                // - Pending: current outstanding receivable.
                // - Paid: settled part of that receivable.
                TotalCredit = Math.Round(LedgerEntries.Sum(e => Math.Max(0, e.NetTotal - e.InitialPayment)), 2);
                TotalPending = Math.Round(LedgerEntries.Sum(e => e.RemainingAmount), 2);
                TotalPaid = Math.Round(Math.Max(0, TotalCredit - TotalPending), 2);

                // Refresh the customer's pending credit too
                Customer.PendingCredit = TotalPending;
                OnPropertyChanged(nameof(TotalPending));
                OnPropertyChanged(nameof(TotalPaid));
                OnPropertyChanged(nameof(TotalCredit));
                OnPropertyChanged(nameof(Customer));
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

                // Capture details before the service call, as it triggers a refresh that nulls SelectedBill
                string invoiceNumber = SelectedBill.InvoiceNumber;
                int billId = SelectedBill.BillId;

                var updatedBill = _creditService.RecordPayment(SelectedBill.BillId, amount, PaymentNote);

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

                StatusMessage = $"✓ Payment of Rs. {amount:N2} recorded successfully for Bill #{invoiceNumber}.";
                
                // Real-time UI Sync:
                // 1. Refresh the grid and total summaries
                LoadLedger(); 
                
                // 2. Re-fetch current bill but KEEP it selected to keep sidebar open
                var refreshedBill = _creditService.GetBillById(billId);
                if (refreshedBill != null)
                {
                    refreshedBill.Customer = Customer;
                    SelectedBill = refreshedBill;
                    LoadBillTimeline(); // Refresh chronological timeline in sidebar
                }

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

        private void CloseBillDetail()
        {
            IsBillDetailOpen = false;
        }

        private void CloseSidebar()
        {
            SelectedBill = null;
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
