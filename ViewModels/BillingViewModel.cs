using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Threading.Tasks;
using GroceryPOS.Helpers;
using GroceryPOS.Models;
using GroceryPOS.Services;
using GroceryPOS.Data.Repositories;

namespace GroceryPOS.ViewModels
{
    public class BillingViewModel : BaseViewModel
    {
        private readonly AuthService _authService;
        private readonly ItemService _itemService;
        private readonly BillService _billService;
        private readonly PrintService _printService;
        private readonly IStockService _stockService;
        private readonly CustomerService _customerService;
        private readonly CreditService _creditService;
        private readonly BillRepository _billRepo;
        private readonly AccountService _accountService;
        private readonly System.Windows.Threading.DispatcherTimer _timer;

        public ObservableCollection<BillingTab> Tabs { get; set; } = new();
        private BillingTab? _selectedTab;
        public BillingTab? SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (_selectedTab != null)
                {
                    _selectedTab.IsActive = false;
                    _selectedTab.CartItems.CollectionChanged -= OnCartItemsCollectionChanged;
                    foreach (var item in _selectedTab.CartItems) item.PropertyChanged -= OnCartItemPropertyChanged;
                }
                
                if (SetProperty(ref _selectedTab, value))
                {
                    if (_selectedTab != null)
                    {
                        _selectedTab.IsActive = true;
                        _selectedTab.CartItems.CollectionChanged += OnCartItemsCollectionChanged;
                        foreach (var item in _selectedTab.CartItems) item.PropertyChanged += OnCartItemPropertyChanged;
                    }
                    NotifyTabPropertiesChanged();
                    RecalculateTotal();
                }
            }
        }

        public ObservableCollection<CartItem> CartItems => SelectedTab?.CartItems ?? new();
        public string DiscountText { get => SelectedTab?.DiscountText ?? "0"; set { if (SelectedTab != null) { SelectedTab.DiscountText = value; RecalculateTotal(); OnPropertyChanged(); } } }
        public string TaxText { get => SelectedTab?.TaxText ?? "0"; set { if (SelectedTab != null) { SelectedTab.TaxText = value; RecalculateTotal(); OnPropertyChanged(); } } }
        public string CashReceivedText { get => SelectedTab?.CashReceivedText ?? "0"; set { if (SelectedTab != null) { if (!IsCashPayment && HasSelectedCustomer && double.TryParse(value, out var v) && v > GrandTotal + 0.001) { SelectedTab.CashReceivedText = GrandTotal.ToString("F2"); } else { SelectedTab.CashReceivedText = value; } CalculateChange(); OnPropertyChanged(); } } }
        public string InvoiceNumber { get => SelectedTab?.InvoiceNumber ?? "00000"; set { if (SelectedTab != null) { SelectedTab.InvoiceNumber = value; OnPropertyChanged(); } } }

        // History Preview & Payment
        public Bill? PreviewHistoryBill => SelectedTab?.PreviewHistoryBill;
        public bool IsHistoryPaymentOpen { get => SelectedTab?.IsHistoryPaymentOpen ?? false; set { if (SelectedTab != null) { SelectedTab.IsHistoryPaymentOpen = value; OnPropertyChanged(); } } }
        public string HistoryPaymentAmount { get => SelectedTab?.HistoryPaymentAmount ?? ""; set { if (SelectedTab != null) { SelectedTab.HistoryPaymentAmount = value; OnPropertyChanged(); } } }
        public string HistoryPaymentNote { get => SelectedTab?.HistoryPaymentNote ?? ""; set { if (SelectedTab != null) { SelectedTab.HistoryPaymentNote = value; OnPropertyChanged(); } } }
        public string HistoryPaymentError { get => SelectedTab?.HistoryPaymentError ?? ""; set { if (SelectedTab != null) { SelectedTab.HistoryPaymentError = value; OnPropertyChanged(); } } }
        public bool IsBillDetailOpen { get => SelectedTab?.IsBillDetailOpen ?? false; set { if (SelectedTab != null) { SelectedTab.IsBillDetailOpen = value; OnPropertyChanged(); } } }

        public Customer? SelectedCustomer { get => SelectedTab?.Customer; set { if (SelectedTab != null) { SelectedTab.Customer = value; SelectedTab.CustomerId = value?.CustomerId; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedCustomer)); OnPropertyChanged(nameof(IsWalkIn)); OnPropertyChanged(nameof(IsAmountEditable)); OnPropertyChanged(nameof(CustomerDisplayName)); CalculateChange(); } } }
        public bool HasSelectedCustomer => SelectedCustomer != null;
        public bool IsWalkIn => SelectedCustomer == null;

        // ── Store Credit ──
        public double PendingCreditAmount
        {
            get => SelectedTab?.PendingCreditAmount ?? 0;
            set { if (SelectedTab != null) { SelectedTab.PendingCreditAmount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPendingCredit)); OnPropertyChanged(nameof(PendingCreditDisplay)); } }
        }
        public bool HasPendingCredit => PendingCreditAmount > 0 && HasSelectedCustomer;
        public string PendingCreditDisplay => $"⚠ This customer has Rs. {PendingCreditAmount:N0} pending.";


        public string CustomerSearchQuery { get => SelectedTab?.CustomerSearchQuery ?? string.Empty; set { if (SelectedTab != null && SelectedTab.CustomerSearchQuery != value) { SelectedTab.CustomerSearchQuery = value; SearchCustomers(); OnPropertyChanged(); } } }
        public ObservableCollection<Customer> CustomerSearchResults => SelectedTab?.CustomerSearchResults ?? new();
        public ObservableCollection<Bill> CustomerBills => SelectedTab?.CustomerBills ?? new();
        public Customer? SelectedSearchResult { get => SelectedTab?.SelectedSearchResult; set { if (SelectedTab != null) { SelectedTab.SelectedSearchResult = value; OnPropertyChanged(); } } }
        private Bill? _selectedHistoryBill;
        public Bill? SelectedHistoryBill { get => _selectedHistoryBill; set { if (SetProperty(ref _selectedHistoryBill, value) && value != null) { if (SelectedTab != null) { SelectedTab.PreviewHistoryBill = value; OnPropertyChanged(nameof(PreviewHistoryBill)); } _selectedHistoryBill = null; OnPropertyChanged(); } } }

        public bool IsCustomerSearchFocused { get; set; }
        public bool IsRegistrationVisible { get; set; }
        public string NewCustomerName { get; set; } = "";
        public string NewCustomerPhone { get; set; } = "";
        public string NewCustomerSecondaryPhone { get; set; } = "";
        public string NewCustomerAddress { get; set; } = "";
        public string NewCustomerAddress2 { get; set; } = "";
        public string NewCustomerAddress3 { get; set; } = "";
        public string RegistrationErrorMessage { get; set; } = "";

        public ObservableCollection<Item> ItemList { get; set; } = new();
        public ObservableCollection<Item> FilteredItemList { get; set; } = new();
        
        private string _productSearchText = "";
        public string ProductSearchText
        {
            get => _productSearchText;
            set 
            { 
                if (SetProperty(ref _productSearchText, value)) 
                { 
                    // If the text change matches the selected item's description, 
                    // it's likely just arrow-key browsing. Skip filtering to keep the full list.
                    if (SelectedSearchItem != null && SelectedSearchItem.Description == value)
                    {
                        return;
                    }

                    FilterProducts();
                    // Open dropdown if we have text and matches
                    IsProductDropDownOpen = !string.IsNullOrWhiteSpace(value) && FilteredItemList.Any();
                } 
            }
        }

        private bool _isProductDropDownOpen;
        public bool IsProductDropDownOpen
        {
            get => _isProductDropDownOpen;
            set => SetProperty(ref _isProductDropDownOpen, value);
        }

        private Item? _selectedSearchItem;
        public Item? SelectedSearchItem
        {
            get => _selectedSearchItem;
            set => SetProperty(ref _selectedSearchItem, value);
        }
        public string BarcodeInput { get; set; } = "";
        public int QuantityInput { get; set; } = 1;

        private bool _isBarcodeFocused;
        public bool IsBarcodeFocused
        {
            get => _isBarcodeFocused;
            set => SetProperty(ref _isBarcodeFocused, value);
        }

        private string _systemErrorMessage = "";
        public string SystemErrorMessage 
        { 
            get => _systemErrorMessage; 
            set => SetProperty(ref _systemErrorMessage, value); 
        }

        private bool _systemErrorVisible;
        public bool SystemErrorVisible 
        { 
            get => _systemErrorVisible; 
            set => SetProperty(ref _systemErrorVisible, value); 
        }

        private async void ShowSystemError(string message)
        {
            SystemErrorMessage = message;
            SystemErrorVisible = true;
            await Task.Delay(1500);
            SystemErrorVisible = false;
        }

        private void RefocusBarcode()
        {
            IsBarcodeFocused = false;
            OnPropertyChanged(nameof(IsBarcodeFocused));
            IsBarcodeFocused = true;
            OnPropertyChanged(nameof(IsBarcodeFocused));
        }

        public double SubTotal { get; set; }

        // ── Dashboard Stats (inline header) ──
        private double _statTotalSales;
        public double StatTotalSales { get => _statTotalSales; set => SetProperty(ref _statTotalSales, value); }
        private int _statSaleCount;
        public int StatSaleCount { get => _statSaleCount; set => SetProperty(ref _statSaleCount, value); }
        private double _statReturns;
        public double StatReturns { get => _statReturns; set => SetProperty(ref _statReturns, value); }
        private double _statCredit;
        public double StatCredit { get => _statCredit; set => SetProperty(ref _statCredit, value); }
        private double _statRecoveredCredit;
        public double StatRecoveredCredit { get => _statRecoveredCredit; set => SetProperty(ref _statRecoveredCredit, value); }
        private double _statStoreCredit;
        public double StatStoreCredit { get => _statStoreCredit; set => SetProperty(ref _statStoreCredit, value); }
        private double _statCashInDrawer;
        public double StatCashInDrawer { get => _statCashInDrawer; set => SetProperty(ref _statCashInDrawer, value); }
        private double _statOnlinePayments;
        public double StatOnlinePayments { get => _statOnlinePayments; set => SetProperty(ref _statOnlinePayments, value); }
        public ObservableCollection<string> AvailableAddresses { get; } = new();
        private string? _selectedBillingAddress;
        public string? SelectedBillingAddress { get => _selectedBillingAddress; set { _selectedBillingAddress = value; OnPropertyChanged(nameof(SelectedBillingAddress)); } }

        private bool _isAddingAddress;
        public bool IsAddingAddress { get => _isAddingAddress; set { _isAddingAddress = value; OnPropertyChanged(); } }
        private string _newAddressInput = "";
        public string NewAddressInput { get => _newAddressInput; set { _newAddressInput = value; OnPropertyChanged(); } }

        public double DiscountAmount { get; set; }
        public double TaxAmount { get; set; }
        public double GrandTotal { get; set; }
        public double ChangeAmount { get; set; }
        public double ChangeAmountAbs => Math.Abs(ChangeAmount);
        public bool IsChangeNegative => ChangeAmount < -0.01;
        public bool IsChangeAmountVisible
        {
            get
            {
                if (!IsCashPayment && IsWalkIn) return false; // walk-in online: no amount shown
                if (!IsCashPayment && HasSelectedCustomer)
                {
                    double.TryParse(CashReceivedText, out var onlineAmt);
                    return onlineAmt < GrandTotal - 0.01; // only show due amount
                }
                return !IsChangeNegative || HasSelectedCustomer;
            }
        }
        
        public string ChangeDisplayLabel 
        { 
            get 
            {
                if (!IsCashPayment && IsWalkIn) return "EXACT PAYMENT";
                if (!IsCashPayment && HasSelectedCustomer)
                {
                    double.TryParse(CashReceivedText, out var onlineAmt);
                    if (onlineAmt < GrandTotal - 0.01) return "DUE AMOUNT";
                    return "EXACT PAYMENT";
                }
                if (IsChangeNegative && IsWalkIn) return "INSUFFICIENT CASH";
                if (IsChangeNegative && HasSelectedCustomer) return "DUE AMOUNT";
                return "RETURN AMOUNT";
            }
        }

        public string ChangeDisplayBrush
        {
            get
            {
                if (!IsCashPayment && IsWalkIn) return "#22C55E";
                if (!IsCashPayment && HasSelectedCustomer)
                {
                    double.TryParse(CashReceivedText, out var onlineAmt);
                    if (onlineAmt < GrandTotal - 0.01) return "#EF4444"; // Red for due
                    return "#22C55E"; // Green for exact
                }
                if (IsChangeNegative) return "#EF4444";
                return "#3B82F6";
            }
        }

        public bool IsAmountEditable => IsCashPayment || (!IsCashPayment && HasSelectedCustomer);

        // ── Preview-specific computed properties ──
        public double PreviewCashReceived { get { double.TryParse(CashReceivedText, out var v); return v; } }
        public double PreviewChange => Math.Max(0, ChangeAmount);
        public bool PreviewHasDue => HasSelectedCustomer && PreviewCashReceived < GrandTotal - 0.01;
        public double PreviewPaidAmount => Math.Min(PreviewCashReceived, GrandTotal);
        public double PreviewDueAmount => Math.Max(0, GrandTotal - PreviewCashReceived);
        public bool PreviewShowTax => TaxAmount > 0;
        public bool PreviewHasCashReceived => PreviewCashReceived > 0 || IsCashPayment;

        public string CurrentDateTime => DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss");
        public DateTime CurrentTime => DateTime.Now;
        public string StatusMessage { get => SelectedTab?.StatusMessage ?? ""; set { if (SelectedTab != null) { SelectedTab.StatusMessage = value; OnPropertyChanged(); } } }
        public bool IsPreviewVisible { get; set; }
        public string StoreName => "GROCERY MART";
        public string StoreAddress => "Rawat, Rawalpindi, Pakistan";
        public string StorePhone => "0300-1234567";
        public string CashierName => _authService.CurrentUser?.FullName ?? "Cashier";
        public string CustomerDisplayName => SelectedCustomer?.FullName ?? "Walk-in";
        public CartItem? SelectedCartItem { get; set; }
        public ICommand OpenBillDetailCommand { get; }
        public ICommand CloseBillDetailCommand { get; }
        public ICommand FinishCartEditCommand { get; }

        public ICommand ScanBarcodeCommand { get; }
        public ICommand RemoveFromCartCommand { get; }
        public ICommand IncreaseQuantityCommand { get; }
        public ICommand DecreaseQuantityCommand { get; }
        // ── Payment Method ──
        public List<string> PaymentMethods { get; } = new() { "Cash", "Online" };

        private ObservableCollection<Account> _activeAccounts = new();
        public ObservableCollection<Account> ActiveAccounts { get => _activeAccounts; set => SetProperty(ref _activeAccounts, value); }

        private Account? _selectedAccount;
        public Account? SelectedAccount
        {
            get => _selectedAccount;
            set
            {
                if (SetProperty(ref _selectedAccount, value))
                {
                    OnPropertyChanged(nameof(SelectedAccount));
                    if (value != null)
                    {
                        SelectedOnlineMethod = value.DisplayName;
                    }
                }
            }
        }

        public List<string> OnlinePaymentMethods { get; } = new() { "Easypaisa", "JazzCash", "Bank Transfer" };

        private string? _selectedOnlineMethod;
        /// <summary>The specific online channel selected by the cashier (Easypaisa / JazzCash / Bank Transfer).</summary>
        public string? SelectedOnlineMethod
        {
            get => _selectedOnlineMethod;
            set
            {
                if (SetProperty(ref _selectedOnlineMethod, value))
                    OnPropertyChanged(nameof(SelectedOnlineMethod));
            }
        }

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
                    OnPropertyChanged(nameof(IsAmountEditable));
                    if (!IsCashPayment)
                    {
                        // Online: auto-set received = grand total
                        CashReceivedText = GrandTotal.ToString("F2");
                    }
                    else
                    {
                        // Cash: clear sub-method and focus the amount field
                        SelectedOnlineMethod = null;
                        CashReceivedText = "";
                        FocusCashReceived = false;
                        OnPropertyChanged(nameof(FocusCashReceived));
                        FocusCashReceived = true;
                        OnPropertyChanged(nameof(FocusCashReceived));
                    }
                    CalculateChange();
                }
            }
        }
        public bool IsCashPayment    => SelectedPaymentMethod == "Cash";
        public bool IsOnlinePayment  => SelectedPaymentMethod == "Online";

        private bool _focusCashReceived;
        public bool FocusCashReceived { get => _focusCashReceived; set => SetProperty(ref _focusCashReceived, value); }

        // ── HISTORY PAYMENT (Billing) METHOD SELECTION ──
        public List<string> HistoryOnlinePaymentMethods { get; } = new() { "Easypaisa", "JazzCash", "Bank Transfer" };
        public ObservableCollection<Account> HistoryActiveAccounts { get; set; } = new();

        public string SelectedHistoryPaymentMethod { get => SelectedTab?.SelectedHistoryPaymentMethod ?? "Cash"; set { if (SelectedTab != null) { SelectedTab.SelectedHistoryPaymentMethod = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsHistoryOnlinePayment)); } } }
        public bool IsHistoryOnlinePayment => SelectedHistoryPaymentMethod == "Online";
        public Account? SelectedHistoryAccount { get => SelectedTab?.SelectedHistoryAccount; set { if (SelectedTab != null) { SelectedTab.SelectedHistoryAccount = value; OnPropertyChanged(); } } }
        public string? SelectedHistoryOnlineMethod { get => SelectedTab?.SelectedHistoryOnlineMethod; set { if (SelectedTab != null) { SelectedTab.SelectedHistoryOnlineMethod = value; OnPropertyChanged(); } } }

        public ICommand CompleteSaleCommand { get; }
        public ICommand ClearCartCommand { get; }
        public ICommand AddTabCommand { get; }
        public ICommand CloseTabCommand { get; }
        public ICommand TogglePreviewCommand { get; }
        public ICommand SelectCustomerCommand { get; }
        public ICommand ClearCustomerCommand { get; }
        public ICommand LoadPreviewToCartCommand { get; }
        public ICommand OpenHistoryPaymentCommand { get; }
        public ICommand CloseHistoryPaymentCommand { get; }
        public ICommand RecordHistoryPaymentCommand { get; }
        public ICommand PayFullHistoryCommand { get; }
        public ICommand ClosePreviewCommand { get; }
        public ICommand ToggleRegistrationCommand { get; }
        public ICommand SaveNewCustomerCommand { get; }
        public ICommand NavigateSearchCommand { get; }
        public ICommand NavigateProductSearchCommand { get; }
        public ICommand PrintBillCommand { get; }

        public ICommand AddAddressCommand { get; }
        public ICommand CancelAddAddressCommand { get; }
        public ICommand SaveAddressCommand { get; }

        public BillingViewModel(AuthService authService, ItemService itemService, BillService billService, PrintService printService, IStockService stockService, CustomerService customerService, CreditService creditService, BillRepository billRepo, AccountService accountService)
        {
            _authService = authService; _itemService = itemService; _billService = billService; _printService = printService; _stockService = stockService;            _customerService = customerService;
            _creditService   = creditService;
            _billRepo        = billRepo;
            _accountService  = accountService;
            Tabs = new ObservableCollection<BillingTab>(); AddNewTab();
            _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => { OnPropertyChanged(nameof(CurrentTime)); OnPropertyChanged(nameof(CurrentDateTime)); };
            _timer.Start();

            LoadHistoryActiveAccounts();

            ScanBarcodeCommand = new RelayCommand(_ => ScanBarcode());
            RemoveFromCartCommand = new RelayCommand(_ => RemoveFromCart());
            IncreaseQuantityCommand = new RelayCommand(_ => IncreaseQuantity());
            DecreaseQuantityCommand = new RelayCommand(_ => DecreaseQuantity());
            CompleteSaleCommand = new RelayCommand(_ => CompleteSale());
            ClearCartCommand = new RelayCommand(_ => ClearCart());
            AddTabCommand = new RelayCommand(_ => AddNewTab());
            CloseTabCommand = new RelayCommand(obj => CloseTab(obj as BillingTab));
            TogglePreviewCommand = new RelayCommand(() => { IsPreviewVisible = !IsPreviewVisible; OnPropertyChanged(nameof(IsPreviewVisible)); });
            SelectCustomerCommand = new RelayCommand(obj => SelectCustomer(obj as Customer));
            ClearCustomerCommand = new RelayCommand(_ => ClearCustomer());
            LoadPreviewToCartCommand= new RelayCommand(_ => { if (PreviewHistoryBill != null) LoadBillIntoCart(PreviewHistoryBill); });
            OpenHistoryPaymentCommand = new RelayCommand(_ => { if (PreviewHistoryBill != null) { var fresh = _billRepo.GetById(PreviewHistoryBill.BillId); if (fresh != null && SelectedTab != null) { fresh.Customer = PreviewHistoryBill.Customer; SelectedTab.PreviewHistoryBill = fresh; } HistoryPaymentAmount = ""; HistoryPaymentNote = ""; HistoryPaymentError = ""; SelectedHistoryPaymentMethod = "Cash"; SelectedHistoryAccount = HistoryActiveAccounts.FirstOrDefault(); SelectedHistoryOnlineMethod = null; IsHistoryPaymentOpen = true; OnPropertyChanged(nameof(PreviewHistoryBill)); } });
            CloseHistoryPaymentCommand = new RelayCommand(_ => IsHistoryPaymentOpen = false);
            RecordHistoryPaymentCommand = new RelayCommand(_ => RecordHistoryPayment());
            PayFullHistoryCommand = new RelayCommand(_ => { if (PreviewHistoryBill != null) HistoryPaymentAmount = PreviewHistoryBill.RemainingAmount.ToString("F2"); });
            ClosePreviewCommand = new RelayCommand(_ => { if (SelectedTab != null) { SelectedTab.PreviewHistoryBill = null; OnPropertyChanged(nameof(PreviewHistoryBill)); } });
            ToggleRegistrationCommand = new RelayCommand(() => { IsRegistrationVisible = !IsRegistrationVisible; ClearRegistrationForm(); OnPropertyChanged(nameof(IsRegistrationVisible)); });
            SaveNewCustomerCommand = new RelayCommand(_ => SaveNewCustomer());
            OpenBillDetailCommand = new RelayCommand(_ => { if (PreviewHistoryBill != null) IsBillDetailOpen = true; });
            CloseBillDetailCommand = new RelayCommand(_ => { if (SelectedTab != null) SelectedTab.IsBillDetailOpen = false; OnPropertyChanged(nameof(IsBillDetailOpen)); });
            FinishCartEditCommand = new RelayCommand(_ => { SelectedCartItem = null; OnPropertyChanged(nameof(SelectedCartItem)); RefocusBarcode(); });
            NavigateSearchCommand = new RelayCommand(p => NavigateSearchResults(p?.ToString()));
            NavigateProductSearchCommand = new RelayCommand(p => NavigateProductResults(p?.ToString()));
            PrintBillCommand = new RelayCommand(async _ => { if (SelectedTab?.PreviewHistoryBill is Bill b) await AttemptPrint(b); });

            AddAddressCommand = new RelayCommand(_ => { IsAddingAddress = true; NewAddressInput = ""; });
            CancelAddAddressCommand = new RelayCommand(_ => { IsAddingAddress = false; NewAddressInput = ""; });
            SaveAddressCommand = new RelayCommand(_ => SaveAddress());
            _stockService.StockChanged += LoadDashboardStats;
            LoadDashboardStats();
            LoadProducts();
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

        private void LoadProducts() { var items = _itemService.GetAllItems(); ItemList = new ObservableCollection<Item>(items); FilteredItemList = new ObservableCollection<Item>(items); }
        private void LoadDashboardStats()
        {
            StatTotalSales = _billService.GetTodayTotal();
            StatSaleCount = _billService.GetTodayBillCount();
            StatReturns = _billService.GetTodayReturnsTotal();
            StatCredit = _billService.GetTodayTotalCredit();
            StatRecoveredCredit = _billService.GetTodayRecoveredCredit();
            StatStoreCredit = _billService.GetTodayStoreCredit();
            StatCashInDrawer = _billService.GetTodayCashInDrawer();
            StatOnlinePayments = _billService.GetTodayOnlinePayments();
        }
        private void FilterProducts()
        {
            var query = ProductSearchText ?? "";
            // Always fetch fresh from cache so newly added products appear instantly
            var allItems = _itemService.GetAllItems();
            var filtered = string.IsNullOrWhiteSpace(query) 
                ? allItems 
                : allItems.Where(i => i.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrEmpty(i.Barcode) && i.Barcode.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();

            // Stabilize UI by modifying the collection instead of reassigning it
            var toRemove = FilteredItemList.Where(i => !filtered.Contains(i)).ToList();
            foreach (var item in toRemove) FilteredItemList.Remove(item);

            for (int i = 0; i < filtered.Count; i++)
            {
                if (i >= FilteredItemList.Count || FilteredItemList[i] != filtered[i])
                {
                    if (i < FilteredItemList.Count) FilteredItemList.Insert(i, filtered[i]);
                    else FilteredItemList.Add(filtered[i]);
                }
            }
        }
        private string GetNextAvailableInvoiceNumber()
        {
            // Get base ID from database
            string baseNumStr = _billService.GetNextInvoiceNumber();
            if (!int.TryParse(baseNumStr, out int nextId)) nextId = 1;

            // Find highest already assigned ID in open tabs
            int maxTabId = 0;
            foreach (var tab in Tabs)
            {
                if (int.TryParse(tab.InvoiceNumber, out int tabId))
                {
                    if (tabId > maxTabId) maxTabId = tabId;
                }
            }

            // The resulting ID should be at least (maxTabID + 1), but also at least nextId
            int finalId = Math.Max(nextId, maxTabId + 1);
            return finalId.ToString("D5");
        }

        private void AddNewTab() { var tab = new BillingTab { TabName = $"Bill {Tabs.Count + 1}", InvoiceNumber = GetNextAvailableInvoiceNumber() }; Tabs.Add(tab); SelectedTab = tab; RefocusBarcode(); }
        private void CloseTab(BillingTab? tab) { if (tab == null || Tabs.Count <= 1) return; Tabs.Remove(tab); SelectedTab = Tabs.LastOrDefault(); for (int i = 0; i < Tabs.Count; i++) Tabs[i].TabName = $"Bill {i + 1}"; NotifyTabPropertiesChanged(); }
        private void ScanBarcode() 
        { 
            StatusMessage = string.Empty;
            OnPropertyChanged(nameof(StatusMessage));

            string bc = !string.IsNullOrWhiteSpace(BarcodeInput) ? BarcodeInput : SelectedSearchItem?.Barcode ?? SelectedSearchItem?.ItemId ?? ""; 
            if (string.IsNullOrWhiteSpace(bc)) return; 
            
            // Try barcode lookup first, then fall back to ID lookup
            var it = _itemService.GetItemByBarcode(bc)
                  ?? (int.TryParse(bc, out var id) ? _itemService.GetItemById(id) : null);
            if (it != null) 
            { 
                AddToCart(it); 
                BarcodeInput = ""; 
                SelectedSearchItem = null; 
                ProductSearchText = string.Empty;
                IsProductDropDownOpen = false;
                OnPropertyChanged(nameof(BarcodeInput)); 
                OnPropertyChanged(nameof(ProductSearchText));
                OnPropertyChanged(nameof(IsProductDropDownOpen));
            } 
            else { StatusMessage = "✗ Product not found."; OnPropertyChanged(nameof(StatusMessage)); ShowPopupError("Product not found."); } 
        }
        private void AddToCart(Item it) 
        { 
            if (SelectedTab == null) return; 
            var ex = SelectedTab.CartItems.FirstOrDefault(i => i.ItemId == it.ItemId); 
            if (ex != null) 
            {
                double totalRequested = ex.Quantity + QuantityInput;
                if (totalRequested > it.StockQuantity)
                {
                    ShowSystemError($"⚠ Maximum Available Stock: {it.StockQuantity}");
                    StatusMessage = $"⚠ Maximum Available Stock: {it.StockQuantity}";
                    OnPropertyChanged(nameof(StatusMessage));
                }
                else
                {
                    ex.Quantity = totalRequested;
                    if (totalRequested == it.StockQuantity)
                    {
                        StatusMessage = $"⚠ Maximum Available Stock: {it.StockQuantity}";
                        OnPropertyChanged(nameof(StatusMessage));
                    }
                    else if (StatusMessage.Contains("Available Stock"))
                    {
                        StatusMessage = "";
                        OnPropertyChanged(nameof(StatusMessage));
                    }
                }
            }
            else 
            {
                int quantityToAdd = QuantityInput;
                if (quantityToAdd >= it.StockQuantity)
                {
                    if (quantityToAdd > it.StockQuantity)
                    {
                        quantityToAdd = (int)it.StockQuantity;
                        ShowSystemError($"⚠ Maximum Available Stock: {it.StockQuantity}");
                        StatusMessage = $"⚠ Maximum Available Stock: {it.StockQuantity}";
                        OnPropertyChanged(nameof(StatusMessage));
                    }
                    else if (quantityToAdd == it.StockQuantity)
                    {
                        StatusMessage = $"⚠ Maximum Available Stock: {it.StockQuantity}";
                        OnPropertyChanged(nameof(StatusMessage));
                    }
                    else 
                    {
                        if (StatusMessage.Contains("Available Stock"))
                        {
                            StatusMessage = "";
                            OnPropertyChanged(nameof(StatusMessage));
                        }
                    }
                }
                else
                {
                    // Do nothing here so we don't accidentally overwrite a success message unnecessarily, or explicitly clear it
                    if (StatusMessage.Contains("Available Stock"))
                    {
                        StatusMessage = "";
                        OnPropertyChanged(nameof(StatusMessage));
                    }
                }
                
                if (quantityToAdd > 0)
                {
                    SelectedTab.CartItems.Add(new CartItem 
                    { 
                        ItemId = it.ItemId, 
                        Barcode = it.Barcode,
                        ItemDescription = it.Description, 
                        UnitPrice = it.SalePrice, 
                        Quantity = quantityToAdd,
                        AvailableStock = it.StockQuantity
                    });
                }
                else if (it.StockQuantity <= 0)
                {
                    StatusMessage = $"⚠ Maximum Available Stock: {it.StockQuantity}";
                    ShowSystemError($"⚠ Maximum Available Stock: {it.StockQuantity}");
                    OnPropertyChanged(nameof(StatusMessage));
                }
            }
            QuantityInput = 1; 
            SelectedCartItem = null;
            OnPropertyChanged(nameof(SelectedCartItem));
            RecalculateTotal(); 
            RefocusBarcode();
        }
        private void RemoveFromCart() { if (SelectedCartItem != null && SelectedTab != null) { SelectedTab.CartItems.Remove(SelectedCartItem); RecalculateTotal(); } }
        private void IncreaseQuantity() 
        { 
            if (SelectedCartItem != null) 
            { 
                if (SelectedCartItem.Quantity + 1 >= SelectedCartItem.AvailableStock)
                {
                    if (SelectedCartItem.Quantity + 1 > SelectedCartItem.AvailableStock)
                    {
                        ShowSystemError($"⚠ Maximum Available Stock: {SelectedCartItem.AvailableStock}");
                        StatusMessage = $"⚠ Maximum Available Stock: {SelectedCartItem.AvailableStock}";
                        OnPropertyChanged(nameof(StatusMessage));
                        return;
                    }
                    StatusMessage = $"⚠ Maximum Available Stock: {SelectedCartItem.AvailableStock}";
                    OnPropertyChanged(nameof(StatusMessage));
                }
                else
                {
                    if (StatusMessage.Contains("Available Stock"))
                    {
                        StatusMessage = "";
                        OnPropertyChanged(nameof(StatusMessage));
                    }
                }
                SelectedCartItem.Quantity++; 
                RecalculateTotal(); 
                RefocusBarcode(); 
            } 
        }
        private void DecreaseQuantity() 
        { 
            if (SelectedCartItem != null && SelectedCartItem.Quantity > 1) 
            { 
                SelectedCartItem.Quantity--; 
                if (SelectedCartItem.Quantity < SelectedCartItem.AvailableStock)
                {
                    StatusMessage = "";
                    OnPropertyChanged(nameof(StatusMessage));
                }
                RecalculateTotal(); 
                RefocusBarcode(); 
            } 
        }
        private void RecalculateTotal() { if (SelectedTab == null) return; SubTotal = SelectedTab.CartItems.Sum(i => i.TotalPrice); double.TryParse(DiscountText, out var d); double.TryParse(TaxText, out var t); if (d > SubTotal) { d = SubTotal; DiscountText = d.ToString("F0"); } DiscountAmount = d; TaxAmount = t; GrandTotal = Math.Max(0, SubTotal - DiscountAmount + TaxAmount); if (!IsCashPayment && IsWalkIn) { CashReceivedText = GrandTotal.ToString("F2"); } else if (!IsCashPayment && HasSelectedCustomer) { double.TryParse(CashReceivedText, out var curAmt); if (curAmt > GrandTotal || string.IsNullOrWhiteSpace(CashReceivedText)) { CashReceivedText = GrandTotal.ToString("F2"); } } CalculateChange(); OnPropertyChanged(nameof(SubTotal)); OnPropertyChanged(nameof(DiscountAmount)); OnPropertyChanged(nameof(TaxAmount)); OnPropertyChanged(nameof(GrandTotal)); OnPropertyChanged(nameof(CartItems)); OnPropertyChanged(nameof(PreviewShowTax)); }
        private void CalculateChange() 
        { 
            if (!IsCashPayment)
            {
                if (HasSelectedCustomer)
                {
                    // Online + registered: allow partial, show due amount
                    double.TryParse(CashReceivedText, out var onlineAmt);
                    ChangeAmount = onlineAmt - GrandTotal;
                }
                else
                {
                    // Online + walk-in: exact payment, no change
                    ChangeAmount = 0;
                }
            }
            else
            {
                bool hasCash = double.TryParse(CashReceivedText, out var c);
                if (hasCash) 
                    ChangeAmount = c - GrandTotal; 
                else 
                    ChangeAmount = -GrandTotal;
            }
                
            OnPropertyChanged(nameof(ChangeAmount)); 
            OnPropertyChanged(nameof(ChangeAmountAbs)); 
            OnPropertyChanged(nameof(IsChangeNegative)); 
            OnPropertyChanged(nameof(IsChangeAmountVisible)); 
            OnPropertyChanged(nameof(ChangeDisplayLabel)); 
            OnPropertyChanged(nameof(ChangeDisplayBrush)); 
            OnPropertyChanged(nameof(PreviewCashReceived));
            OnPropertyChanged(nameof(PreviewChange));
            OnPropertyChanged(nameof(PreviewHasDue));
            OnPropertyChanged(nameof(PreviewPaidAmount));
            OnPropertyChanged(nameof(PreviewDueAmount));
            OnPropertyChanged(nameof(PreviewHasCashReceived));
        }
        private async void CompleteSale() 
        { 
            try 
            { 
                StatusMessage = string.Empty;
                OnPropertyChanged(nameof(StatusMessage));

                if (SelectedTab == null || !SelectedTab.CartItems.Any()) { StatusMessage = "✗ Cart is empty."; OnPropertyChanged(nameof(StatusMessage)); ShowPopupError("Cart is empty."); return; } 

                double.TryParse(DiscountText, out var d); 
                double.TryParse(TaxText, out var t);
                double sub     = SelectedTab.CartItems.Sum(i => i.TotalPrice);
                double grand   = Math.Round(sub - d + t, 2);

                double cashReceived;
                double paidAmount;

                if (!IsCashPayment)
                {
                    // ── Validate online account selection ──
                    if (SelectedAccount == null)
                    {
                        StatusMessage = "✗ Please select a payment account (e.g. Bank/Easypaisa).";
                        OnPropertyChanged(nameof(StatusMessage));
                        ShowPopupError("Please select a payment account.\nChoose a Bank, Easypaisa, or JazzCash account.");
                        return;
                    }

                    if (HasSelectedCustomer)
                    {
                        // Online + registered: allow partial payment, rest goes to credit
                        double.TryParse(CashReceivedText, out cashReceived);
                        if (cashReceived > grand + 0.01)
                        { StatusMessage = "✗ Amount cannot exceed grand total for online payment."; OnPropertyChanged(nameof(StatusMessage)); ShowPopupError("Amount cannot exceed grand total for online payment."); return; }
                        paidAmount = Math.Min(cashReceived, grand);
                    }
                    else
                    {
                        // Online + walk-in: exact payment, no change, no credit
                        cashReceived = grand;
                        paidAmount = grand;
                    }
                }
                else
                {
                    double.TryParse(CashReceivedText, out cashReceived);

                    // For registered customers, paidAmount is capped at grandTotal (the rest is credit/due)
                    // For walk-ins, it must be the full grandTotal
                    paidAmount = Math.Min(cashReceived, grand);

                    // For walk-in customers, enforce full payment
                    if (IsWalkIn)
                    {
                        if (cashReceived < grand - 0.01)
                        { StatusMessage = "✗ Insufficient cash."; OnPropertyChanged(nameof(StatusMessage)); ShowPopupError("Insufficient cash."); return; }
                        paidAmount = grand; 
                    }
                }

                var sb = _billService.CompleteBill(
                    _authService.CurrentUser?.Id, 
                    SelectedCustomer?.CustomerId, 
                    SelectedTab.CartItems.Select(c => new Models.BillDescription { ItemId = c.ItemId, Quantity = c.Quantity, UnitPrice = c.UnitPrice, ItemDescription = c.ItemDescription }).ToList(), 
                    d, 
                    t, 
                    cashReceived, 
                    paidAmount, 
                    SelectedBillingAddress, 
                    SelectedPaymentMethod, 
                    SelectedOnlineMethod,
                    SelectedAccount?.Id);

                // Ensure Customer object is attached for PrintService
                sb.Customer = SelectedCustomer;

                await AttemptPrint(sb);
                StatusMessage = $"✓ Sale Completed: Bill #{sb.InvoiceNumber} | {sb.PaymentStatus}";
                OnPropertyChanged(nameof(StatusMessage));
                if (Tabs.Count > 1) CloseTab(SelectedTab); else ClearCart(); 
                RefocusBarcode();
            } 
            catch (Exception ex) { StatusMessage = $"✗ Bill failed: {ex.Message}"; OnPropertyChanged(nameof(StatusMessage)); ShowPopupError($"Bill failed: {ex.Message}"); AppLogger.Error("Complete bill failed", ex); } 
        }
        private async Task AttemptPrint(Bill b) 
        { 
            bool isOnline = _printService.IsPrinterOnline();

            if (isOnline)
            {
                bool printSuccess = _printService.PrintReceipt(b, _authService.CurrentUser?.FullName ?? "Cashier");
                if (printSuccess)
                {
                    _billRepo.UpdatePrintStatus(b.BillId, true, DateTime.Now); 
                    return;
                }
            }

            // Printer is offline or print failed. Complete the sale normally without queuing.
            _billRepo.UpdatePrintStatus(b.BillId, false, null); 
        }
        private void ClearCart() 
        { 
            if (SelectedTab == null) return; 
            SelectedTab.CartItems.Clear(); 
            SelectedTab.DiscountText = "0"; 
            SelectedTab.TaxText = "0"; 
            SelectedTab.CashReceivedText = "0"; 
            PendingCreditAmount = 0; 
            SelectedPaymentMethod = "Cash";
            SelectedOnlineMethod = null;
            SelectedAccount = ActiveAccounts.FirstOrDefault();
            OnPropertyChanged(nameof(IsCashPayment));
            OnPropertyChanged(nameof(IsOnlinePayment));
            ClearCustomer(); 
            RecalculateTotal(); 
            
            // Ensure UI is notified of reset fields
            OnPropertyChanged(nameof(CashReceivedText));
            OnPropertyChanged(nameof(DiscountText));
            OnPropertyChanged(nameof(TaxText));
            
            InvoiceNumber = GetNextAvailableInvoiceNumber(); 
        }
        private void SearchCustomers() { if (SelectedTab == null) return; SelectedSearchResult = null; if (string.IsNullOrWhiteSpace(CustomerSearchQuery) || CustomerSearchQuery.Length < 1) { SelectedTab.CustomerSearchResults.Clear(); OnPropertyChanged(nameof(CustomerSearchResults)); return; } var results = _customerService.SearchCustomers(CustomerSearchQuery); SelectedTab.CustomerSearchResults.Clear(); foreach (var c in results) SelectedTab.CustomerSearchResults.Add(c); OnPropertyChanged(nameof(CustomerSearchResults)); }
        private void SelectCustomer(Customer? c)
        {
            var targetCustomer = c ?? SelectedSearchResult;
            if (targetCustomer == null || SelectedTab == null) return;
            
            SelectedCustomer = targetCustomer;
            SelectedTab.CustomerSearchQuery = "";
            SelectedTab.CustomerSearchResults.Clear();
            SelectedSearchResult = null;
            OnPropertyChanged(nameof(CustomerSearchQuery));
            OnPropertyChanged(nameof(CustomerSearchResults));
            LoadCustomerHistory(targetCustomer.CustomerId);

            // Load pending credit for warning badge
            PendingCreditAmount = _customerService.GetPendingCredit(targetCustomer.CustomerId);

            // Populate address selection
            AvailableAddresses.Clear();
            if (!string.IsNullOrWhiteSpace(targetCustomer.Address)) AvailableAddresses.Add(targetCustomer.Address);
            if (!string.IsNullOrWhiteSpace(targetCustomer.Address2)) AvailableAddresses.Add(targetCustomer.Address2);
            if (!string.IsNullOrWhiteSpace(targetCustomer.Address3)) AvailableAddresses.Add(targetCustomer.Address3);
            SelectedBillingAddress = AvailableAddresses.FirstOrDefault();
        }
        private void ClearCustomer() 
        { 
            if (SelectedTab == null) return;

            // Clear customer identity
            SelectedCustomer = null; 
            SelectedTab.CustomerBills.Clear(); 
            SelectedTab.CustomerSearchQuery = ""; 
            SelectedTab.CustomerSearchResults.Clear(); 
            SelectedTab.StatusMessage = "";
            SelectedTab.IsHistoryPaymentOpen = false;
            SelectedTab.IsBillDetailOpen = false;
            SelectedTab.PreviewHistoryBill = null;
            SelectedTab.LoadedHistoryBillId = null;
            PendingCreditAmount = 0; 
            AvailableAddresses.Clear();
            SelectedBillingAddress = null;
            IsAddingAddress = false;

            // Clear cart and reset billing totals
            foreach (var item in SelectedTab.CartItems)
                item.PropertyChanged -= OnCartItemPropertyChanged;
            SelectedTab.CartItems.Clear();
            SelectedTab.DiscountText = "0";
            SelectedTab.TaxText = "0";
            SelectedTab.CashReceivedText = "0";

            RecalculateTotal();

            // Notify all affected properties
            OnPropertyChanged(nameof(CartItems));
            OnPropertyChanged(nameof(DiscountText));
            OnPropertyChanged(nameof(TaxText));
            OnPropertyChanged(nameof(CashReceivedText));
            OnPropertyChanged(nameof(PreviewHistoryBill));
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(IsHistoryPaymentOpen));
            OnPropertyChanged(nameof(IsBillDetailOpen));
            OnPropertyChanged(nameof(CustomerSearchQuery)); 
            OnPropertyChanged(nameof(CustomerSearchResults)); 
            OnPropertyChanged(nameof(IsWalkIn)); 
            OnPropertyChanged(nameof(HasSelectedCustomer));
            OnPropertyChanged(nameof(PendingCreditAmount));
        }

        private void SaveAddress()
        {
            if (SelectedCustomer == null || string.IsNullOrWhiteSpace(NewAddressInput)) return;

            if (string.IsNullOrWhiteSpace(SelectedCustomer.Address)) SelectedCustomer.Address = NewAddressInput;
            else if (string.IsNullOrWhiteSpace(SelectedCustomer.Address2)) SelectedCustomer.Address2 = NewAddressInput;
            else if (string.IsNullOrWhiteSpace(SelectedCustomer.Address3)) SelectedCustomer.Address3 = NewAddressInput;

            try
            {
                _customerService.UpdateCustomer(SelectedCustomer);
                AvailableAddresses.Add(NewAddressInput);
                SelectedBillingAddress = NewAddressInput;
                IsAddingAddress = false;
                NewAddressInput = "";
                OnPropertyChanged(nameof(AvailableAddresses));
                OnPropertyChanged(nameof(SelectedBillingAddress));
            }
            catch (Exception ex)
            {
                ShowSystemError("Failed to save address: " + ex.Message);
            }
        }
        private void LoadCustomerHistory(int id) { if (SelectedTab == null) return; var bills = _billRepo.GetBillsByCustomerId(id); SelectedTab.CustomerBills.Clear(); foreach (var b in bills) SelectedTab.CustomerBills.Add(b); OnPropertyChanged(nameof(CustomerBills)); }
        private void LoadBillIntoCart(Bill b)
        {
            if (SelectedTab == null) return;
            if (SelectedTab.LoadedHistoryBillId == b.BillId)
            {
                StatusMessage = "\u2139 This bill is already loaded in the cart.";
                OnPropertyChanged(nameof(StatusMessage));
                return;
            }
            bool cartWasEmpty = SelectedTab.CartItems.Count == 0;
            bool wasCapped = false;

            foreach (var it in b.Items)
            {
                var currentItem = _itemService.GetItemById(it.ItemInternalId);
                double stock = currentItem?.StockQuantity ?? 0;
                double currentPrice = currentItem?.SalePrice ?? it.UnitPrice;
                double finalQty = it.Quantity;
                if (finalQty > stock) { finalQty = stock; wasCapped = true; }
                if (finalQty <= 0) continue;

                // Match strictly by ItemId (barcode) only — never by name or price
                var existing = SelectedTab.CartItems.FirstOrDefault(c => c.ItemId == it.ItemId);
                if (existing != null)
                {
                    existing.AvailableStock = stock;
                    existing.UnitPrice = currentPrice;
                    existing.Quantity += finalQty;
                    existing.IsCopied = true;
                    if (existing.Quantity > stock) { existing.Quantity = stock; wasCapped = true; }
                }
                else
                {
                    var newItem = new CartItem 
                    { 
                        ItemId = it.ItemId, 
                        Barcode = currentItem?.Barcode,
                        ItemDescription = currentItem?.Description ?? it.ItemDescription, 
                        UnitPrice = currentPrice, 
                        Quantity = finalQty,
                        AvailableStock = stock,
                        IsCopied = true
                    };
                    // PropertyChanged is handled by OnCartItemsCollectionChanged — no manual subscription
                    SelectedTab.CartItems.Add(newItem);
                }
            }

            if (wasCapped)
            {
                StatusMessage = "⚠ Some items were capped due to current stock levels.";
                OnPropertyChanged(nameof(StatusMessage));
            }

            if (cartWasEmpty)
            {
                SelectedTab.DiscountText = b.DiscountAmount.ToString();
                SelectedTab.TaxText = b.TaxAmount.ToString();
                OnPropertyChanged(nameof(DiscountText));
                OnPropertyChanged(nameof(TaxText));
            }
            SelectedTab.LoadedHistoryBillId = b.BillId;
            RecalculateTotal();
        }
        private void SaveNewCustomer() { try { if (string.IsNullOrWhiteSpace(NewCustomerName) || string.IsNullOrWhiteSpace(NewCustomerPhone)) { RegistrationErrorMessage = "Name and Phone are required."; OnPropertyChanged(nameof(RegistrationErrorMessage)); return; } 
            var customer = new Customer { 
                Name = NewCustomerName, 
                FullName = NewCustomerName, 
                PrimaryPhone = NewCustomerPhone, 
                SecondaryPhone = NewCustomerSecondaryPhone, 
                Address = NewCustomerAddress,
                Address2 = NewCustomerAddress2,
                Address3 = NewCustomerAddress3
            };
            _customerService.RegisterCustomer(customer);
            SelectCustomer(customer); 
            SelectCustomer(customer);
            IsRegistrationVisible = false; OnPropertyChanged(nameof(IsRegistrationVisible)); ClearRegistrationForm(); }
            catch (Exception ex) { RegistrationErrorMessage = ex.Message; OnPropertyChanged(nameof(RegistrationErrorMessage)); } }
        private void ClearRegistrationForm()
        {
            NewCustomerName = "";
            NewCustomerPhone = "";
            NewCustomerSecondaryPhone = "";
            NewCustomerAddress = "";
            NewCustomerAddress2 = "";
            NewCustomerAddress3 = "";
            RegistrationErrorMessage = "";
            OnPropertyChanged(nameof(NewCustomerName));
            OnPropertyChanged(nameof(NewCustomerPhone));
            OnPropertyChanged(nameof(NewCustomerSecondaryPhone));
            OnPropertyChanged(nameof(NewCustomerAddress));
            OnPropertyChanged(nameof(NewCustomerAddress2));
            OnPropertyChanged(nameof(NewCustomerAddress3));
            OnPropertyChanged(nameof(RegistrationErrorMessage));
        }

        private void NavigateSearchResults(string? direction)
        {
            if (CustomerSearchResults == null || CustomerSearchResults.Count == 0) return;

            int currentIndex = SelectedSearchResult != null ? CustomerSearchResults.IndexOf(SelectedSearchResult) : -1;
            int nextIndex = currentIndex;

            if (direction == "Down")
            {
                nextIndex = (currentIndex + 1) % CustomerSearchResults.Count;
            }
            else if (direction == "Up")
            {
                nextIndex = currentIndex <= 0 ? CustomerSearchResults.Count - 1 : currentIndex - 1;
            }

            if (nextIndex >= 0 && nextIndex < CustomerSearchResults.Count)
            {
                SelectedSearchResult = CustomerSearchResults[nextIndex];
            }
        }

        private void NavigateProductResults(string? direction)
        {
            if (FilteredItemList == null || FilteredItemList.Count == 0) return;

            int currentIndex = SelectedSearchItem != null ? FilteredItemList.IndexOf(SelectedSearchItem) : -1;
            int nextIndex = currentIndex;

            if (direction == "Down")
            {
                nextIndex = (currentIndex + 1) % FilteredItemList.Count;
            }
            else if (direction == "Up")
            {
                nextIndex = currentIndex <= 0 ? FilteredItemList.Count - 1 : currentIndex - 1;
            }

            if (nextIndex >= 0 && nextIndex < FilteredItemList.Count)
            {
                SelectedSearchItem = FilteredItemList[nextIndex];
            }
        }

        private void OnCartItemsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (CartItem item in e.OldItems) item.PropertyChanged -= OnCartItemPropertyChanged;
            }
            if (e.NewItems != null)
            {
                foreach (CartItem item in e.NewItems) item.PropertyChanged += OnCartItemPropertyChanged;
            }
            RecalculateTotal();
        }

        private void OnCartItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CartItem.Quantity))
            {
                if (sender is CartItem item)
                {
                    if (item.Quantity >= item.AvailableStock)
                    {
                        if (item.Quantity > item.AvailableStock)
                        {
                            item.Quantity = (int)item.AvailableStock;
                            ShowSystemError($"⚠ Maximum Available Stock: {item.AvailableStock}");
                            return; // Wait for the recursive property changed to hit this logic instead
                        }
                        StatusMessage = $"⚠ Maximum Available Stock: {item.AvailableStock}";
                    }
                    else
                    {
                        if (StatusMessage.Contains("Available Stock"))
                        {
                            StatusMessage = "";
                        }
                    }
                    OnPropertyChanged(nameof(StatusMessage));
                }
                RecalculateTotal();
            }
            else if (e.PropertyName == nameof(CartItem.UnitPrice))
            {
                RecalculateTotal();
            }
        }

        private void RecordHistoryPayment()
        {
            if (SelectedTab == null || SelectedTab.PreviewHistoryBill == null) return;
            try
            {
                HistoryPaymentError = "";
                if (!double.TryParse(HistoryPaymentAmount, out double amount) || amount <= 0)
                {
                    HistoryPaymentError = "Enter a valid amount.";
                    return;
                }

                // Validate: Online payment must have account selected
                if (IsHistoryOnlinePayment && SelectedHistoryAccount == null)
                {
                    HistoryPaymentError = "⚠ For online payment, please select an account to receive the payment.";
                    return;
                }

                var updatedBill = _creditService.RecordPayment(SelectedTab.PreviewHistoryBill.BillId, amount, HistoryPaymentNote, SelectedHistoryPaymentMethod);
                
                // Attach customer info for receipt printing
                updatedBill.Customer = SelectedCustomer;

                // Print payment receipt
                try
                {
                    _printService.PrintPaymentReceipt(updatedBill, amount, _authService.CurrentUser?.FullName ?? "Cashier");
                }
                catch (Exception pex)
                {
                    AppLogger.Error("Payment receipt print failed", pex);
                }

                // Update preview bill with new remaining
                SelectedTab.PreviewHistoryBill = updatedBill;
                OnPropertyChanged(nameof(PreviewHistoryBill));

                // Refresh customer total due
                if (SelectedCustomer != null)
                {
                    PendingCreditAmount = _customerService.GetPendingCredit(SelectedCustomer.CustomerId);
                    LoadCustomerHistory(SelectedCustomer.CustomerId);
                }

                IsHistoryPaymentOpen = false;
                StatusMessage = $"✓ Payment of Rs. {amount:N0} recorded for Bill #{updatedBill.InvoiceNumber}";
                MessageBox.Show(StatusMessage, "Payment Recorded", MessageBoxButton.OK, MessageBoxImage.Information);
                OnPropertyChanged(nameof(StatusMessage));
            }
            catch (Exception ex)
            {
                HistoryPaymentError = ex.Message;
                AppLogger.Error("RecordHistoryPayment failed", ex);
            }
        }

        private void NotifyTabPropertiesChanged() { OnPropertyChanged(nameof(CartItems)); OnPropertyChanged(nameof(DiscountText)); OnPropertyChanged(nameof(TaxText)); OnPropertyChanged(nameof(CashReceivedText)); OnPropertyChanged(nameof(InvoiceNumber)); OnPropertyChanged(nameof(SelectedCustomer)); OnPropertyChanged(nameof(HasSelectedCustomer)); OnPropertyChanged(nameof(IsWalkIn)); OnPropertyChanged(nameof(CustomerSearchQuery)); OnPropertyChanged(nameof(CustomerSearchResults)); OnPropertyChanged(nameof(SelectedSearchResult)); OnPropertyChanged(nameof(CustomerBills)); OnPropertyChanged(nameof(PreviewHistoryBill)); OnPropertyChanged(nameof(IsHistoryPaymentOpen)); OnPropertyChanged(nameof(HistoryPaymentAmount)); OnPropertyChanged(nameof(HistoryPaymentNote)); OnPropertyChanged(nameof(HistoryPaymentError)); OnPropertyChanged(nameof(PendingCreditAmount)); OnPropertyChanged(nameof(HasPendingCredit)); OnPropertyChanged(nameof(PendingCreditDisplay)); OnPropertyChanged(nameof(SelectedBillingAddress)); OnPropertyChanged(nameof(StatusMessage)); OnPropertyChanged(nameof(IsBillDetailOpen)); }

        private void LoadHistoryActiveAccounts()
        {
            try
            {
                var accounts = _accountService.GetActiveAccounts();
                HistoryActiveAccounts.Clear();
                foreach (var account in accounts)
                    HistoryActiveAccounts.Add(account);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load active accounts for history payment", ex);
            }
        }

        public override void Dispose() { _timer.Stop(); _stockService.StockChanged -= LoadDashboardStats; base.Dispose(); }
    }
}
