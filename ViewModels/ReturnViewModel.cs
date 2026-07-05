using System;
using System.Runtime.Versioning;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Text.RegularExpressions;
using GroceryPOS.Helpers;
using GroceryPOS.Models;
using GroceryPOS.Services;
using GroceryPOS.Exceptions;

namespace GroceryPOS.ViewModels
{
    [SupportedOSPlatform("windows")]
    public class ReturnViewModel : BaseViewModel
    {
        private readonly IReturnService _returnService;
        private readonly CustomerService _customerService;
        private readonly BillService _billService;
        private readonly AuthService _authService;
        private readonly PrintService _printService;

        private string _billIdInput = string.Empty;
        public string BillIdInput
        {
            get => _billIdInput;
            set => SetProperty(ref _billIdInput, value);
        }

        private string _customerPhoneInput = string.Empty;
        public string CustomerPhoneInput
        {
            get => _customerPhoneInput;
            set
            {
                var digitsOnly = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
                if (digitsOnly.Length > 11)
                    digitsOnly = digitsOnly[..11];

                SetProperty(ref _customerPhoneInput, digitsOnly);
            }
        }

        public ObservableCollection<Bill> CustomerBills { get; } = new();
        private bool _isPopulatingCustomerBills;

        private Bill? _selectedCustomerBill;
        public Bill? SelectedCustomerBill
        {
            get => _selectedCustomerBill;
            set
            {
                if (!SetProperty(ref _selectedCustomerBill, value))
                    return;

                // Auto-load selected bill from dropdown (user no longer needs to press "Load").
                if (!_isPopulatingCustomerBills && value != null)
                    _ = LoadBillById(value.BillId);
            }
        }

        public bool HasCustomerBills => CustomerBills.Count > 0;

        private Bill? _originalBill;
        public Bill? OriginalBill
        {
            get => _originalBill;
            set
            {
                if (SetProperty(ref _originalBill, value))
                {
                    NotifyBillDisplayProperties();
                }
            }
        }

        public ObservableCollection<ReturnItemViewModel> Items { get; } = new();
        public ObservableCollection<BillReturn> ReturnHistory { get; } = new();

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isPreviewVisible;
        public bool IsPreviewVisible
        {
            get => _isPreviewVisible;
            set => SetProperty(ref _isPreviewVisible, value);
        }

        // ── Return Outcome Result (populated after ProcessReturn) ──
        private double _returnResultCashRefund;
        public double ReturnResultCashRefund
        {
            get => _returnResultCashRefund;
            set
            {
                if (SetProperty(ref _returnResultCashRefund, value))
                    OnPropertyChanged(nameof(ShowCashRefundRow));
            }
        }

        private double _returnResultCreditAdjusted;
        public double ReturnResultCreditAdjusted
        {
            get => _returnResultCreditAdjusted;
            set
            {
                if (SetProperty(ref _returnResultCreditAdjusted, value))
                    OnPropertyChanged(nameof(ShowCreditAdjustRow));
            }
        }

        private string _returnResultType = string.Empty;
        public string ReturnResultType
        {
            get => _returnResultType;
            set => SetProperty(ref _returnResultType, value);
        }

        public bool ShowCashRefundRow    => ReturnResultCashRefund > 0;
        public bool ShowCreditAdjustRow  => ReturnResultCreditAdjusted > 0;

        // ── Bill Details Banner Display Helpers ──
        public bool HasOriginalBill => OriginalBill != null;
        public string DisplayCustomerName => OriginalBill?.Customer?.FullName ?? "Walk-in Customer";
        public string DisplayCustomerPhone => string.IsNullOrWhiteSpace(OriginalBill?.Customer?.Phone) ? "—" : OriginalBill!.Customer!.Phone;
        public string DisplayCustomerAddress => string.IsNullOrWhiteSpace(OriginalBill?.Customer?.Address) 
            ? (string.IsNullOrWhiteSpace(OriginalBill?.BillingAddress) ? "No address recorded" : OriginalBill.BillingAddress!) 
            : OriginalBill!.Customer!.Address!;
        public string DisplayInvoiceNumber => OriginalBill?.InvoiceNumber != null ? $"Bill #{OriginalBill.InvoiceNumber}" : "—";
        public string DisplayBillDate => OriginalBill?.CreatedAt.ToString("dd MMM yyyy, hh:mm tt") ?? "—";
        public string DisplayPaymentMethod => OriginalBill?.OnlinePaymentMethod != null ? $"{OriginalBill.PaymentMethod} ({OriginalBill.OnlinePaymentMethod})" : (OriginalBill?.PaymentMethod ?? "Cash");

        // Always static — the original bill total never changes
        public double DisplayOriginalTotal => OriginalBill?.GrandTotal ?? 0;

        // All returns: already returned (from DB) + what is currently being entered live
        public double DisplayReturnedAmount => OriginalBill == null ? 0 :
            Math.Round(OriginalBill.ReturnedAmount + (double)CurrentReturnGrandTotal, 2);

        // Net total after all returns (including live)
        private double NetAfterAllReturns => OriginalBill == null ? 0 :
            Math.Max(0, Math.Round(OriginalBill.GrandTotal - DisplayReturnedAmount, 2));

        // Paid amount = capped at what goods are still worth after all returns.
        // If goods value is 0 (all returned), paid effectively becomes 0.
        public double DisplayPaidAmount => OriginalBill == null ? 0 :
            Math.Round(Math.Min(OriginalBill.PaidAmount, NetAfterAllReturns), 2);

        // Remaining = what's still owed against remaining goods (after paid)
        public double DisplayRemainingDue => OriginalBill == null ? 0 :
            Math.Max(0, Math.Round(NetAfterAllReturns - DisplayPaidAmount, 2));

        // ── Live Return Cart Summary Helpers ──
        public double LiveCreditRecovery => OriginalBill == null ? 0 : Math.Min(OriginalBill.RemainingAmount, (double)CurrentReturnGrandTotal);
        public double LiveCashRefund => OriginalBill == null ? 0 : Math.Max(0, Math.Round((double)CurrentReturnGrandTotal - LiveCreditRecovery, 2));
        public bool ShowLiveCreditRecovery => LiveCreditRecovery > 0;
        public bool ShowLiveCashRefund => LiveCashRefund > 0;
        public bool HasLiveReturn => CurrentReturnGrandTotal > 0;

        private void NotifyBillDisplayProperties()
        {
            OnPropertyChanged(nameof(DisplayCustomerName));
            OnPropertyChanged(nameof(DisplayCustomerPhone));
            OnPropertyChanged(nameof(DisplayCustomerAddress));
            OnPropertyChanged(nameof(DisplayInvoiceNumber));
            OnPropertyChanged(nameof(DisplayBillDate));
            OnPropertyChanged(nameof(DisplayPaymentMethod));
            OnPropertyChanged(nameof(DisplayOriginalTotal));
            OnPropertyChanged(nameof(DisplayPaidAmount));
            OnPropertyChanged(nameof(DisplayRemainingDue));
            OnPropertyChanged(nameof(HasOriginalBill));
            OnPropertyChanged(nameof(LiveCreditRecovery));
            OnPropertyChanged(nameof(LiveCashRefund));
            OnPropertyChanged(nameof(ShowLiveCreditRecovery));
            OnPropertyChanged(nameof(ShowLiveCashRefund));
            OnPropertyChanged(nameof(HasLiveReturn));
        }

        public string StoreName => "GROCERY MART";
        public string StoreAddress => "123 Main Street, City Name";
        public string StorePhone => "0300-1234567";

        public ICommand SearchCommand { get; }
        public ICommand SearchByPhoneCommand { get; }
        public ICommand LoadSelectedBillCommand { get; }
        public ICommand ProcessReturnCommand { get; }
        public ICommand ClearFormCommand { get; }
        public ICommand TogglePreviewCommand { get; }

        // ── Preview Calculation Properties ──
        public decimal CurrentReturnGrandTotal => Items.Sum(i => (decimal)i.ReturnQuantity * (decimal)(OriginalBill?.Items.FirstOrDefault(bi => bi.ItemId == i.ItemId)?.UnitPrice ?? 0));
        
        public double PreviewRemainingDue
        {
            get
            {
                if (OriginalBill == null) return 0;
                return Math.Max(0, Math.Round(OriginalBill.RemainingAmount - (double)CurrentReturnGrandTotal, 2));
            }
        }

        public ObservableCollection<ReturnItemViewModel> CurrentReturnPreviewItems { get; } = new();

        public void RefreshPreview()
        {
            CurrentReturnPreviewItems.Clear();
            foreach (var item in Items.Where(i => i.ReturnQuantity > 0))
            {
                CurrentReturnPreviewItems.Add(item);
            }
            OnPropertyChanged(nameof(CurrentReturnGrandTotal));
            OnPropertyChanged(nameof(PreviewRemainingDue));
            OnPropertyChanged(nameof(LiveCreditRecovery));
            OnPropertyChanged(nameof(LiveCashRefund));
            OnPropertyChanged(nameof(ShowLiveCreditRecovery));
            OnPropertyChanged(nameof(ShowLiveCashRefund));
            OnPropertyChanged(nameof(HasLiveReturn));
            OnPropertyChanged(nameof(DisplayOriginalTotal));
            OnPropertyChanged(nameof(DisplayReturnedAmount));
            OnPropertyChanged(nameof(DisplayPaidAmount));
            OnPropertyChanged(nameof(DisplayRemainingDue));
        }

        public ReturnViewModel(IReturnService returnService, CustomerService customerService, BillService billService, AuthService authService, PrintService printService)
        {
            _returnService = returnService;
            _customerService = customerService;
            _billService = billService;
            _authService = authService;
            _printService = printService;

            SearchCommand = new RelayCommand(async _ => await SearchBill());
            SearchByPhoneCommand = new RelayCommand(async _ => await SearchBillsByPhone());
            LoadSelectedBillCommand = new RelayCommand(async _ => await LoadSelectedBill());
            ProcessReturnCommand = new RelayCommand(async _ => await ProcessReturn());
            ClearFormCommand = new RelayCommand(_ => ClearForm());
            TogglePreviewCommand = new RelayCommand(_ => IsPreviewVisible = !IsPreviewVisible);
        }

        private async Task SearchBill()
        {
            if (!int.TryParse(BillIdInput, out int billId))
            {
                StatusMessage = "Please enter a valid Bill ID.";
                ShowPopupError("Please enter a valid Bill ID.");
                return;
            }

            CustomerPhoneInput = string.Empty; // Clear phone when searching by Bill ID
            await LoadBillById(billId);
        }

        private async Task LoadBillById(int billId)
        {
            if (billId <= 0)
            {
                StatusMessage = "Please enter a valid Bill ID.";
                ShowPopupError("Please enter a valid Bill ID.");
                return;
            }

            try
            {
                StatusMessage = "Searching...";
                var result = await _returnService.GetBillWithReturnHistory(billId);
                OriginalBill = result.Original;

                // Load return history from database (item-level records)
                var history = await _returnService.GetReturnHistory(OriginalBill.BillId);

                Dispatch(() =>
                {
                    Items.Clear();
                    foreach (var item in OriginalBill.Items)
                    {
                        int alreadyReturned = _returnService.GetTotalReturnedQuantity(OriginalBill.BillId, item.ItemId);
                        var vm = new ReturnItemViewModel
                        {
                            ItemId = item.ItemId,
                            Barcode = item.Barcode,
                            Description = item.ItemDescription,
                            OriginalQuantity = item.Quantity,
                            AlreadyReturned = alreadyReturned,
                            RemainingQuantity = item.Quantity - alreadyReturned,
                            ReturnQuantity = 0,
                            UnitPrice = (decimal)item.UnitPrice,
                            OnQuantityChanged = () => RefreshPreview()
                        };
                        Items.Add(vm);
                    }

                    ReturnHistory.Clear();
                    int lastReturnId = -1;
                    int seqIdx = 0;
                    foreach (var entry in history.OrderBy(r => r.ReturnedAt))
                    {
                        if (entry.Id != lastReturnId)
                        {
                            seqIdx++;
                            lastReturnId = entry.Id;
                        }
                        entry.ReturnBillId = $"Return {seqIdx}";
                        ReturnHistory.Add(entry);
                    }
                    RefreshPreview();
                });

                StatusMessage = $"✓ Bill #{billId} loaded with {result.Returns.Count} previous returns.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                ShowPopupError(ex.Message);
                AppLogger.Error("SearchBill failed", ex);
            }
        }

        private async Task SearchBillsByPhone()
        {
            if (string.IsNullOrWhiteSpace(CustomerPhoneInput))
            {
                StatusMessage = "Enter customer phone number to search bills.";
                ShowPopupError("Enter customer phone number to search bills.");
                return;
            }

            if (!Regex.IsMatch(CustomerPhoneInput, @"^0\d{10}$"))
            {
                StatusMessage = "Phone number must be exactly 11 digits and start with 0.";
                ShowPopupError("Phone number must be exactly 11 digits and start with 0.");
                return;
            }

            BillIdInput = string.Empty; // Clear Bill ID when searching by phone
            var normalizedPhone = _customerService.NormalizePhone(CustomerPhoneInput);

            try
            {
                StatusMessage = "Searching customer by phone...";
                var customer = _customerService.GetCustomerByPhone(normalizedPhone);
                if (customer == null)
                {
                    Dispatch(() =>
                    {
                        CustomerBills.Clear();
                        SelectedCustomerBill = null;
                        OnPropertyChanged(nameof(HasCustomerBills));
                    });
                    StatusMessage = "No registered customer found with this phone number.";
                    ShowPopupError("No registered customer found with this phone number.");
                    return;
                }

                var bills = _billService
                    .GetBillsByCustomerId(customer.CustomerId)
                    .Where(b => b.BillId > 0)
                    .OrderByDescending(b => b.CreatedAt)
                    .ToList();

                Dispatch(() =>
                {
                    _isPopulatingCustomerBills = true;
                    SelectedCustomerBill = null; // Reset selection to ensure change notification later
                    CustomerBills.Clear();
                    foreach (var bill in bills)
                        CustomerBills.Add(bill);

                    _isPopulatingCustomerBills = false;

                    // Automatically select and load the first bill (latest) if any exist
                    SelectedCustomerBill = CustomerBills.FirstOrDefault();
                    
                    OnPropertyChanged(nameof(HasCustomerBills));
                });

                StatusMessage = bills.Count == 0
                    ? $"Customer found ({customer.FullName}), but no bills available."
                    : $"Found {bills.Count} bill(s) for {customer.FullName}. Select one to load.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                ShowPopupError(ex.Message);
                AppLogger.Error("SearchBillsByPhone failed", ex);
            }

            await Task.CompletedTask;
        }

        private async Task LoadSelectedBill()
        {
            if (SelectedCustomerBill == null)
            {
                StatusMessage = "Please select a bill from the customer bills list.";
                ShowPopupError("Please select a bill from the customer bills list.");
                return;
            }

            BillIdInput = SelectedCustomerBill.BillId.ToString();
            await LoadBillById(SelectedCustomerBill.BillId);
        }

        private async Task ProcessReturn()
        {
            if (OriginalBill == null) return;

            var itemsToReturn = Items.Where(i => i.ReturnQuantity > 0)
                .Select(i => new BillDescription
                {
                    ItemId = i.ItemId,
                    Quantity = i.ReturnQuantity,
                    ItemDescription = i.Description
                }).ToList();

            if (!itemsToReturn.Any())
            {
                StatusMessage = "No items selected for return.";
                ShowPopupError("No items selected for return.");
                return;
            }

            try
            {
                StatusMessage = "Processing return...";
                var returnBill = await _returnService.ProcessReturn(OriginalBill.BillId, _authService.CurrentUser?.Id, itemsToReturn);

                // ── Populate result properties from return bill metadata ──
                double cashRefund       = returnBill.CashReceived;
                double creditAdjusted   = returnBill.RemainingDueAfterThisReturn;
                string outcomeType      = returnBill.Status; // "CashOnly" | "CreditOnly" | "Mixed"

                ReturnResultCashRefund    = cashRefund;
                ReturnResultCreditAdjusted = creditAdjusted;
                ReturnResultType          = outcomeType;

                // Build descriptive success message
                string msg = outcomeType switch
                {
                    "CreditOnly" => $"✓ Return Bill #{returnBill.InvoiceNumber} — Credit reduced by Rs.{creditAdjusted:N0}. No cash refund.",
                    "CashOnly"   => $"✓ Return Bill #{returnBill.InvoiceNumber} — Cash refund: Rs.{cashRefund:N0}",
                    "Mixed"      => $"✓ Return Bill #{returnBill.InvoiceNumber} — Credit reduced Rs.{creditAdjusted:N0} + Cash refund Rs.{cashRefund:N0}",
                    _            => $"✓ Return processed! Return Bill: {returnBill.InvoiceNumber}"
                };
                StatusMessage = msg;
                
                // ── Print Return-Only Receipt ──
                try
                {
                    _printService.PrintReturnOnlyReceipt(OriginalBill, returnBill, _authService.CurrentUser?.FullName ?? "Cashier");
                }
                catch (Exception pex)
                {
                    StatusMessage += " (Print failed)";
                    AppLogger.Error("Return receipt print failed", pex);
                }

                // ── Refresh the current view instead of clearing it ──
                // This lets the user see the updated "Already Returned" quantities and the new history entry.
                await LoadBillById(OriginalBill.BillId);
                
                // Keep the success message visible even after SearchBill updates it
                StatusMessage = $"✓ Return processed! Return Bill: {returnBill.InvoiceNumber}";
            }
            catch (BusinessException ex)
            {
                StatusMessage = $"Business Rule: {ex.Message}";
                ShowPopupError(ex.Message);
            }
            catch (Exception ex)
            {
                StatusMessage = $"An error occurred: {ex.Message}";
                ShowPopupError(ex.Message);
                AppLogger.Error("ProcessReturn failed", ex);
            }
        }


        public void ClearForm()
        {
            OriginalBill = null;
            Dispatch(() => Items.Clear());
            Dispatch(() => ReturnHistory.Clear());
            RefreshPreview();
            Dispatch(() => CustomerBills.Clear());
            _isPopulatingCustomerBills = false;
            SelectedCustomerBill = null;
            BillIdInput = string.Empty;
            CustomerPhoneInput = string.Empty;
            OnPropertyChanged(nameof(HasCustomerBills));
            StatusMessage = "Form cleared.";
        }
    }

    public class ReturnItemViewModel : BaseViewModel
    {
        public string ItemId { get; set; } = string.Empty;
        public string? Barcode { get; set; }
        public string Description { get; set; } = string.Empty;
        public double OriginalQuantity { get; set; }
        public double AlreadyReturned { get; set; }
        public double RemainingQuantity { get; set; }
        public Action? OnQuantityChanged { get; set; }

        private double _returnQuantity;
        public double ReturnQuantity
        {
            get => _returnQuantity;
            set
            {
                double clamped = value;
                if (clamped > RemainingQuantity) clamped = RemainingQuantity;
                if (clamped < 0) clamped = 0;

                bool changed = (_returnQuantity != clamped);
                bool wasClamped = (value != clamped);

                _returnQuantity = clamped;

                if (changed)
                {
                    OnPropertyChanged(nameof(ReturnQuantity));
                    OnPropertyChanged(nameof(TotalPrice));
                }

                if (wasClamped)
                {
                    // In WPF Two-Way binding with UpdateSourceTrigger=PropertyChanged,
                    // synchronous PropertyChanged notifications are ignored by the active TextBox while typing.
                    // Schedule an asynchronous update on the UI thread so the TextBox immediately reflects the clamped value.
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        OnPropertyChanged(nameof(ReturnQuantity));
                        OnPropertyChanged(nameof(TotalPrice));
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }

                if (changed || wasClamped)
                {
                    OnQuantityChanged?.Invoke();
                    if (Application.Current?.MainWindow?.DataContext is MainViewModel mainVM && 
                        mainVM.CurrentView is ReturnViewModel returnVM)
                    {
                        returnVM.RefreshPreview();
                    }
                }
            }
        }

        public decimal UnitPrice { get; set; }
        public decimal TotalPrice => (decimal)ReturnQuantity * UnitPrice;
    }
}
