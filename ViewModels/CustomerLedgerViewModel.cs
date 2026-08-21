using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Models;
using FruitVegetableMarketPOS.Services;
using FruitVegetableMarketPOS.Data.Repositories;

namespace FruitVegetableMarketPOS.ViewModels
{
    /// <summary>
    /// ViewModel for the Customer Ledger screen.
    /// Shows all bills for a customer with credit summary and allows recording payments.
    /// </summary>
    [SupportedOSPlatform("windows")]
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

        /// <summary>
        /// One statement line: a sale/opening bill or a later recovery.
        /// Previous / total / pending are reconstructed from Fruit POS bills + bill_payment.
        /// </summary>
        public class LedgerRow
        {
            public DateTime CreatedAt { get; set; }
            public string InvoiceDisplay { get; set; } = string.Empty;
            public string SubtotalDisplay { get; set; } = "—";
            public double PreviousCredit { get; set; }
            public double TotalBanam { get; set; }
            public double ReceivedAmount { get; set; }
            public double PendingCredit { get; set; }
            public bool HasPendingCredit => PendingCredit > 0.01;
            public bool IsPayment { get; set; }
            public bool IsBill => !IsPayment;
            public bool ShowPayDue => IsBill && Bill != null && Bill.HasPendingCredit;
            public Bill? Bill { get; set; }
            public CreditPayment? Payment { get; set; }
        }

        private readonly CreditService _creditService;
        private readonly CustomerService _customerService;
        private readonly PrintService _printService;
        private readonly AuthService _authService;
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
        public ObservableCollection<LedgerRow> LedgerEntries { get; } = new();

        private bool _hasOpeningBalance;
        public bool HasOpeningBalance
        {
            get => _hasOpeningBalance;
            private set
            {
                if (SetProperty(ref _hasOpeningBalance, value))
                    OnPropertyChanged(nameof(CanAddPreviousDues));
            }
        }

        /// <summary>Hide + Previous Dues once an opening-balance bill already exists.</summary>
        public bool CanAddPreviousDues => !HasOpeningBalance;

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

        /// <summary>True when customer has any unpaid dues.</summary>
        public bool HasPendingDues => TotalPending > 0.01;

        // ── Pay Dues (FIFO multi-bill) panel ──
        private bool _isPayDuesPanelOpen;
        public bool IsPayDuesPanelOpen
        {
            get => _isPayDuesPanelOpen;
            set
            {
                if (SetProperty(ref _isPayDuesPanelOpen, value))
                    RecalcDuesPreview();
            }
        }

        private string _duesCashText = string.Empty;
        public string DuesCashText
        {
            get => _duesCashText;
            set
            {
                if (SetProperty(ref _duesCashText, value))
                    RecalcDuesPreview();
            }
        }

        private string _duesNote = string.Empty;
        public string DuesNote
        {
            get => _duesNote;
            set => SetProperty(ref _duesNote, value);
        }

        private string _duesError = string.Empty;
        public string DuesError
        {
            get => _duesError;
            set => SetProperty(ref _duesError, value);
        }

        private double _duesPreviewApplied;
        public double DuesPreviewApplied
        {
            get => _duesPreviewApplied;
            private set => SetProperty(ref _duesPreviewApplied, value);
        }

        private double _duesPreviewChange;
        public double DuesPreviewChange
        {
            get => _duesPreviewChange;
            private set => SetProperty(ref _duesPreviewChange, value);
        }

        private double _duesPreviewRemaining;
        public double DuesPreviewRemaining
        {
            get => _duesPreviewRemaining;
            private set => SetProperty(ref _duesPreviewRemaining, value);
        }

        public bool DuesPreviewHasChange => DuesPreviewChange > 0.01;

        // ── Record Payment panel ──
        private bool _isPaymentPanelOpen;
        public bool IsPaymentPanelOpen
        {
            get => _isPaymentPanelOpen;
            set
            {
                if (SetProperty(ref _isPaymentPanelOpen, value))
                    RecalcPaymentPreview();
            }
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
                    RecalcPaymentPreview();
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
            set
            {
                if (SetProperty(ref _paymentAmountText, value))
                    RecalcPaymentPreview();
            }
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

        private double _payPreviewApplied;
        public double PayPreviewApplied
        {
            get => _payPreviewApplied;
            private set => SetProperty(ref _payPreviewApplied, value);
        }

        private double _payPreviewChange;
        public double PayPreviewChange
        {
            get => _payPreviewChange;
            private set => SetProperty(ref _payPreviewChange, value);
        }

        private double _payPreviewRemaining;
        public double PayPreviewRemaining
        {
            get => _payPreviewRemaining;
            private set => SetProperty(ref _payPreviewRemaining, value);
        }

        public bool PayPreviewHasChange => PayPreviewChange > 0.01;

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
                    if (IsPayDuesPanelOpen)
                        RecalcDuesPreview();
                    if (IsPaymentPanelOpen)
                        RecalcPaymentPreview();
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

        // ── Opening Balance / Previous Dues panel ──
        private bool _isOpeningBalancePanelOpen;
        public bool IsOpeningBalancePanelOpen
        {
            get => _isOpeningBalancePanelOpen;
            set => SetProperty(ref _isOpeningBalancePanelOpen, value);
        }

        private string _openingBalanceAmountText = string.Empty;
        public string OpeningBalanceAmountText
        {
            get => _openingBalanceAmountText;
            set => SetProperty(ref _openingBalanceAmountText, value);
        }

        private string _openingBalanceNote = string.Empty;
        public string OpeningBalanceNote
        {
            get => _openingBalanceNote;
            set => SetProperty(ref _openingBalanceNote, value);
        }

        private string _openingBalanceError = string.Empty;
        public string OpeningBalanceError
        {
            get => _openingBalanceError;
            set => SetProperty(ref _openingBalanceError, value);
        }

        // ── Commands ──
        public ICommand RefreshCommand { get; }
        public ICommand OpenPaymentPanelCommand { get; }
        public ICommand ClosePaymentPanelCommand { get; }
        public ICommand RecordPaymentCommand { get; }
        public ICommand PayFullRemainingCommand { get; }
        public ICommand OpenPayDuesPanelCommand { get; }
        public ICommand ClosePayDuesPanelCommand { get; }
        public ICommand RecordDuesPaymentCommand { get; }
        public ICommand FillDuesFullPendingCommand { get; }
        public ICommand OpenOpeningBalancePanelCommand { get; }
        public ICommand CloseOpeningBalancePanelCommand { get; }
        public ICommand SaveOpeningBalanceCommand { get; }
        public ICommand ViewBillCommand { get; }
        public ICommand PrintBillCommand { get; }
        public ICommand PrintLedgerCommand { get; }
        public ICommand SaveLedgerPdfCommand { get; }
        public ICommand OpenReturnDetailCommand { get; }
        public ICommand CloseReturnDetailCommand { get; }
        public ICommand CloseBillDetailCommand { get; }
        public ICommand CloseSidebarCommand { get; }

        /// <summary>Raised when the user wants to go back to Customer Management.</summary>
        public event Action? GoBackRequested;
        public ICommand GoBackCommand { get; }

        public CustomerLedgerViewModel(CreditService creditService, CustomerService customerService, PrintService printService, AuthService authService, IReturnService returnService, AccountService accountService)
        {
            _creditService  = creditService;
            _customerService = customerService;
            _printService = printService;
            _authService = authService;
            _returnService = returnService;
            _accountService = accountService;

            RefreshCommand          = new RelayCommand(_ => Refresh());
            OpenPaymentPanelCommand = new RelayCommand(obj => OpenPaymentPanel(AsBill(obj)));
            ClosePaymentPanelCommand= new RelayCommand(_ => ClosePaymentPanel());
            RecordPaymentCommand    = new RelayCommand(_ => RecordPayment());
            PayFullRemainingCommand = new RelayCommand(_ => PayFullRemaining());
            OpenPayDuesPanelCommand = new RelayCommand(_ => OpenPayDuesPanel(), _ => HasPendingDues);
            ClosePayDuesPanelCommand = new RelayCommand(_ => ClosePayDuesPanel());
            RecordDuesPaymentCommand = new RelayCommand(_ => RecordDuesPayment());
            FillDuesFullPendingCommand = new RelayCommand(_ => FillDuesFullPending());
            OpenOpeningBalancePanelCommand = new RelayCommand(_ => OpenOpeningBalancePanel());
            CloseOpeningBalancePanelCommand = new RelayCommand(_ => CloseOpeningBalancePanel());
            SaveOpeningBalanceCommand = new RelayCommand(_ => SaveOpeningBalance());
            ViewBillCommand         = new RelayCommand(obj => ViewLedgerRow(obj));
            PrintBillCommand        = new RelayCommand(obj => PrintLedgerRow(obj));
            PrintLedgerCommand      = new RelayCommand(_ => PrintLedger());
            SaveLedgerPdfCommand    = new RelayCommand(_ => SaveLedgerPdf());
            OpenReturnDetailCommand = new RelayCommand(obj => OpenReturnDetail(obj as BillAuditTimelineEntry));
            CloseReturnDetailCommand= new RelayCommand(_ => SelectedReturnDetail = null);
            CloseBillDetailCommand  = new RelayCommand(_ => CloseBillDetail());
            CloseSidebarCommand     = new RelayCommand(_ => CloseSidebar());
            GoBackCommand           = new RelayCommand(_ => GoBackRequested?.Invoke());

            LoadActiveAccounts();
            AppEvents.DataChanged += OnAppDataChanged;
        }

        private void OnAppDataChanged()
        {
            AppEvents.InvokeOnUi(() =>
            {
                if (Customer == null) return;
                // Don't yank the payment panel out from under the cashier mid-entry
                if (IsPaymentPanelOpen || IsPayDuesPanelOpen || IsOpeningBalancePanelOpen) return;
                Refresh();
            });
        }

        /// <summary>Reload ledger + pending totals for the open customer.</summary>
        public void OnActivated()
        {
            if (Customer != null)
                Refresh();
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
                IsPayDuesPanelOpen = false;
                IsOpeningBalancePanelOpen = false;
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
                var snapshot = _ledgerRepo.GetIntegritySnapshot(Customer.CustomerId);
                if (Math.Abs(snapshot.Drift) > 0.01)
                {
                    StatusMessage = $"⚠ Ledger drift detected: Rs. {snapshot.Drift:N2}. Rebuilding running balances...";
                    _ledgerRepo.RebuildRunningBalances(Customer.CustomerId);
                }

                var bills = (_billRepo.GetBillsByCustomerId(Customer.CustomerId) ?? new List<Bill>())
                    .Where(b => !string.Equals(b.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                    .Where(b => !b.IsReturn)
                    .ToList();
                var recoveries = _creditService.GetRecoveriesForCustomer(Customer.CustomerId) ?? new List<CreditPayment>();

                HasOpeningBalance = _billRepo.CustomerHasOpeningBalance(Customer.CustomerId);

                TotalCredit = Math.Round(bills.Sum(b => b.NetTotal), 2);
                TotalPaid = Math.Round(bills.Sum(b => b.AppliedReceived), 2);
                TotalPending = Math.Max(0, Math.Round(bills.Sum(b => b.RemainingAmount), 2));

                var events = new List<(DateTime At, int Seq, string Kind, object Data)>();
                foreach (var bill in bills)
                    events.Add((bill.CreatedAt, bill.BillId, "Bill", bill));
                foreach (var pay in recoveries)
                    events.Add((pay.PaidAt, pay.PaymentId, "Payment", pay));

                events.Sort((a, b) =>
                {
                    int cmp = a.At.CompareTo(b.At);
                    if (cmp != 0) return cmp;
                    if (a.Kind != b.Kind)
                        return string.Equals(a.Kind, "Bill", StringComparison.Ordinal) ? -1 : 1;
                    return a.Seq.CompareTo(b.Seq);
                });

                double running = 0;
                var rows = new List<LedgerRow>();
                foreach (var ev in events)
                {
                    if (ev.Kind == "Bill" && ev.Data is Bill bill)
                    {
                        bill.Customer = Customer;
                        double taza = Math.Round(Math.Max(0, bill.NetTotal), 2);
                        double received = Math.Round(Math.Min(Math.Max(0, bill.InitialPayment), taza), 2);
                        double prev = running;
                        double totalBanam = Math.Round(prev + taza, 2);
                        double pending = Math.Round(Math.Max(0, totalBanam - received), 2);
                        running = pending;
                        rows.Add(new LedgerRow
                        {
                            CreatedAt = bill.CreatedAt,
                            InvoiceDisplay = bill.InvoiceDisplay,
                            SubtotalDisplay = bill.IsOpeningBalance ? "—" : $"Rs. {bill.SubTotal:N0}",
                            PreviousCredit = prev,
                            TotalBanam = totalBanam,
                            ReceivedAmount = received,
                            PendingCredit = pending,
                            IsPayment = false,
                            Bill = bill
                        });
                    }
                    else if (ev.Data is CreditPayment pay)
                    {
                        double received = Math.Round(Math.Max(0, pay.AmountPaid), 2);
                        double prev = running;
                        double pending = Math.Max(0, Math.Round(prev - received, 2));
                        running = pending;
                        rows.Add(new LedgerRow
                        {
                            CreatedAt = pay.PaidAt,
                            InvoiceDisplay = $"P-{pay.PaymentId:D5}",
                            SubtotalDisplay = "—",
                            PreviousCredit = prev,
                            TotalBanam = prev,
                            ReceivedAmount = received,
                            PendingCredit = pending,
                            IsPayment = true,
                            Payment = pay
                        });
                    }
                }

                LedgerEntries.Clear();
                foreach (var row in rows
                    .OrderByDescending(r => r.CreatedAt)
                    .ThenByDescending(r => r.InvoiceDisplay, StringComparer.OrdinalIgnoreCase))
                {
                    LedgerEntries.Add(row);
                }

                Customer.PendingCredit = TotalPending;
                OnPropertyChanged(nameof(TotalPending));
                OnPropertyChanged(nameof(TotalPaid));
                OnPropertyChanged(nameof(TotalCredit));
                OnPropertyChanged(nameof(HasPendingDues));
                OnPropertyChanged(nameof(Customer));
                (OpenPayDuesPanelCommand as RelayCommand)?.RaiseCanExecuteChanged();

                if (Math.Abs(snapshot.Drift) > 0.01)
                    StatusMessage = "Ledger audit recalculated successfully.";
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
            ClosePayDuesPanel();
            CloseOpeningBalancePanel();
            LoadLedger();
            StatusMessage = string.Empty;
        }

        // ────────────────────────────────────────────
        //  OPENING BALANCE / PREVIOUS DUES
        // ────────────────────────────────────────────

        private void OpenOpeningBalancePanel()
        {
            if (Customer == null) return;
            ClosePaymentPanel();
            ClosePayDuesPanel();
            OpeningBalanceAmountText = string.Empty;
            OpeningBalanceNote = string.Empty;
            OpeningBalanceError = string.Empty;
            IsOpeningBalancePanelOpen = true;
        }

        private void CloseOpeningBalancePanel()
        {
            IsOpeningBalancePanelOpen = false;
            OpeningBalanceError = string.Empty;
        }

        private void SaveOpeningBalance()
        {
            if (Customer == null) return;

            try
            {
                OpeningBalanceError = string.Empty;

                if (!double.TryParse(OpeningBalanceAmountText?.Trim(), out var amount) || amount <= 0)
                {
                    OpeningBalanceError = "Enter a valid amount greater than zero.";
                    return;
                }

                var bill = _creditService.CreateOpeningBalance(
                    Customer.CustomerId,
                    amount,
                    string.IsNullOrWhiteSpace(OpeningBalanceNote) ? null : OpeningBalanceNote.Trim(),
                    _authService.CurrentUser?.Id);

                CloseOpeningBalancePanel();
                LoadLedger();
                StatusMessage = $"✓ Previous dues Rs. {amount:N0} recorded (Invoice #{bill.InvoiceDisplay}).";
                CustomerEvents.NotifyCreditsChanged();
            }
            catch (Exception ex)
            {
                OpeningBalanceError = ex.Message;
                AppLogger.Error("CustomerLedgerViewModel.SaveOpeningBalance failed", ex);
            }
        }

        // ────────────────────────────────────────────
        //  PAYMENT RECORDING
        // ────────────────────────────────────────────

        private static Bill? AsBill(object? obj)
        {
            if (obj is Bill bill) return bill;
            if (obj is LedgerRow row) return row.Bill;
            return null;
        }

        private void OpenPaymentPanel(Bill? bill)
        {
            if (bill == null || !bill.HasPendingCredit) return;
            ClosePayDuesPanel();
            CloseOpeningBalancePanel();
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

        /// <summary>Same live preview rules as Pay Dues (apply / change / remaining).</summary>
        private void RecalcPaymentPreview()
        {
            double remaining = SelectedBillRemaining;
            double.TryParse(PaymentAmountText, out var cash);
            cash = Math.Max(0, Math.Round(cash, 2));
            PayPreviewApplied = Math.Min(cash, remaining);

            if (IsOnlinePayment)
            {
                PayPreviewChange = 0;
                PayPreviewRemaining = Math.Max(0, Math.Round(remaining - Math.Min(cash, remaining), 2));
                if (cash > remaining + 0.001)
                {
                    PaymentError = $"Online amount (Rs. {cash:N0}) cannot exceed remaining (Rs. {remaining:N0}).\nآن لائن رقم باقی واجب الادا سے زیادہ نہیں ہو سکتی۔";
                }
                else if (!string.IsNullOrEmpty(PaymentError) &&
                         PaymentError.Contains("cannot exceed remaining", StringComparison.OrdinalIgnoreCase))
                {
                    PaymentError = string.Empty;
                }
            }
            else
            {
                PayPreviewChange = Math.Max(0, Math.Round(cash - remaining, 2));
                PayPreviewRemaining = Math.Max(0, Math.Round(remaining - cash, 2));
                if (!string.IsNullOrEmpty(PaymentError) &&
                    (PaymentError.Contains("Overpayment", StringComparison.OrdinalIgnoreCase) ||
                     PaymentError.Contains("cannot exceed remaining", StringComparison.OrdinalIgnoreCase) ||
                     PaymentError.Contains("exceeds remaining", StringComparison.OrdinalIgnoreCase)))
                {
                    PaymentError = string.Empty;
                }
            }

            OnPropertyChanged(nameof(PayPreviewHasChange));
        }

        // ────────────────────────────────────────────
        //  PAY DUES (FIFO multi-bill)
        // ────────────────────────────────────────────

        private void OpenPayDuesPanel()
        {
            if (Customer == null || !HasPendingDues) return;
            ClosePaymentPanel();
            CloseOpeningBalancePanel();
            LoadLedger(); // ensure pending is fresh
            DuesCashText = string.Empty;
            DuesNote = string.Empty;
            DuesError = string.Empty;
            SelectedPaymentMethod = "Cash";
            SelectedAccount = ActiveAccounts.FirstOrDefault();
            IsPayDuesPanelOpen = true;
            RecalcDuesPreview();
        }

        private void ClosePayDuesPanel()
        {
            IsPayDuesPanelOpen = false;
            DuesCashText = string.Empty;
            DuesNote = string.Empty;
            DuesError = string.Empty;
            SelectedPaymentMethod = "Cash";
            SelectedAccount = ActiveAccounts.FirstOrDefault();
            RecalcDuesPreview();
        }

        private void FillDuesFullPending()
        {
            DuesCashText = TotalPending.ToString("F2");
        }

        private void RecalcDuesPreview()
        {
            double.TryParse(DuesCashText, out var cash);
            cash = Math.Max(0, Math.Round(cash, 2));
            DuesPreviewApplied = Math.Min(cash, TotalPending);

            // Online: no overpay / change — amount must be ≤ pending
            if (IsOnlinePayment)
            {
                DuesPreviewChange = 0;
                DuesPreviewRemaining = Math.Max(0, Math.Round(TotalPending - Math.Min(cash, TotalPending), 2));
                if (cash > TotalPending + 0.001)
                {
                    DuesError = $"Online amount (Rs. {cash:N0}) cannot exceed total pending (Rs. {TotalPending:N0}).\nآن لائن رقم کل واجب الادا سے زیادہ نہیں ہو سکتی۔";
                }
                else if (!string.IsNullOrEmpty(DuesError) && DuesError.Contains("cannot exceed total pending", StringComparison.OrdinalIgnoreCase))
                {
                    DuesError = string.Empty;
                }
            }
            else
            {
                DuesPreviewChange = Math.Max(0, Math.Round(cash - TotalPending, 2));
                DuesPreviewRemaining = Math.Max(0, Math.Round(TotalPending - cash, 2));
                if (!string.IsNullOrEmpty(DuesError) && DuesError.Contains("cannot exceed total pending", StringComparison.OrdinalIgnoreCase))
                    DuesError = string.Empty;
            }

            OnPropertyChanged(nameof(DuesPreviewHasChange));
        }

        private void RecordDuesPayment()
        {
            try
            {
                DuesError = string.Empty;

                if (Customer == null)
                {
                    DuesError = "No customer loaded.";
                    return;
                }

                if (!double.TryParse(DuesCashText, out double cash) || cash <= 0)
                {
                    DuesError = "Please enter a valid amount greater than zero.\nبراہ کرم درست رقم درج کریں۔";
                    return;
                }

                if (IsOnlinePayment && SelectedAccount == null)
                {
                    DuesError = "Please select a payment account for online payment.\nآن لائن ادائیگی کے لیے اکاؤنٹ منتخب کریں۔";
                    return;
                }

                if (TotalPending <= 0.01)
                {
                    DuesError = "This customer has no pending dues.\nاس گاہک کی کوئی واجب الادا رقم نہیں۔";
                    return;
                }

                // Online payments cannot exceed pending (no change for online)
                if (IsOnlinePayment && cash > TotalPending + 0.001)
                {
                    DuesError = $"Online amount (Rs. {cash:N0}) cannot exceed total pending (Rs. {TotalPending:N0}).\nآن لائن رقم کل واجب الادا سے زیادہ نہیں ہو سکتی۔";
                    return;
                }

                var result = _creditService.RecordDuesPayment(
                    Customer.CustomerId,
                    cash,
                    DuesNote,
                    SelectedPaymentMethod);

                string methodDisplay = IsOnlinePayment
                    ? $"{SelectedPaymentMethod} ({SelectedAccount?.DisplayName})"
                    : SelectedPaymentMethod;

                // Pay Dues does NOT print — payment slips are only for individual bill Pay Due
                LoadLedger();

                var lines = new System.Text.StringBuilder();
                lines.AppendLine($"Cash received: Rs. {result.CashReceived:N2} ({methodDisplay})");
                lines.AppendLine($"Applied to dues: Rs. {result.AppliedAmount:N2}");
                foreach (var a in result.Allocations)
                    lines.AppendLine($"  • Bill #{a.InvoiceNumber}: Rs. {a.AmountPaid:N2}");

                if (result.ChangeGiven > 0.01)
                    lines.AppendLine($"Change given: Rs. {result.ChangeGiven:N2}");

                if (result.IsFullyCleared)
                    lines.AppendLine("All pending dues cleared. Status: Paid.");
                else
                    lines.AppendLine($"Remaining pending: Rs. {result.RemainingPending:N2}");

                StatusMessage = result.IsFullyCleared
                    ? $"✓ Dues cleared. Applied Rs. {result.AppliedAmount:N2}." +
                      (result.ChangeGiven > 0.01 ? $" Change Rs. {result.ChangeGiven:N2}." : "")
                    : $"✓ Applied Rs. {result.AppliedAmount:N2}. Remaining pending Rs. {result.RemainingPending:N2}.";

                CustomerEvents.NotifyCreditsChanged();
                SalesEvents.NotifyChanged();

                MessageBox.Show(lines.ToString(), "Pay Dues Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                OnPropertyChanged(nameof(StatusMessage));
                ClosePayDuesPanel();
                return;
            }
            catch (Exception ex)
            {
                DuesError = ex.Message;
                AppLogger.Error("CustomerLedgerViewModel.RecordDuesPayment failed", ex);
            }
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

                if (!double.TryParse(PaymentAmountText, out double cash) || cash <= 0)
                {
                    PaymentError = "Please enter a valid amount greater than zero.\nبراہ کرم درست رقم درج کریں۔";
                    return;
                }

                if (IsOnlinePayment && SelectedAccount == null)
                {
                    PaymentError = "Please select a payment account for online payment.\nآن لائن ادائیگی کے لیے اکاؤنٹ منتخب کریں۔";
                    return;
                }

                double remaining = SelectedBill.RemainingAmount;
                if (remaining <= 0.01)
                {
                    PaymentError = "This bill is already fully paid.";
                    return;
                }

                // Online: no change — cannot exceed remaining (same as Pay Dues)
                if (IsOnlinePayment && cash > remaining + 0.001)
                {
                    PaymentError = $"Online amount (Rs. {cash:N0}) cannot exceed remaining (Rs. {remaining:N0}).\nآن لائن رقم باقی واجب الادا سے زیادہ نہیں ہو سکتی۔";
                    return;
                }

                string invoiceNumber = SelectedBill.InvoiceNumber;
                int billId = SelectedBill.BillId;

                // Shared payment module (cash overpay → change)
                var result = _creditService.RecordPayment(
                    SelectedBill.BillId, cash, PaymentNote, SelectedPaymentMethod);

                try
                {
                    result.Bill.Customer = Customer;
                    _printService.PrintPaymentReceipt(
                        result.Bill, result.AppliedAmount, _authService.CurrentUser?.FullName ?? "Cashier");
                }
                catch (Exception pex)
                {
                    AppLogger.Error("Payment receipt print failed (Ledger)", pex);
                }

                string methodDisplay = IsOnlinePayment
                    ? $"{SelectedPaymentMethod} ({SelectedAccount?.DisplayName})"
                    : SelectedPaymentMethod;

                var lines = new System.Text.StringBuilder();
                lines.AppendLine($"Cash received: Rs. {result.CashReceived:N2} ({methodDisplay})");
                lines.AppendLine($"Applied to Bill #{invoiceNumber}: Rs. {result.AppliedAmount:N2}");
                if (result.ChangeGiven > 0.01)
                    lines.AppendLine($"Change given: Rs. {result.ChangeGiven:N2}");
                if (result.IsFullyPaid)
                    lines.AppendLine("Bill fully paid.");
                else
                    lines.AppendLine($"Remaining on bill: Rs. {result.RemainingAfter:N2}");

                StatusMessage = result.IsFullyPaid
                    ? $"✓ Bill #{invoiceNumber} paid. Applied Rs. {result.AppliedAmount:N2}." +
                      (result.ChangeGiven > 0.01 ? $" Change Rs. {result.ChangeGiven:N2}." : "")
                    : $"✓ Applied Rs. {result.AppliedAmount:N2} to Bill #{invoiceNumber}. Remaining Rs. {result.RemainingAfter:N2}.";

                LoadLedger();

                var refreshedBill = _creditService.GetBillById(billId);
                if (refreshedBill != null)
                {
                    refreshedBill.Customer = Customer;
                    SelectedBill = refreshedBill;
                }

                CustomerEvents.NotifyCreditsChanged();
                SalesEvents.NotifyChanged();

                MessageBox.Show(lines.ToString(), "Payment Recorded", MessageBoxButton.OK, MessageBoxImage.Information);
                OnPropertyChanged(nameof(StatusMessage));
                ClosePaymentPanel();
            }
            catch (Exception ex)
            {
                PaymentError = ex.Message;
                AppLogger.Error("CustomerLedgerViewModel.RecordPayment failed", ex);
            }
        }
        private void ViewLedgerRow(object? obj)
        {
            if (obj is LedgerRow { IsPayment: true, Payment: not null } payRow)
            {
                ViewBill(_creditService.GetBillById(payRow.Payment.BillId));
                return;
            }

            ViewBill(AsBill(obj));
        }

        private void ViewBill(Bill? bill)
        {
            if (bill == null) return;

            try 
            {
                var full = _billRepo.GetById(bill.BillId) ?? bill;
                full.Customer ??= Customer;
                _billRepo.LoadAuditLogs(full);
                SelectedBill = full;
                BuildBillAuditTimeline(full);
                SelectedReturnDetail = null;
                IsBillDetailOpen = true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load bill audit details", ex);
                ShowPopupError("Failed to load detailed bill history.");
            }
        }

        private void PrintLedgerRow(object? obj)
        {
            if (obj is LedgerRow { IsPayment: true, Payment: not null } payRow)
            {
                try
                {
                    var pay = payRow.Payment;
                    var bill = _creditService.GetBillById(pay.BillId);
                    if (bill == null)
                    {
                        ShowPopupError("Failed to print payment.");
                        return;
                    }
                    bill.Customer = Customer;
                    _printService.PrintPaymentReceipt(
                        bill,
                        pay.AmountPaid,
                        _authService.CurrentUser?.FullName ?? "System Admin");
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Failed to print payment from ledger", ex);
                    ShowPopupError("Failed to print payment.");
                }
                return;
            }

            PrintBill(AsBill(obj));
        }

        private void PrintBill(Bill? bill)
        {
            if (bill == null || Customer == null) return;
            try
            {
                _billRepo.LoadAuditLogs(bill);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load bill audit logs before printing", ex);
            }

            var ok = _printService.PrintInvoiceLedgerStatement(bill.BillId);
            if (!ok)
            {
                _printService.PrintReceipt(bill, _authService.CurrentUser?.FullName ?? "System Admin");
            }
        }

        private List<LedgerPdfRow> ToPdfRows() =>
            LedgerEntries.Select(r => new LedgerPdfRow
            {
                CreatedAt = r.CreatedAt,
                InvoiceDisplay = r.InvoiceDisplay,
                SubtotalDisplay = r.SubtotalDisplay,
                PreviousCredit = r.PreviousCredit,
                TotalBanam = r.TotalBanam,
                ReceivedAmount = r.ReceivedAmount,
                PendingCredit = r.PendingCredit,
                IsPayment = r.IsPayment,
                IsOpening = r.Bill?.IsOpeningBalance == true
            }).ToList();

        private void SaveLedgerPdf()
        {
            if (Customer == null)
            {
                ShowPopupError("No customer selected.");
                return;
            }
            if (LedgerEntries.Count == 0)
            {
                ShowPopupError("No ledger entries found to save.");
                return;
            }

            try
            {
                var safeName = string.Concat((Customer.FullName ?? "Customer")
                    .Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));
                if (string.IsNullOrWhiteSpace(safeName))
                    safeName = "Customer";

                var dlg = new SaveFileDialog
                {
                    Filter = "PDF (*.pdf)|*.pdf",
                    FileName = $"PMC_Ledger_{safeName}_{DateTime.Now:yyyyMMdd}.pdf",
                    OverwritePrompt = true
                };
                if (dlg.ShowDialog() != true)
                    return;

                LedgerPdfService.Save(dlg.FileName, Customer, ToPdfRows(), TotalCredit, TotalPaid, TotalPending);
                ShowPopupSuccess("Ledger PDF saved.");
                try
                {
                    Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
                }
                catch
                {
                    /* file is saved even if the viewer cannot open */
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to save ledger PDF", ex);
                ShowPopupError("Failed to save ledger PDF.");
            }
        }

        private void PrintLedger()
        {
            if (Customer == null)
            {
                ShowPopupError("No customer selected.");
                return;
            }
            if (LedgerEntries.Count == 0)
            {
                ShowPopupError("No ledger entries found to print.");
                return;
            }

            try
            {
                var ok = LedgerPdfService.Print(Customer, ToPdfRows(), TotalCredit, TotalPaid, TotalPending);
                if (!ok)
                    ShowPopupError("Ledger print cancelled.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to print customer ledger", ex);
                ShowPopupError("Failed to print customer ledger.");
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
            // runningPending tracks how much the customer still owes.
            // We start at GrandTotal and subtract each payment in chronological order.
            double runningPending = Math.Max(0, bill.GrandTotal);

            foreach (var payment in bill.PaymentLogs)
            {
                bool isRefund = string.Equals(payment.TransactionType, "Refund", StringComparison.OrdinalIgnoreCase);
                if (isRefund)
                {
                    // Refund is represented inside the RETURN row note to avoid duplicate rows.
                    continue;
                }

                bool isSale = string.Equals(payment.TransactionType, "Sale", StringComparison.OrdinalIgnoreCase);

                BillAuditTimeline.Add(new BillAuditTimelineEntry
                {
                    Date = payment.PaidAt,
                    Type = isSale ? "Sale Created" : "Payment",
                    Method = string.IsNullOrWhiteSpace(payment.PaymentMethod) ? "Cash" : payment.PaymentMethod,
                    Amount = Math.Abs(payment.AmountPaid),
                    Note = BuildPaymentNote(payment, bill),
                    IsReturn = false,
                    IsRefund = false,
                    // Sale Created MUST sort first (SortOrder=0); regular payments sort after returns (SortOrder=3)
                    SortOrder = isSale ? 0 : 3,
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

            // Sale Created = SortOrder 0 → ALWAYS first, regardless of timestamp
            // Returns      = SortOrder 1 → before same-timestamp refund detail
            // Payments     = SortOrder 3 → after returns within same timestamp
            var ordered = BillAuditTimeline
                .OrderBy(x => x.SortOrder)       // primary: Sale Created (0) always first
                .ThenBy(x => x.Date)             // secondary: chronological within same tier
                .ThenBy(x => x.SourceOrderId)    // tertiary: insertion order tiebreak
                .ToList();

            // Build "remaining after this row" in strict chronological order.
            // The Sale Created row's BalanceImpact = initial payment at the time of sale.
            // After Sale row  → remaining = GrandTotal - InitialPayment (opening credit)
            // After Payment   → remaining = previous remaining - payment amount
            // After Return    → remaining = previous remaining - credit adjusted
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
                    // Subtract this payment's contribution from the running balance.
                    // For Sale Created: BalanceImpact = InitialPayment → remaining becomes GrandTotal - InitialPayment
                    // For Payment:      BalanceImpact = payment amount  → remaining decreases further
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
    }
}
