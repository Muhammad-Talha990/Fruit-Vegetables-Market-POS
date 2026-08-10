using System;
using System.Runtime.Versioning;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Threading.Tasks;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Models;
using FruitVegetableMarketPOS.Services;
using FruitVegetableMarketPOS.Data.Repositories;

namespace FruitVegetableMarketPOS.ViewModels
{
    [SupportedOSPlatform("windows")]
    public class BillingViewModel : BaseViewModel
    {
        private readonly AuthService _authService;
        private readonly ItemService _itemService;
        private readonly ItemTypeService _itemTypeService;
        private readonly CategoryService _categoryService;
        private readonly DailyItemSelectionService _dailySelection;
        private readonly BillService _billService;
        private readonly PrintService _printService;
        private readonly CustomerService _customerService;
        private readonly CreditService _creditService;
        private readonly BillRepository _billRepo;
        private readonly AccountService _accountService;
        private readonly System.Windows.Threading.DispatcherTimer _timer;
        private bool _isWarmedUp;
        private readonly List<PosProductCard> _allTodayProducts = new();
        private Item? _pendingScanItem;

        /// <summary>Standard quantity unit / +/- step for fruit-veg POS (5 KG).</summary>
        public const double QuantityStepKg = 5.0;

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

        public Customer? SelectedCustomer { get => SelectedTab?.Customer; set { if (SelectedTab != null) { SelectedTab.Customer = value; SelectedTab.CustomerId = value?.CustomerId; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedCustomer)); OnPropertyChanged(nameof(IsWalkIn)); OnPropertyChanged(nameof(IsWalkInCustomerSelected)); OnPropertyChanged(nameof(IsRegisteredCustomerSelected)); OnPropertyChanged(nameof(IsAmountEditable)); OnPropertyChanged(nameof(CustomerDisplayName)); OnPropertyChanged(nameof(HasPendingCredit)); OnPropertyChanged(nameof(IsCustomerInactive)); OnPropertyChanged(nameof(IsBillingLocked)); OnPropertyChanged(nameof(MenuOpacity)); OnPropertyChanged(nameof(InactiveCustomerBannerVisible)); CalculateChange(); } } }
        public bool HasSelectedCustomer => SelectedCustomer != null;
        public bool IsWalkIn => SelectedCustomer == null || SelectedCustomer.FullName == "Walk-in Customer";
        public bool IsWalkInCustomerSelected => SelectedCustomer != null && SelectedCustomer.FullName == "Walk-in Customer";
        public bool IsRegisteredCustomerSelected => SelectedCustomer != null && SelectedCustomer.FullName != "Walk-in Customer";

        /// <summary>Registered customer selected but marked inactive — billing must be locked.</summary>
        public bool IsCustomerInactive =>
            SelectedCustomer != null &&
            !string.Equals(SelectedCustomer.FullName, "Walk-in Customer", StringComparison.OrdinalIgnoreCase) &&
            !SelectedCustomer.IsActive;

        public bool IsBillingLocked => IsCustomerInactive;
        public double MenuOpacity => IsBillingLocked ? 0.32 : 1.0;
        public bool InactiveCustomerBannerVisible => IsBillingLocked;

        public const string InactiveCustomerMessageEn = "Customer is inactive at the moment";
        public const string InactiveCustomerMessageUr = "گاہک اس وقت غیر فعال ہے";
        public string InactiveCustomerMessageEnText => InactiveCustomerMessageEn;
        public string InactiveCustomerMessageUrText => InactiveCustomerMessageUr;

        // ── Store Credit ──

        public double PendingCreditAmount
        {
            get => SelectedTab?.PendingCreditAmount ?? 0;
            set { if (SelectedTab != null) { SelectedTab.PendingCreditAmount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPendingCredit)); OnPropertyChanged(nameof(PendingCreditDisplay)); } }
        }
        public bool HasPendingCredit => PendingCreditAmount > 0 && HasSelectedCustomer && !IsWalkIn;
        public string PendingCreditDisplay => $"⚠ This customer has Rs. {PendingCreditAmount:N0} pending.";


        public string CustomerSearchQuery
        {
            get => SelectedTab?.CustomerSearchQuery ?? string.Empty;
            set
            {
                if (SelectedTab == null || SelectedTab.CustomerSearchQuery == value) return;
                SelectedTab.CustomerSearchQuery = value;
                SearchCustomers();
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCustomerSearchResults));
                OnPropertyChanged(nameof(IsCustomerDropDownOpen));
            }
        }
        public ObservableCollection<Customer> CustomerSearchResults => SelectedTab?.CustomerSearchResults ?? new();
        public bool HasCustomerSearchResults => CustomerSearchResults.Count > 0;
        public bool IsCustomerDropDownOpen => HasCustomerSearchResults && !HasSelectedCustomer;
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

        // ── POS Product Grid ──
        public ObservableCollection<Category> PosCategories { get; } = new();
        public ObservableCollection<PosCategoryChip> CategoryFilters { get; } = new();
        public ObservableCollection<PosProductCard> TodayProducts { get; } = new();
        public ObservableCollection<ItemType> AvailableTypesForPicker { get; } = new();
        public ObservableCollection<TypeQtyRow> TypeQtyRows { get; } = new();
        public ObservableCollection<Item> AllMasterItems { get; } = new();
        public ObservableCollection<PreviousDayMenuItem> PreviousDayMenuItems { get; } = new();


        private bool _isNewDayPromptVisible;
        public bool IsNewDayPromptVisible
        {
            get => _isNewDayPromptVisible;
            set
            {
                if (SetProperty(ref _isNewDayPromptVisible, value))
                    OnPropertyChanged(nameof(IsNewDaySetupActive));
            }
        }

        private bool _isPreviousDayPickerVisible;
        public bool IsPreviousDayPickerVisible
        {
            get => _isPreviousDayPickerVisible;
            set
            {
                if (SetProperty(ref _isPreviousDayPickerVisible, value))
                    OnPropertyChanged(nameof(IsNewDaySetupActive));
            }
        }

        /// <summary>True while Continue/Refresh new-day flow is active over the product grid.</summary>
        public bool IsNewDaySetupActive => IsNewDayPromptVisible || IsPreviousDayPickerVisible;

        private string _previousMenuDateDisplay = string.Empty;
        public string PreviousMenuDateDisplay
        {
            get => _previousMenuDateDisplay;
            set => SetProperty(ref _previousMenuDateDisplay, value);
        }

        private Category? _selectedCategory;
        public Category? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    UpdateCategoryChipSelection();
                    ApplyProductFilters();
                }
            }
        }

        private string _productSearchQuery = string.Empty;
        public string ProductSearchQuery
        {
            get => _productSearchQuery;
            set
            {
                if (SetProperty(ref _productSearchQuery, value))
                    ApplyProductFilters();
            }
        }

        private PosProductCard? _selectedPosProduct;
        public PosProductCard? SelectedPosProduct
        {
            get => _selectedPosProduct;
            set => SetProperty(ref _selectedPosProduct, value);
        }

        private ItemType? _selectedType;
        public ItemType? SelectedType
        {
            get => _selectedType;
            set
            {
                if (SetProperty(ref _selectedType, value))
                {
                    SelectedTypePrice = value?.Price ?? 0;
                    OnPropertyChanged(nameof(SelectedTypeDisplay));
                }
            }
        }

        public string SelectedTypeDisplay => SelectedType != null
            ? $"{SelectedType.TypeName} — Rs. {SelectedType.Price:N0}"
            : string.Empty;

        private bool _isTypePickerOpen;
        public bool IsTypePickerOpen
        {
            get => _isTypePickerOpen;
            set => SetProperty(ref _isTypePickerOpen, value);
        }

        private bool _isQuantityPickerOpen;
        public bool IsQuantityPickerOpen
        {
            get => _isQuantityPickerOpen;
            set => SetProperty(ref _isQuantityPickerOpen, value);
        }

        private string _quantityText = "1.000";
        public string QuantityText
        {
            get => _quantityText;
            set => SetProperty(ref _quantityText, value);
        }

        private double _selectedTypePrice;
        public double SelectedTypePrice
        {
            get => _selectedTypePrice;
            set => SetProperty(ref _selectedTypePrice, value);
        }

        private bool _syncingDailySetup;

        private string _dailySetupItemIdText = string.Empty;
        public string DailySetupItemIdText
        {
            get => _dailySetupItemIdText;
            set
            {
                if (!SetProperty(ref _dailySetupItemIdText, value)) return;
                if (_syncingDailySetup) return;
                SyncDailySetupFromId(value);
            }
        }

        private Item? _dailySetupSelectedItem;
        public Item? DailySetupSelectedItem
        {
            get => _dailySetupSelectedItem;
            set
            {
                if (!SetProperty(ref _dailySetupSelectedItem, value)) return;
                if (_syncingDailySetup) return;
                SyncDailySetupFromItem(value);
            }
        }

        /// <summary>Dropdown choices: Type 1 / قسم 1 … Type 10 / قسم 10.</summary>
        public ObservableCollection<TypeCountOption> DailyTypeCountOptions { get; } = new(
            Enumerable.Range(1, 10).Select(n => new TypeCountOption { Count = n }));

        private TypeCountOption? _selectedDailyTypeCountOption;
        /// <summary>Selected type-count from the Type dropdown (null until chosen).</summary>
        public TypeCountOption? SelectedDailyTypeCountOption
        {
            get => _selectedDailyTypeCountOption;
            set
            {
                if (!SetProperty(ref _selectedDailyTypeCountOption, value)) return;
                _dailyTypeCountText = value?.Count.ToString() ?? string.Empty;
                OnPropertyChanged(nameof(DailyTypeCountText));
                // Keep prices already typed for Type 1…N when increasing/decreasing count
                if (!_syncingDailySetup)
                    RebuildDailyTypePriceRows(preserveTypedPrices: true);
            }
        }

        private string _dailyTypeCountText = string.Empty;
        /// <summary>How many types (qism) to create — 1 to 10 (kept in sync with dropdown).</summary>
        public string DailyTypeCountText
        {
            get => _dailyTypeCountText;
            set
            {
                if (!SetProperty(ref _dailyTypeCountText, value)) return;
                SyncTypeCountOptionFromText(value);
                RebuildDailyTypePriceRows();
            }
        }

        public ObservableCollection<DailyTypePriceRow> DailyTypePriceRows { get; } = new();

        public bool HasDailyTypePriceRows => DailyTypePriceRows.Count > 0;

        // ── Update menu item dialog (separate from Add form) ──
        private bool _isUpdateMenuItemOpen;
        public bool IsUpdateMenuItemOpen
        {
            get => _isUpdateMenuItemOpen;
            set => SetProperty(ref _isUpdateMenuItemOpen, value);
        }

        private Item? _updateMenuItem;
        public string UpdateMenuItemTitle => _updateMenuItem == null
            ? string.Empty
            : $"{_updateMenuItem.Description}  ·  #{_updateMenuItem.PosCode}";

        public string UpdateMenuItemUrdu => _updateMenuItem?.NameUrdu ?? string.Empty;

        public ObservableCollection<DailyTypePriceRow> UpdateTypePriceRows { get; } = new();

        private bool _syncingUpdateDialog;
        private TypeCountOption? _selectedUpdateTypeCountOption;
        public TypeCountOption? SelectedUpdateTypeCountOption
        {
            get => _selectedUpdateTypeCountOption;
            set
            {
                if (!SetProperty(ref _selectedUpdateTypeCountOption, value)) return;
                if (!_syncingUpdateDialog)
                    RebuildUpdateTypePriceRows(preserveTypedPrices: true);
            }
        }

        private void RebuildUpdateTypePriceRows(bool preserveTypedPrices)
        {
            var count = Math.Clamp(_selectedUpdateTypeCountOption?.Count ?? 1, 1, 10);
            var previous = preserveTypedPrices
                ? UpdateTypePriceRows.Select(r => r.PriceText ?? string.Empty).ToList()
                : new List<string>();

            UpdateTypePriceRows.Clear();
            for (int i = 1; i <= count; i++)
            {
                UpdateTypePriceRows.Add(new DailyTypePriceRow
                {
                    Index = i,
                    PriceText = i - 1 < previous.Count ? previous[i - 1] ?? string.Empty : string.Empty
                });
            }
            OnPropertyChanged(nameof(UpdateTypePriceRows));
        }

        private void SyncTypeCountOptionFromText(string? text)
        {
            TypeCountOption? match = null;
            if (int.TryParse((text ?? string.Empty).Trim(), out var n) && n >= 1 && n <= 10)
                match = DailyTypeCountOptions.FirstOrDefault(o => o.Count == n);

            if (!Equals(_selectedDailyTypeCountOption, match))
            {
                _selectedDailyTypeCountOption = match;
                OnPropertyChanged(nameof(SelectedDailyTypeCountOption));
            }
        }

        private void SetDailyTypeCount(int count, bool rebuild, bool preserveTypedPrices = false)
        {
            count = Math.Clamp(count, 1, 10);
            _dailyTypeCountText = count.ToString();
            _selectedDailyTypeCountOption = DailyTypeCountOptions.FirstOrDefault(o => o.Count == count);
            OnPropertyChanged(nameof(DailyTypeCountText));
            OnPropertyChanged(nameof(SelectedDailyTypeCountOption));
            if (rebuild)
                RebuildDailyTypePriceRows(preserveTypedPrices);
        }

        public bool IsAdmin => _authService.IsAdmin;
        public string BusinessDateDisplay => _dailySelection.CurrentBusinessDate;

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

        public bool IsAmountEditable => IsCashPayment || (!IsCashPayment && !IsWalkIn);

        // ── Preview-specific computed properties ──
        public double PreviewCashReceived { get { double.TryParse(CashReceivedText, out var v); return v; } }
        public double PreviewChange => Math.Max(0, ChangeAmount);
        public bool PreviewHasDue => !IsWalkIn && HasSelectedCustomer && PreviewCashReceived < GrandTotal - 0.01;
        public double PreviewPaidAmount => Math.Min(PreviewCashReceived, GrandTotal);
        public double PreviewDueAmount => Math.Max(0, GrandTotal - PreviewCashReceived);
        public bool PreviewShowTax => TaxAmount > 0;
        public bool PreviewHasCashReceived => PreviewCashReceived > 0 || IsCashPayment;

        public string CurrentDateTime => DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss");
        public DateTime CurrentTime => DateTime.Now;
        public string StatusMessage { get => SelectedTab?.StatusMessage ?? ""; set { if (SelectedTab != null) { SelectedTab.StatusMessage = value; OnPropertyChanged(); } } }
        public bool IsPreviewVisible { get; set; }

        private bool _isCartPreviewOpen;
        /// <summary>Full receipt preview overlay for the current cart (before Place Order).</summary>
        public bool IsCartPreviewOpen
        {
            get => _isCartPreviewOpen;
            set => SetProperty(ref _isCartPreviewOpen, value);
        }

        private Bill? _cartBillPreview;
        /// <summary>Draft bill mirroring the cart — shown in BillReceiptControl.</summary>
        public Bill? CartBillPreview
        {
            get => _cartBillPreview;
            private set => SetProperty(ref _cartBillPreview, value);
        }

        private bool _isCustomerHistoryOpen;
        public bool IsCustomerHistoryOpen
        {
            get => _isCustomerHistoryOpen;
            set => SetProperty(ref _isCustomerHistoryOpen, value);
        }
        public string StoreName => "PMC";
        public string StoreNameUrdu => "پاک مدینہ کمیشن ایجنٹس";
        public string StoreAddress => "I-11/4 Islamabad";
        public string StorePhone => "0345 5113044";
        public string CashierName => _authService.CurrentUser?.FullName ?? "Cashier";
        public string CustomerDisplayName => SelectedCustomer?.FullName ?? "Walk-in";

        private string _walkInPhoneInput = string.Empty;
        public string WalkInPhoneInput
        {
            get => _walkInPhoneInput;
            set
            {
                if (SetProperty(ref _walkInPhoneInput, value))
                {
                    OnPropertyChanged(nameof(IsWalkInPhoneValid));
                    (CompleteSaleCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    if (IsWalkInPhoneValid && _walkInPhoneInput.Length == 11)
                    {
                        TrySelectWalkInCustomer(_walkInPhoneInput);
                    }
                }
            }
        }

        private static bool IsValidPkPhone(string? phone) =>
            !string.IsNullOrWhiteSpace(phone) &&
            System.Text.RegularExpressions.Regex.IsMatch(phone.Trim(), "^0[0-9]{10}$");

        public bool IsWalkInPhoneValid => IsValidPkPhone(WalkInPhoneInput);

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
                    OnPropertyChanged(nameof(PreviewPaymentMethodText));
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
                    OnPropertyChanged(nameof(PreviewPaymentMethodText));
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
        
        public string PreviewPaymentMethodText
        {
            get
            {
                if (IsOnlinePayment)
                {
                    var accountName = SelectedAccount?.DisplayName ?? SelectedOnlineMethod;
                    if (!string.IsNullOrEmpty(accountName))
                    {
                        return $"Online ({accountName})";
                    }
                }
                return SelectedPaymentMethod;
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
        public ICommand SelectTabCommand { get; }
        public ICommand CloseTabCommand { get; }

        /// <summary>True when more than one bill tab is open (close × allowed).</summary>
        public bool CanCloseTabs => Tabs.Count > 1;
        public ICommand TogglePreviewCommand { get; }
        public ICommand OpenCartPreviewCommand { get; }
        public ICommand CloseCartPreviewCommand { get; }
        public ICommand SelectCustomerCommand { get; }
        public ICommand SelectWalkInCustomerCommand { get; }
        public ICommand ClearCustomerCommand { get; }
        public ICommand LoadPreviewToCartCommand { get; }
        public ICommand OpenCustomerHistoryCommand { get; }
        public ICommand CloseCustomerHistoryCommand { get; }
        public ICommand ViewCustomerHistoryBillCommand { get; }
        public ICommand PayCustomerHistoryBillCommand { get; }
        public ICommand LoadCustomerHistoryBillCommand { get; }
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

        public ICommand SelectCategoryCommand { get; }
        public ICommand SelectProductCommand { get; }
        public ICommand ToggleTodayAvailabilityCommand { get; }
        public ICommand UpdateTodayProductCommand { get; }
        public ICommand SaveUpdateMenuItemCommand { get; }
        public ICommand CloseUpdateMenuItemCommand { get; }
        public ICommand ConfirmTypeCommand { get; }
        public ICommand ConfirmQuantityAndAddCommand { get; }
        public ICommand IncrementQuantityCommand { get; }
        public ICommand DecrementQuantityCommand { get; }
        public ICommand IncrementTypeQtyCommand { get; }
        public ICommand DecrementTypeQtyCommand { get; }
        public ICommand AddDailyItemCommand { get; }
        public ICommand ClearDailySetupCommand { get; }
        public ICommand RefreshTodayProductsCommand { get; }
        public ICommand CloseTypePickerCommand { get; }
        public ICommand CloseQuantityPickerCommand { get; }
        public ICommand ShowContinuePreviousCommand { get; }
        public ICommand NewDayRefreshCommand { get; }
        public ICommand ConfirmPreviousSelectionCommand { get; }
        public ICommand CancelPreviousPickerCommand { get; }
        public ICommand SelectAllPreviousCommand { get; }
        public ICommand ClearPreviousSelectionCommand { get; }

        public BillingViewModel(
            AuthService authService,
            ItemService itemService,
            ItemTypeService itemTypeService,
            CategoryService categoryService,
            DailyItemSelectionService dailySelection,
            BillService billService,
            PrintService printService,
            CustomerService customerService,
            CreditService creditService,
            BillRepository billRepo,
            AccountService accountService)
        {
            _authService = authService;
            _itemService = itemService;
            _itemTypeService = itemTypeService;
            _categoryService = categoryService;
            _dailySelection = dailySelection;
            _billService = billService;
            _printService = printService;
            _customerService = customerService;
            _creditService = creditService;
            _billRepo = billRepo;
            _accountService = accountService;
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
            SelectTabCommand = new RelayCommand(obj =>
            {
                if (obj is BillingTab tab)
                    SelectedTab = tab;
            });
            CloseTabCommand = new RelayCommand(obj => CloseTab(obj as BillingTab, confirmIfDirty: true));
            TogglePreviewCommand = new RelayCommand(_ => OpenCartBillPreview());
            OpenCartPreviewCommand = new RelayCommand(_ => OpenCartBillPreview(), _ => CartItems.Count > 0);
            CloseCartPreviewCommand = new RelayCommand(_ => CloseCartBillPreview());
            SelectCustomerCommand = new RelayCommand(obj =>
            {
                var customer = obj as Customer ?? SelectedSearchResult ?? CustomerSearchResults.FirstOrDefault();
                SelectCustomer(customer);
            });
            SelectWalkInCustomerCommand = new RelayCommand(_ => TrySelectWalkInCustomer(_walkInPhoneInput));
            ClearCustomerCommand = new RelayCommand(_ => ClearCustomer());
            LoadPreviewToCartCommand= new RelayCommand(_ => { if (PreviewHistoryBill != null) LoadBillIntoCart(PreviewHistoryBill); });
            OpenCustomerHistoryCommand = new RelayCommand(_ => OpenCustomerHistory(), _ => IsRegisteredCustomerSelected);
            CloseCustomerHistoryCommand = new RelayCommand(_ => IsCustomerHistoryOpen = false);
            ViewCustomerHistoryBillCommand = new RelayCommand(obj => ViewCustomerHistoryBill(obj as Bill));
            PayCustomerHistoryBillCommand = new RelayCommand(obj => PayCustomerHistoryBill(obj as Bill));
            LoadCustomerHistoryBillCommand = new RelayCommand(obj =>
            {
                if (obj is not Bill bill) return;
                SetPreviewHistoryBill(bill);
                LoadBillIntoCart(bill);
                IsCustomerHistoryOpen = false;
            });
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

            SelectCategoryCommand = new RelayCommand(obj => SelectCategory(obj as PosCategoryChip));
            SelectProductCommand = new RelayCommand(SelectProduct);
            ToggleTodayAvailabilityCommand = new RelayCommand(ToggleTodayAvailability);
            UpdateTodayProductCommand = new RelayCommand(UpdateTodayProduct);
            SaveUpdateMenuItemCommand = new RelayCommand(_ => SaveUpdateMenuItem());
            CloseUpdateMenuItemCommand = new RelayCommand(_ => CloseUpdateMenuItem());
            ConfirmTypeCommand = new RelayCommand(_ => ConfirmTypeQuantitiesAndAdd());
            ConfirmQuantityAndAddCommand = new RelayCommand(_ => ConfirmQuantityAndAdd());
            IncrementQuantityCommand = new RelayCommand(_ => AdjustPickerQuantity(1));
            DecrementQuantityCommand = new RelayCommand(_ => AdjustPickerQuantity(-1));
            IncrementTypeQtyCommand = new RelayCommand(obj => AdjustTypeQty(obj as TypeQtyRow, 1));
            DecrementTypeQtyCommand = new RelayCommand(obj => AdjustTypeQty(obj as TypeQtyRow, -1));
            AddDailyItemCommand = new RelayCommand(_ => AddDailyItem());
            ClearDailySetupCommand = new RelayCommand(_ => ClearDailySetup());
            RefreshTodayProductsCommand = new RelayCommand(_ => RefreshTodayProducts());
            CloseTypePickerCommand = new RelayCommand(_ =>
            {
                IsTypePickerOpen = false;
                TypeQtyRows.Clear();
                _pendingScanItem = null;
            });
            CloseQuantityPickerCommand = new RelayCommand(_ => { IsQuantityPickerOpen = false; _pendingScanItem = null; });
            ShowContinuePreviousCommand = new RelayCommand(_ => ShowContinuePrevious());
            NewDayRefreshCommand = new RelayCommand(_ => NewDayRefresh());
            ConfirmPreviousSelectionCommand = new RelayCommand(_ => ConfirmPreviousSelection());
            CancelPreviousPickerCommand = new RelayCommand(_ => CancelPreviousPicker());
            SelectAllPreviousCommand = new RelayCommand(_ => SetAllPreviousSelection(true));
            ClearPreviousSelectionCommand = new RelayCommand(_ => SetAllPreviousSelection(false));

            // Heavy POS data loads in OnActivated / Warmup — keep ctor fast so Billing opens instantly.
            CatalogEvents.CatalogChanged += OnCatalogChanged;
        }

        /// <summary>
        /// Preloads billing data in the background after login so the first Billing click is instant.
        /// </summary>
        public void Warmup()
        {
            if (_isWarmedUp) return;
            LoadDashboardStats();
            LoadProducts();
            LoadActiveAccounts();
            LoadPosData();
            RebuildDailyTypePriceRows();
            _isWarmedUp = true;
        }

        /// <summary>Called whenever Billing is opened so prices/photos stay live.</summary>
        public void OnActivated()
        {
            if (!_isWarmedUp)
            {
                Warmup();
                return;
            }

            // Already warm — only refresh stats + today's menu (cheap compared to full warmup).
            LoadDashboardStats();
            RefreshTodayProducts();
            LoadActiveAccounts();
            CheckNewDaySetup();
            OnPropertyChanged(nameof(BusinessDateDisplay));
            OnPropertyChanged(nameof(IsAdmin));
        }

        private void OnCatalogChanged()
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                LoadDashboardStats();
                LoadAllMasterItemsForSetup();
                RefreshTodayProducts();
            });
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

        private void LoadPosData()
        {
            LoadPosCategories();
            LoadAllMasterItemsForSetup();
            CheckNewDaySetup();
            RefreshTodayProducts();
            OnPropertyChanged(nameof(BusinessDateDisplay));
            OnPropertyChanged(nameof(IsAdmin));
        }

        private void CheckNewDaySetup()
        {
            IsPreviousDayPickerVisible = false;
            PreviousDayMenuItems.Clear();
            PreviousMenuDateDisplay = string.Empty;

            // Quietly mark the day done when there is nothing to prompt for
            // (first day, or today's menu already has items).
            if (!_dailySelection.IsDaySetupDone() && !_dailySelection.NeedsNewDaySetup())
                _dailySelection.MarkDaySetupDone();

            IsNewDayPromptVisible = _dailySelection.NeedsNewDaySetup();
            if (IsNewDayPromptVisible)
                PreviousMenuDateDisplay = _dailySelection.GetPreviousMenuDate() ?? string.Empty;
        }

        private void ShowContinuePrevious()
        {
            PreviousDayMenuItems.Clear();
            var prevDate = _dailySelection.GetPreviousMenuDate();
            PreviousMenuDateDisplay = prevDate ?? string.Empty;

            foreach (var row in _dailySelection.GetPreviousDayMenuItems())
                PreviousDayMenuItems.Add(row);

            if (PreviousDayMenuItems.Count == 0)
            {
                ShowPopupError("No previous-day items found.");
                return;
            }

            IsNewDayPromptVisible = false;
            IsPreviousDayPickerVisible = true;
        }

        private void NewDayRefresh()
        {
            try
            {
                _dailySelection.RefreshStartFresh(_authService.CurrentUser?.Id);
                IsNewDayPromptVisible = false;
                IsPreviousDayPickerVisible = false;
                PreviousDayMenuItems.Clear();
                ClearDailySetupForm();
                RefreshTodayProducts();
                ShowPopupSuccess("Today's list cleared. Add items manually.");
            }
            catch (Exception ex)
            {
                ShowPopupError($"Refresh failed: {ex.Message}");
                AppLogger.Error("NewDayRefresh failed", ex);
            }
        }

        private void ConfirmPreviousSelection()
        {
            var selectedIds = PreviousDayMenuItems
                .Where(i => i.IsSelected)
                .Select(i => i.ItemId)
                .ToList();

            if (selectedIds.Count == 0)
            {
                ShowPopupError("Select at least one item, or use Refresh to start empty.");
                return;
            }

            try
            {
                var added = _dailySelection.ContinueWithSelected(selectedIds, _authService.CurrentUser?.Id);
                IsPreviousDayPickerVisible = false;
                IsNewDayPromptVisible = false;
                PreviousDayMenuItems.Clear();
                RefreshTodayProducts();
                ShowPopupSuccess($"✓ {added} item(s) added to today's menu.");
            }
            catch (Exception ex)
            {
                ShowPopupError($"Continue failed: {ex.Message}");
                AppLogger.Error("ConfirmPreviousSelection failed", ex);
            }
        }

        private void CancelPreviousPicker()
        {
            IsPreviousDayPickerVisible = false;
            PreviousDayMenuItems.Clear();
            IsNewDayPromptVisible = _dailySelection.NeedsNewDaySetup();
        }

        private void SetAllPreviousSelection(bool selected)
        {
            foreach (var item in PreviousDayMenuItems)
                item.IsSelected = selected;
        }

        private void LoadPosCategories()
        {
            PosCategories.Clear();
            CategoryFilters.Clear();

            CategoryFilters.Add(new PosCategoryChip { Label = "All (تمام)", Category = null, IsSelected = true });

            foreach (var cat in _categoryService.GetAllActive()
                         .Where(c => c.Name is "Fruits" or "Vegetables")
                         .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name))
            {
                PosCategories.Add(cat);
                CategoryFilters.Add(new PosCategoryChip { Label = cat.ChipLabel, Category = cat, IsSelected = false });
            }
        }

        private void LoadAllMasterItemsForSetup()
        {
            AllMasterItems.Clear();
            foreach (var item in _itemService.GetActiveItems()
                         .OrderBy(i => int.TryParse(i.PosCode, out var n) ? n : int.MaxValue)
                         .ThenBy(i => i.Description))
                AllMasterItems.Add(item);
        }

        /// <summary>When user types item ID (1,2,3…), fill the name dropdown — clear if ID not found.</summary>
        private void SyncDailySetupFromId(string? code)
        {
            Item? matched = null;
            _syncingDailySetup = true;
            try
            {
                code = (code ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(code))
                {
                    // Match by POS code (barcode) first — e.g. "5" → Grapes — not raw DB ItemId.
                    var item = _itemService.GetItemByBarcode(code);
                    if (item != null && item.IsActive)
                        matched = AllMasterItems.FirstOrDefault(i => i.Id == item.Id) ?? item;
                }

                // Always sync dropdown: show match, or clear when ID is empty/invalid
                _dailySetupSelectedItem = matched;
                OnPropertyChanged(nameof(DailySetupSelectedItem));
            }
            finally
            {
                _syncingDailySetup = false;
            }

            PrefillDailyTypeRowsFromItem(matched);
        }

        /// <summary>When user picks a name, fill the item ID box.</summary>
        private void SyncDailySetupFromItem(Item? item)
        {
            _syncingDailySetup = true;
            try
            {
                _dailySetupItemIdText = item == null ? string.Empty : item.PosCode;
                OnPropertyChanged(nameof(DailySetupItemIdText));
            }
            finally
            {
                _syncingDailySetup = false;
            }

            PrefillDailyTypeRowsFromItem(item);
        }

        private void PrefillDailyTypeRowsFromItem(Item? item)
        {
            if (item == null)
            {
                DailyTypePriceRows.Clear();
                OnPropertyChanged(nameof(HasDailyTypePriceRows));
                return;
            }

            // Every item needs at least Type 1 — select it by default when picking an item.
            // Prices stay empty for fresh entry (Update button loads existing types separately).
            SetDailyTypeCount(1, rebuild: true);
        }

        private void RebuildDailyTypePriceRows(bool preserveTypedPrices = true)
        {
            var previousPrices = preserveTypedPrices
                ? DailyTypePriceRows.Select(r => r.PriceText).ToList()
                : new System.Collections.Generic.List<string>();
            DailyTypePriceRows.Clear();

            if (!int.TryParse((DailyTypeCountText ?? string.Empty).Trim(), out var count) || count < 1)
                count = 0;
            if (count > 10)
            {
                count = 10;
                if (_dailyTypeCountText != "10")
                    SetDailyTypeCount(10, rebuild: false);
            }

            for (int i = 1; i <= count; i++)
            {
                DailyTypePriceRows.Add(new DailyTypePriceRow
                {
                    Index = i,
                    PriceText = i - 1 < previousPrices.Count ? previousPrices[i - 1] : string.Empty
                });
            }

            OnPropertyChanged(nameof(HasDailyTypePriceRows));
        }

        private void RefreshTodayProducts()
        {
            PosProductCard.InvalidatePhotoCache();
            _allTodayProducts.Clear();
            foreach (var sel in _dailySelection.GetVisibleForToday())
            {
                var item = _itemService.GetItemWithTypes(sel.ItemId);
                if (item == null || !item.IsActive) continue;

                var types = item.Types.OrderBy(t => t.SortOrder).ToList();
                var defaultType = types.FirstOrDefault();
                var typeNames = string.Join(" ", types.Select(t => t.TypeName));

                _allTodayProducts.Add(new PosProductCard
                {
                    Selection = sel,
                    ItemId = item.Id,
                    Name = item.Description,
                    NameUrdu = item.NameUrdu,
                    Unit = "piece",
                    CategoryId = item.CategoryId,
                    DisplayPrice = defaultType?.Price ?? 0,
                    Barcode = item.Barcode,
                    IsAvailable = sel.IsAvailable,
                    SearchText = $"{item.Description} {item.NameUrdu} {item.Barcode} {item.Id} {typeNames}".ToLowerInvariant()
                });
            }

            ApplyProductFilters();
        }

        private void ApplyProductFilters()
        {
            var query = (ProductSearchQuery ?? string.Empty).Trim().ToLowerInvariant();
            IEnumerable<PosProductCard> filtered = _allTodayProducts;

            if (SelectedCategory != null)
                filtered = filtered.Where(p => p.CategoryId == SelectedCategory.CategoryId);

            if (!string.IsNullOrWhiteSpace(query))
                filtered = filtered.Where(p => p.SearchText.Contains(query));

            TodayProducts.Clear();
            foreach (var card in filtered)
                TodayProducts.Add(card);
        }

        private void UpdateCategoryChipSelection()
        {
            foreach (var chip in CategoryFilters)
            {
                chip.IsSelected = chip.Category == null
                    ? SelectedCategory == null
                    : SelectedCategory?.CategoryId == chip.Category.CategoryId;
            }
        }

        private void SelectCategory(PosCategoryChip? chip)
        {
            if (chip == null) return;
            SelectedCategory = chip.Category;
        }

        private void SelectProduct(object? param)
        {
            if (param is not PosProductCard card) return;

            if (IsBillingLocked)
            {
                ShowPopupError($"{InactiveCustomerMessageEn}\n{InactiveCustomerMessageUr}");
                return;
            }

            if (!card.IsAvailable)
            {
                ShowPopupError($"'{card.Name}' is deactivated for today. Tap ✓ to activate again.");
                return;
            }

            SelectedPosProduct = card;
            _pendingScanItem = null;

            var types = _itemTypeService.GetActiveByItemId(card.ItemId).OrderBy(t => t.SortOrder).ToList();
            if (!types.Any())
            {
                MessageBox.Show("Item has no active types.", "POS", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AvailableTypesForPicker.Clear();
            TypeQtyRows.Clear();
            foreach (var t in types)
            {
                AvailableTypesForPicker.Add(t);
                TypeQtyRows.Add(new TypeQtyRow { Type = t, Quantity = 0 });
            }

            SelectedType = types[0];
            IsQuantityPickerOpen = false;
            IsTypePickerOpen = true;
        }

        private static void AdjustTypeQty(TypeQtyRow? row, int step)
        {
            if (row == null) return;
            row.Quantity = Math.Max(0, row.Quantity + step);
        }

        private void ConfirmTypeQuantitiesAndAdd()
        {
            if (IsBillingLocked)
            {
                IsTypePickerOpen = false;
                ShowPopupError($"{InactiveCustomerMessageEn}\n{InactiveCustomerMessageUr}");
                return;
            }

            Item? item = _pendingScanItem;
            if (item == null && SelectedPosProduct != null)
                item = _itemService.GetItemById(SelectedPosProduct.ItemId);

            if (item == null)
            {
                ShowPopupError("Product not found.");
                return;
            }

            // At least one type must have qty ≥ 1; multiple types allowed.
            var lines = TypeQtyRows.Where(r => r.Quantity >= 1).ToList();
            if (lines.Count == 0)
            {
                ShowPopupError("Select at least one type with quantity 1 or more.");
                return;
            }

            foreach (var row in lines)
                AddToCart(item, row.Type, row.Quantity);

            IsTypePickerOpen = false;
            IsQuantityPickerOpen = false;
            TypeQtyRows.Clear();
            _pendingScanItem = null;
            SelectedPosProduct = null;
            ShowPopupSuccess(lines.Count == 1
                ? "✓ Added to cart."
                : $"✓ Added {lines.Count} types to cart.");
        }

        private void OpenQuantityPicker()
        {
            QuantityText = "0";
            IsQuantityPickerOpen = true;
        }

        private void AdjustPickerQuantity(double step)
        {
            if (!double.TryParse(QuantityText, out var qty) || qty < 0)
                qty = 0;

            qty = Math.Round(qty + step, 3);
            if (qty < 0) qty = 0;
            QuantityText = qty.ToString("0.###");
        }

        private void ConfirmQuantityAndAdd()
        {
            if (SelectedType == null)
            {
                ShowPopupError("No type selected.");
                return;
            }

            if (!double.TryParse(QuantityText, out var qty) || qty <= 0)
            {
                ShowPopupError("Enter quantity (1, 2, 3…).");
                return;
            }

            Item? item = _pendingScanItem;
            if (item == null && SelectedPosProduct != null)
                item = _itemService.GetItemById(SelectedPosProduct.ItemId);

            if (item == null)
            {
                ShowPopupError("Product not found.");
                return;
            }

            AddToCart(item, SelectedType, qty);
            IsQuantityPickerOpen = false;
            IsTypePickerOpen = false;
            _pendingScanItem = null;
            SelectedPosProduct = null;
        }

        private void ToggleTodayAvailability(object? param)
        {
            if (param is not PosProductCard card) return;

            var makeAvailable = !card.IsAvailable;
            var confirm = makeAvailable
                ? MessageBox.Show(
                    $"Activate '{card.Name}' for today?\n\nآج دوبارہ فعال کریں؟",
                    "Activate item",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question)
                : MessageBox.Show(
                    $"Deactivate '{card.Name}' for today?\n\nItem stays on the list but cannot be sold until activated again.\n\nآج غیر فعال کریں؟",
                    "Deactivate item",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                _dailySelection.SetAvailable(card.DailySelectionId, makeAvailable);
                card.IsAvailable = makeAvailable;
                ShowPopupSuccess(makeAvailable
                    ? $"✓ '{card.Name}' activated for today."
                    : $"'{card.Name}' deactivated for today.");
            }
            catch (Exception ex)
            {
                ShowPopupError($"Failed to update item status: {ex.Message}");
                AppLogger.Error("ToggleTodayAvailability failed", ex);
            }
        }

        /// <summary>Open Update dialog for an item already on today's menu.</summary>
        private void UpdateTodayProduct(object? param)
        {
            if (param is not PosProductCard card) return;

            var item = AllMasterItems.FirstOrDefault(i => i.Id == card.ItemId)
                       ?? _itemService.GetItemById(card.ItemId);
            if (item == null)
            {
                ShowPopupError("Item not found.\nآئٹم نہیں ملا۔");
                return;
            }

            var types = _itemTypeService.GetActiveByItemId(item.Id).OrderBy(t => t.SortOrder).ToList();
            var typeCount = Math.Clamp(types.Count > 0 ? types.Count : 1, 1, 10);

            _updateMenuItem = item;
            _syncingUpdateDialog = true;
            try
            {
                _selectedUpdateTypeCountOption = DailyTypeCountOptions.FirstOrDefault(o => o.Count == typeCount);
                OnPropertyChanged(nameof(SelectedUpdateTypeCountOption));
            }
            finally
            {
                _syncingUpdateDialog = false;
            }

            UpdateTypePriceRows.Clear();
            for (int i = 1; i <= typeCount; i++)
            {
                var price = i - 1 < types.Count ? types[i - 1].Price : 0;
                UpdateTypePriceRows.Add(new DailyTypePriceRow
                {
                    Index = i,
                    PriceText = price > 0 ? price.ToString("0.##") : string.Empty
                });
            }

            OnPropertyChanged(nameof(UpdateMenuItemTitle));
            OnPropertyChanged(nameof(UpdateMenuItemUrdu));
            IsUpdateMenuItemOpen = true;
        }

        private void CloseUpdateMenuItem()
        {
            IsUpdateMenuItemOpen = false;
            _updateMenuItem = null;
            UpdateTypePriceRows.Clear();
            _selectedUpdateTypeCountOption = null;
            OnPropertyChanged(nameof(SelectedUpdateTypeCountOption));
            OnPropertyChanged(nameof(UpdateMenuItemTitle));
            OnPropertyChanged(nameof(UpdateMenuItemUrdu));
        }

        private void SaveUpdateMenuItem()
        {
            if (_updateMenuItem == null)
            {
                ShowPopupError("No item selected to update.\nکوئی آئٹم منتخب نہیں۔");
                return;
            }

            var typeCount = Math.Clamp(_selectedUpdateTypeCountOption?.Count ?? UpdateTypePriceRows.Count, 1, 10);
            if (UpdateTypePriceRows.Count != typeCount)
                RebuildUpdateTypePriceRows(preserveTypedPrices: true);

            if (UpdateTypePriceRows.Count == 0)
            {
                ShowPopupError("Add at least one type price.\nکم از کم ایک قسم کی قیمت درج کریں۔");
                return;
            }

            var prices = new List<double>();
            for (int i = 0; i < UpdateTypePriceRows.Count; i++)
            {
                var raw = UpdateTypePriceRows[i].PriceText?.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    ShowPopupError($"Enter a price for Type {i + 1} / قسم {i + 1}.");
                    return;
                }
                if (!double.TryParse(raw, out var price) || price < 0)
                {
                    ShowPopupError($"Invalid price for Type {i + 1} / قسم {i + 1}.");
                    return;
                }
                prices.Add(price);
            }

            if (prices.Any(p => p == 0))
            {
                var zeroOk = MessageBox.Show(
                    "One or more type prices are Rs.0.\nSave anyway?\n\nایک یا زیادہ قیمتیں صفر ہیں۔ کیا محفوظ کریں؟",
                    "Zero Price",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (zeroOk != MessageBoxResult.Yes) return;
            }

            try
            {
                var name = _updateMenuItem.Description;
                _itemTypeService.ReplaceWithNumberedTypes(_updateMenuItem.Id, prices);
                CloseUpdateMenuItem();
                RefreshTodayProducts();
                LoadAllMasterItemsForSetup();
                CatalogEvents.NotifyChanged();
                ShowPopupSuccess($"✓ '{name}' updated ({prices.Count} type(s)).\n✓ قیمت / اقسام اپ ڈیٹ ہو گئیں۔");
            }
            catch (Exception ex)
            {
                ShowPopupError(ex.Message);
                AppLogger.Error("SaveUpdateMenuItem failed", ex);
            }
        }

        private void AddDailyItem()
        {
            Item? item = DailySetupSelectedItem;
            var code = DailySetupItemIdText?.Trim();
            if (item == null && !string.IsNullOrWhiteSpace(code))
            {
                item = _itemService.GetItemByBarcode(code);
                if (item == null && int.TryParse(code, out var id))
                    item = _itemService.GetItemById(id);
            }

            if (item == null || !item.IsActive)
            {
                ShowPopupError("Select an item or enter a valid item ID (e.g. 1, 2, 3…).\nآئٹم منتخب کریں یا درست آئی ڈی درج کریں۔");
                return;
            }

            // Keep dropdown + ID in sync for clarity
            if (DailySetupSelectedItem == null || DailySetupSelectedItem.Id != item.Id)
            {
                _syncingDailySetup = true;
                try
                {
                    _dailySetupSelectedItem = AllMasterItems.FirstOrDefault(i => i.Id == item.Id) ?? item;
                    _dailySetupItemIdText = item.PosCode;
                    OnPropertyChanged(nameof(DailySetupSelectedItem));
                    OnPropertyChanged(nameof(DailySetupItemIdText));
                }
                finally { _syncingDailySetup = false; }
            }

            // Already on today's menu → block add (update only via Update dialog)
            if (_dailySelection.IsOnTodayMenu(item.Id))
            {
                ShowPopupError(
                    $"'{item.DisplayName}' (#{item.PosCode}) is already on today's menu.\n" +
                    "Click Update on the item card to change price or types.\n\n" +
                    $"'{item.DisplayName}' پہلے سے آج کی فہرست میں موجود ہے۔\n" +
                    "قیمت یا قسم بدلنے کے لیے کارڈ پر Update دبائیں۔");
                return;
            }

            if (!int.TryParse((DailyTypeCountText ?? string.Empty).Trim(), out var typeCount) || typeCount < 1 || typeCount > 10)
            {
                SetDailyTypeCount(1, rebuild: true);
                ShowPopupError("Select at least Type 1 (minimum 1 type / قسم, maximum 10).");
                return;
            }

            if (DailyTypePriceRows.Count != typeCount)
                RebuildDailyTypePriceRows(preserveTypedPrices: true);

            if (DailyTypePriceRows.Count == 0)
            {
                ShowPopupError("Add at least one type price before saving.");
                return;
            }

            var prices = new List<double>();
            for (int i = 0; i < DailyTypePriceRows.Count; i++)
            {
                var raw = DailyTypePriceRows[i].PriceText?.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    ShowPopupError($"Enter a price for Type {i + 1} / قسم {i + 1}.");
                    return;
                }
                if (!double.TryParse(raw, out var price) || price < 0)
                {
                    ShowPopupError($"Invalid price for Type {i + 1} / قسم {i + 1}.");
                    return;
                }
                prices.Add(price);
            }

            if (prices.Any(p => p == 0))
            {
                var zeroOk = MessageBox.Show(
                    "One or more type prices are Rs.0.\nSave anyway?",
                    "Zero Price",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (zeroOk != MessageBoxResult.Yes) return;
            }

            try
            {
                _itemTypeService.ReplaceWithNumberedTypes(item.Id, prices);
                _dailySelection.AddItem(item.Id, _authService.CurrentUser?.Id);

                var name = item.Description;
                ClearDailySetupForm();
                RefreshTodayProducts();
                LoadAllMasterItemsForSetup();
                CatalogEvents.NotifyChanged();

                ShowPopupSuccess($"✓ '{name}' added to today with {typeCount} type(s).\n✓ آج کی فہرست میں شامل کر دیا گیا۔");
            }
            catch (Exception ex)
            {
                ShowPopupError(ex.Message);
                AppLogger.Error("AddDailyItem failed", ex);
            }
        }

        /// <summary>Clears Add Today form (ID, item, type count, prices) and refreshes today's list.</summary>
        private void ClearDailySetup()
        {
            ClearDailySetupForm();
            RefreshTodayProducts();
            LoadAllMasterItemsForSetup();
        }

        private void ClearDailySetupForm()
        {
            _syncingDailySetup = true;
            try
            {
                _dailySetupItemIdText = string.Empty;
                _dailySetupSelectedItem = null;
                _dailyTypeCountText = string.Empty;
                _selectedDailyTypeCountOption = null;
                OnPropertyChanged(nameof(DailySetupItemIdText));
                OnPropertyChanged(nameof(DailySetupSelectedItem));
                OnPropertyChanged(nameof(DailyTypeCountText));
                OnPropertyChanged(nameof(SelectedDailyTypeCountOption));
            }
            finally
            {
                _syncingDailySetup = false;
            }

            DailyTypePriceRows.Clear();
            OnPropertyChanged(nameof(HasDailyTypePriceRows));
        }
        private string _onlinePaymentBreakdownTooltip = "No online payments today";
        public string OnlinePaymentBreakdownTooltip { get => _onlinePaymentBreakdownTooltip; set => SetProperty(ref _onlinePaymentBreakdownTooltip, value); }

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

            // Populate breakdown tooltip
            var from = DateTime.Today;
            var to = from.AddDays(1);
            var breakdown = _billService.GetOnlinePaymentBreakdown(from, to);
            if (breakdown.Any())
            {
                OnlinePaymentBreakdownTooltip = "Breakdown:\n" + string.Join("\n", breakdown.Select(kv => $"• {kv.Key}: Rs.{kv.Value:N0}"));
            }
            else
            {
                OnlinePaymentBreakdownTooltip = "No online payments today";
            }
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
        private void RefreshInvoiceNumbers()
        {
            try
            {
                // Next free BillId from DB, then assign sequential provisional numbers to open tabs
                string baseNumStr = _billService.GetNextInvoiceNumber();
                if (!int.TryParse(baseNumStr, out int nextId)) nextId = 1;

                for (int i = 0; i < Tabs.Count; i++)
                {
                    Tabs[i].InvoiceNumber = (nextId + i).ToString("D5");
                    Tabs[i].TabName = $"Bill {i + 1}";
                }

                OnPropertyChanged(nameof(InvoiceNumber));
                OnPropertyChanged(nameof(CanCloseTabs));
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to refresh invoice numbers", ex);
            }
        }

        private void AddNewTab()
        {
            const int maxTabs = 12;
            if (Tabs.Count >= maxTabs)
            {
                ShowPopupError($"Maximum {maxTabs} open bills. Complete or close a tab first.");
                return;
            }

            var tab = new BillingTab();
            Tabs.Add(tab);
            RefreshInvoiceNumbers();
            SelectedTab = tab;
            OnPropertyChanged(nameof(CanCloseTabs));
            RefocusBarcode();
        }

        /// <param name="confirmIfDirty">Ask before closing a tab that still has cart items.</param>
        private void CloseTab(BillingTab? tab, bool confirmIfDirty = false)
        {
            if (tab == null || Tabs.Count <= 1) return;

            if (confirmIfDirty && tab.CartItems.Count > 0)
            {
                var result = MessageBox.Show(
                    $"Bill #{tab.InvoiceNumber} has items in the cart.\nClose this tab and discard them?",
                    "Close Bill Tab",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }

            var closingActive = ReferenceEquals(tab, SelectedTab);
            var index = Tabs.IndexOf(tab);
            Tabs.Remove(tab);

            RefreshInvoiceNumbers();

            if (closingActive || SelectedTab == null || !Tabs.Contains(SelectedTab))
            {
                var nextIndex = Math.Clamp(index, 0, Tabs.Count - 1);
                SelectedTab = Tabs[nextIndex];
            }

            OnPropertyChanged(nameof(CanCloseTabs));
            NotifyTabPropertiesChanged();
            RecalculateTotal();
            RefocusBarcode();
        }

        /// <summary>After a successful sale: close that tab (or reset the only tab) and renumber.</summary>
        private void FinishCompletedTab(BillingTab completedTab)
        {
            WalkInPhoneInput = "";
            SelectedPaymentMethod = "Cash";
            SelectedOnlineMethod = null;
            SelectedAccount = ActiveAccounts.FirstOrDefault();

            if (Tabs.Count > 1)
            {
                // No confirm — bill already saved
                CloseTab(completedTab, confirmIfDirty: false);
            }
            else
            {
                // Keep one empty tab with the next bill id
                if (!ReferenceEquals(SelectedTab, completedTab))
                    SelectedTab = completedTab;
                ClearCart();
            }

            OnPropertyChanged(nameof(CanCloseTabs));
        }
        private void ScanBarcode() 
        { 
            StatusMessage = string.Empty;
            OnPropertyChanged(nameof(StatusMessage));

            if (IsBillingLocked)
            {
                ShowPopupError($"{InactiveCustomerMessageEn}\n{InactiveCustomerMessageUr}");
                return;
            }

            string bc = !string.IsNullOrWhiteSpace(BarcodeInput) ? BarcodeInput : SelectedSearchItem?.Barcode ?? SelectedSearchItem?.ItemId ?? ""; 
            if (string.IsNullOrWhiteSpace(bc)) return; 
            
            var it = _itemService.GetItemByBarcode(bc)
                  ?? (int.TryParse(bc, out var id) ? _itemService.GetItemById(id) : null);
            if (it != null) 
            { 
                var types = _itemTypeService.GetActiveByItemId(it.Id).OrderBy(t => t.SortOrder).ToList();
                if (!types.Any())
                {
                    StatusMessage = "✗ Item has no active types.";
                    OnPropertyChanged(nameof(StatusMessage));
                    ShowPopupError("Item has no active types.");
                    return;
                }

                var defaultType = types[0];
                AddToCart(it, defaultType, 1.0);

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

        private void AddToCart(Item it, ItemType type, double qty)
        {
            if (SelectedTab == null) return;
            if (IsBillingLocked)
            {
                ShowPopupError($"{InactiveCustomerMessageEn}\n{InactiveCustomerMessageUr}");
                return;
            }
            if (qty <= 0) return;

            var ex = SelectedTab.CartItems.FirstOrDefault(i =>
                i.ItemId == it.ItemId.ToString() && i.TypeId == type.TypeId);

            if (ex != null)
            {
                // Keep unit price in sync with the selected type price
                ex.TypeName = type.TypeName;
                ex.UnitPrice = type.Price;
                ex.Quantity += qty;
                if (string.IsNullOrWhiteSpace(ex.NameUrdu) && !string.IsNullOrWhiteSpace(it.NameUrdu))
                    ex.NameUrdu = it.NameUrdu;
            }
            else
            {
                SelectedTab.CartItems.Add(new CartItem
                {
                    ItemId = it.ItemId,
                    TypeId = type.TypeId,
                    TypeName = type.TypeName,
                    Unit = "piece",
                    Barcode = it.Barcode,
                    ItemDescription = it.Description,
                    NameUrdu = it.NameUrdu,
                    UnitPrice = type.Price,
                    Quantity = qty
                });
            }

            SelectedCartItem = null;
            OnPropertyChanged(nameof(SelectedCartItem));
            RecalculateTotal();
            RefocusBarcode();
        }

        private void AddToCart(Item it) 
        { 
            var types = _itemTypeService.GetActiveByItemId(it.Id).OrderBy(t => t.SortOrder).ToList();
            if (!types.Any())
            {
                ShowPopupError("Item has no active types.");
                return;
            }

            var qty = Math.Max(0.001, QuantityInput);
            AddToCart(it, types[0], qty);
            QuantityInput = 1;
        }
        private void RemoveFromCart()
        {
            if (IsBillingLocked) return;
            if (SelectedCartItem != null && SelectedTab != null)
            {
                SelectedTab.CartItems.Remove(SelectedCartItem);
                RecalculateTotal();
            }
        }
        private void IncreaseQuantity() 
        {
            if (IsBillingLocked) return; 
            if (SelectedCartItem != null) 
            { 
                // Cart +/- steps by 1 KG
                SelectedCartItem.Quantity = Math.Round(SelectedCartItem.Quantity + 1, 3);
                RecalculateTotal(); 
                RefocusBarcode(); 
            } 
        }
        private void DecreaseQuantity() 
        { 
            if (IsBillingLocked) return;
            if (SelectedCartItem == null) return;
            // Cart +/- steps by 1 KG
            var next = Math.Round(SelectedCartItem.Quantity - 1, 3);
            if (next < 1) return;
            SelectedCartItem.Quantity = next;
            RecalculateTotal(); 
            RefocusBarcode(); 
        }
        private void OpenCartBillPreview()
        {
            if (IsBillingLocked)
            {
                ShowPopupError($"{InactiveCustomerMessageEn}\n{InactiveCustomerMessageUr}");
                return;
            }
            if (SelectedTab == null || CartItems.Count == 0)
            {
                ShowPopupError("Add items to the cart before previewing the bill.");
                return;
            }

            RecalculateTotal();
            CartBillPreview = BuildCartPreviewBill();
            IsCartPreviewOpen = true;
        }

        private void CloseCartBillPreview()
        {
            IsCartPreviewOpen = false;
            CartBillPreview = null;
        }

        /// <summary>
        /// Builds an exact draft Bill from the current cart for receipt preview (not saved).
        /// </summary>
        private Bill BuildCartPreviewBill()
        {
            double.TryParse(CashReceivedText, out var cashReceived);
            cashReceived = Math.Round(cashReceived, 2);
            var paid = Math.Min(cashReceived, GrandTotal);
            if (!IsCashPayment && IsWalkIn)
                paid = GrandTotal;

            var changeGiven = Math.Max(0, Math.Round(cashReceived - GrandTotal, 2));
            int.TryParse(InvoiceNumber, out var provisionalId);

            var bill = new Bill
            {
                BillId = provisionalId > 0 ? provisionalId : 0,
                CreatedAt = DateTime.Now,
                Type = "Sale",
                Status = "Preview",
                TaxAmount = TaxAmount,
                DiscountAmount = DiscountAmount,
                SubTotal = SubTotal,
                CashReceived = cashReceived,
                ChangeGiven = changeGiven,
                InitialPayment = paid,
                PaidAmount = paid,
                PaymentMethod = SelectedPaymentMethod,
                OnlinePaymentMethod = IsOnlinePayment
                    ? (SelectedAccount?.DisplayName ?? SelectedOnlineMethod)
                    : null,
                AccountId = SelectedAccount?.Id,
                Account = SelectedAccount,
                CustomerId = SelectedCustomer?.CustomerId,
                Customer = SelectedCustomer,
                UserId = _authService.CurrentUser?.Id,
                User = _authService.CurrentUser
            };

            foreach (var cart in CartItems)
            {
                int.TryParse(cart.ItemId, out var itemInternalId);
                bill.Items.Add(new BillDescription
                {
                    ItemInternalId = itemInternalId,
                    ItemId = cart.ItemId,
                    Barcode = cart.Barcode,
                    ItemDescription = cart.ItemDescription,
                    ItemName = cart.ItemDescription,
                    NameUrdu = cart.NameUrdu,
                    TypeId = cart.TypeId,
                    TypeName = cart.TypeName,
                    Unit = string.IsNullOrWhiteSpace(cart.Unit) ? "piece" : cart.Unit,
                    Quantity = cart.Quantity,
                    UnitPrice = cart.UnitPrice,
                    DiscountAmount = 0,
                    TotalPrice = cart.TotalPrice
                });
            }

            return bill;
        }

        private void RecalculateTotal() { if (SelectedTab == null) return; SubTotal = SelectedTab.CartItems.Sum(i => i.TotalPrice); double.TryParse(DiscountText, out var d); double.TryParse(TaxText, out var t); if (d > SubTotal) { d = SubTotal; DiscountText = d.ToString("F0"); } DiscountAmount = d; TaxAmount = t; GrandTotal = Math.Max(0, SubTotal - DiscountAmount + TaxAmount); if (!IsCashPayment && IsWalkIn) { CashReceivedText = GrandTotal.ToString("F2"); } else if (!IsCashPayment && HasSelectedCustomer) { double.TryParse(CashReceivedText, out var curAmt); if (curAmt > GrandTotal || string.IsNullOrWhiteSpace(CashReceivedText)) { CashReceivedText = GrandTotal.ToString("F2"); } } CalculateChange(); OnPropertyChanged(nameof(SubTotal)); OnPropertyChanged(nameof(DiscountAmount)); OnPropertyChanged(nameof(TaxAmount)); OnPropertyChanged(nameof(GrandTotal)); OnPropertyChanged(nameof(CartItems)); OnPropertyChanged(nameof(PreviewShowTax)); (OpenCartPreviewCommand as RelayCommand)?.RaiseCanExecuteChanged(); }
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
        private bool CanCompleteSale()
        {
            if (SelectedTab == null || !SelectedTab.CartItems.Any()) return false;
            if (IsBillingLocked) return false;
            return true;
        }

        /// <summary>Phone used for walk-in when no registered customer is selected.</summary>
        private string ResolveWalkInPhoneInput()
        {
            if (IsValidPkPhone(WalkInPhoneInput))
                return WalkInPhoneInput.Trim();
            if (IsValidPkPhone(CustomerSearchQuery))
                return CustomerSearchQuery.Trim();
            return (WalkInPhoneInput ?? string.Empty).Trim();
        }

        private async void CompleteSale() 
        { 
            if (IsBillingLocked)
            {
                ShowPopupError($"{InactiveCustomerMessageEn}\n{InactiveCustomerMessageUr}");
                return;
            }

            if (!CanCompleteSale()) return;

            try 
            { 
                if (SelectedTab == null) return;

                StatusMessage = string.Empty;
                OnPropertyChanged(nameof(StatusMessage));

                var txnTime = DateTimeHelper.CaptureTransactionTime();

                // 1. Resolve Customer (GetOrCreate for Walk-in) — walk-in must have phone
                Customer? finalCustomer = SelectedCustomer;
                if (finalCustomer == null || finalCustomer.FullName == "Walk-in Customer")
                {
                    var walkInPhone = ResolveWalkInPhoneInput();
                    if (string.IsNullOrWhiteSpace(walkInPhone) &&
                        finalCustomer != null &&
                        IsValidPkPhone(finalCustomer.PrimaryPhone))
                    {
                        walkInPhone = finalCustomer.PrimaryPhone.Trim();
                    }

                    if (string.IsNullOrWhiteSpace(walkInPhone))
                    {
                        StatusMessage = "✗ Walk-in phone number is required.";
                        OnPropertyChanged(nameof(StatusMessage));
                        ShowPopupError("Walk-in phone is required.\nPlease enter an 11-digit phone number (e.g. 03001234567) to place this bill.");
                        return;
                    }

                    if (!IsValidPkPhone(walkInPhone))
                    {
                        StatusMessage = "✗ Invalid phone format.";
                        OnPropertyChanged(nameof(StatusMessage));
                        ShowPopupError("Invalid phone number.\nPlease enter a valid 11-digit number starting with '0'.");
                        return;
                    }

                    WalkInPhoneInput = walkInPhone;

                    // Alert if the phone number belongs to a registered customer (not a walk-in)
                    var existing = _customerService.GetCustomerByPhone(walkInPhone);
                    if (existing != null && existing.FullName != "Walk-in Customer")
                    {
                        var res = MessageBox.Show(
                            $"The phone number '{walkInPhone}' is registered to customer '{existing.FullName}'.\n\nDo you want to proceed with this customer?",
                            "Registered Customer Found",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);
                        
                        if (res == MessageBoxResult.Yes)
                        {
                            finalCustomer = existing;
                        }
                        else
                        {
                            return; // User wants to change the number
                        }
                    }
                    else
                    {
                        finalCustomer = _customerService.GetOrCreateWalkIn(walkInPhone);
                    }
                }

                double.TryParse(DiscountText, out var d); 
                double.TryParse(TaxText, out var t);
                double sub     = CartItems.Sum(i => i.TotalPrice);
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

                    if (HasSelectedCustomer && !IsWalkIn)
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

                var onlineMethod = SelectedOnlineMethod
                    ?? SelectedAccount?.AccountTitle
                    ?? SelectedAccount?.DisplayName;

                // Capture the tab being completed so async print / UI updates can't switch it away
                var completedTab = SelectedTab!;
                var cartSnapshot = completedTab.CartItems.Select(c => new Models.BillDescription
                {
                    ItemInternalId = int.TryParse(c.ItemId, out var id) ? id : 0,
                    ItemId = c.ItemId,
                    Quantity = c.Quantity,
                    UnitPrice = c.UnitPrice,
                    ItemDescription = c.ItemDescription,
                    ItemName = c.ItemDescription,
                    NameUrdu = c.NameUrdu,
                    TypeId = c.TypeId,
                    TypeName = c.TypeName,
                    Unit = "piece"
                }).ToList();

                var sb = _billService.CompleteBill(
                    _authService.CurrentUser?.Id,
                    finalCustomer?.CustomerId,
                    cartSnapshot,
                    d,
                    t,
                    cashReceived,
                    paidAmount,
                    SelectedBillingAddress,
                    SelectedPaymentMethod,
                    onlineMethod,
                    SelectedAccount?.Id);

                // Ensure Customer object is attached for PrintService
                sb.Customer = finalCustomer;

                await AttemptPrint(sb);

                // Real bill id from DB (may differ from provisional tab preview)
                StatusMessage = $"✓ Sale Completed: Bill #{sb.InvoiceNumber} | {sb.PaymentStatus}";
                OnPropertyChanged(nameof(StatusMessage));
                ShowPopupSuccess(StatusMessage);

                FinishCompletedTab(completedTab);
                CloseCartBillPreview();
                RefocusBarcode();

                // Instant top-bar + dashboard refresh (Sales / Cash / Online / Credit)
                LoadDashboardStats();
                SalesEvents.NotifyChanged();
            }
            catch (Exception ex) { StatusMessage = $"✗ Bill failed: {ex.Message}"; OnPropertyChanged(nameof(StatusMessage)); ShowPopupError($"Bill failed: {ex.Message}"); AppLogger.Error("Complete bill failed", ex); }
        }
        private async Task AttemptPrint(Bill b) 
        { 
            try
            {
                // Ensure line items + payment snapshot are present for the receipt
                var full = _billRepo.GetById(b.BillId) ?? b;
                if (full.Items == null || full.Items.Count == 0)
                    full.Items = b.Items;
                full.Customer ??= b.Customer;
                full.CashReceived = b.CashReceived > 0 ? b.CashReceived : full.CashReceived;
                full.ChangeGiven = b.ChangeGiven;
                full.PaidAmount = b.PaidAmount > 0 ? b.PaidAmount : full.PaidAmount;
                full.PaymentMethod = string.IsNullOrWhiteSpace(b.PaymentMethod) ? full.PaymentMethod : b.PaymentMethod;
                full.OnlinePaymentMethod = b.OnlinePaymentMethod ?? full.OnlinePaymentMethod;
                full.Account = b.Account ?? full.Account;
                full.BillingAddress = b.BillingAddress ?? full.BillingAddress;

                if (full.Items == null || full.Items.Count == 0)
                {
                    AppLogger.Warning($"AttemptPrint: Bill #{full.BillId} has zero items — attaching cart snapshot failed.");
                }

                // Always attempt print. IsPrinterOnline used to skip printing entirely
                // after false "offline" / purge failures on BlackCopper.
                bool printSuccess = _printService.PrintReceipt(full, _authService.CurrentUser?.FullName ?? "Cashier");
                if (printSuccess)
                {
                    _billRepo.UpdatePrintStatus(full.BillId, true, DateTime.Now);

                    // Also print gate pass (same sale, no Total / no payment footer)
                    bool gateOk = _printService.PrintGatePass(full, _authService.CurrentUser?.FullName ?? "Cashier");
                    if (!gateOk)
                    {
                        AppLogger.Warning($"AttemptPrint: Gate pass failed for Bill #{full.BillId}");
                        ShowPopupError("Bill printed, but the gate pass could not be printed.\nCheck that the printer is ON and connected.");
                    }
                    return;
                }

                AppLogger.Warning($"AttemptPrint: PrintReceipt returned false for Bill #{full.BillId}");
                _billRepo.UpdatePrintStatus(full.BillId, false, null);
                ShowPopupError("Sale saved, but the bill could not be printed.\nCheck that BlackCopper 80mm is ON and connected.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("AttemptPrint failed", ex);
                _billRepo.UpdatePrintStatus(b.BillId, false, null);
                ShowPopupError($"Sale saved, but printing failed:\n{ex.Message}");
            }

            await Task.CompletedTask;
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
            CloseCartBillPreview();
            
            // Ensure UI is notified of reset fields
            OnPropertyChanged(nameof(CashReceivedText));
            OnPropertyChanged(nameof(DiscountText));
            OnPropertyChanged(nameof(TaxText));
            
            RefreshInvoiceNumbers(); 
        }
        private void SearchCustomers()
        {
            if (SelectedTab == null) return;

            SelectedSearchResult = null;
            SelectedTab.CustomerSearchResults.Clear();

            var query = CustomerSearchQuery?.Trim() ?? string.Empty;
            if (query.Length >= 1)
            {
                foreach (var c in _customerService.SearchCustomers(query))
                    SelectedTab.CustomerSearchResults.Add(c);

                if (SelectedTab.CustomerSearchResults.Count > 0)
                    SelectedSearchResult = SelectedTab.CustomerSearchResults[0];
            }

            OnPropertyChanged(nameof(CustomerSearchResults));
            OnPropertyChanged(nameof(HasCustomerSearchResults));
            OnPropertyChanged(nameof(IsCustomerDropDownOpen));
            OnPropertyChanged(nameof(SelectedSearchResult));
        }
        private void TrySelectWalkInCustomer(string phone)
        {
            if (!IsValidPkPhone(phone))
                return;

            if (SelectedCustomer != null && SelectedCustomer.Phone == phone)
                return;

            var existing = _customerService.GetCustomerByPhone(phone);
            if (existing != null && existing.FullName != "Walk-in Customer")
            {
                var res = System.Windows.MessageBox.Show(
                    $"The phone number '{phone}' is registered to customer '{existing.FullName}'.\n\nDo you want to proceed with this customer?",
                    "Registered Customer Found",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (res == System.Windows.MessageBoxResult.Yes)
                {
                    SelectCustomer(existing);
                    WalkInPhoneInput = "";
                }
                return;
            }

            var walkInCustomer = _customerService.GetOrCreateWalkIn(phone);
            SelectCustomer(walkInCustomer);
            WalkInPhoneInput = "";
        }
        private void SelectCustomer(Customer? c)
        {
            var targetCustomer = c ?? SelectedSearchResult;
            if (targetCustomer == null || SelectedTab == null) return;

            // Prefer fresh DB status (reactivated / deactivated since search)
            var fresh = _customerService.GetCustomerById(targetCustomer.CustomerId);
            if (fresh != null)
                targetCustomer = fresh;
            
            SelectedCustomer = targetCustomer;
            SelectedTab.CustomerSearchQuery = "";
            SelectedTab.CustomerSearchResults.Clear();
            SelectedSearchResult = null;
            OnPropertyChanged(nameof(CustomerSearchQuery));
            OnPropertyChanged(nameof(CustomerSearchResults));
            OnPropertyChanged(nameof(HasCustomerSearchResults));
            OnPropertyChanged(nameof(IsCustomerDropDownOpen));

            if (IsCustomerInactive)
            {
                // Lock billing: clear cart so nothing can be sold for an inactive account
                foreach (var item in SelectedTab.CartItems)
                    item.PropertyChanged -= OnCartItemPropertyChanged;
                SelectedTab.CartItems.Clear();
                RecalculateTotal();
                OnPropertyChanged(nameof(CartItems));

                PendingCreditAmount = 0;
                AvailableAddresses.Clear();
                SelectedBillingAddress = null;
                StatusMessage = $"{InactiveCustomerMessageEn} · {InactiveCustomerMessageUr}";
                OnPropertyChanged(nameof(StatusMessage));
                ShowPopupError($"{InactiveCustomerMessageEn}\n{InactiveCustomerMessageUr}");
                (OpenCustomerHistoryCommand as RelayCommand)?.RaiseCanExecuteChanged();
                return;
            }

            LoadCustomerHistory(targetCustomer.CustomerId);

            // Load pending credit for warning badge
            PendingCreditAmount = _customerService.GetPendingCredit(targetCustomer.CustomerId);
            (OpenCustomerHistoryCommand as RelayCommand)?.RaiseCanExecuteChanged();

            // Populate address selection
            AvailableAddresses.Clear();
            if (!string.IsNullOrWhiteSpace(targetCustomer.Address)) AvailableAddresses.Add(targetCustomer.Address);
            if (!string.IsNullOrWhiteSpace(targetCustomer.Address2)) AvailableAddresses.Add(targetCustomer.Address2);
            if (!string.IsNullOrWhiteSpace(targetCustomer.Address3)) AvailableAddresses.Add(targetCustomer.Address3);
            SelectedBillingAddress = AvailableAddresses.FirstOrDefault();
            StatusMessage = string.Empty;
            OnPropertyChanged(nameof(StatusMessage));
        }
        private void ClearCustomer() 
        { 
            if (SelectedTab == null) return;

            // Clear customer identity
            SelectedCustomer = null; 
            WalkInPhoneInput = "";
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
            OnPropertyChanged(nameof(IsWalkInCustomerSelected));
            OnPropertyChanged(nameof(IsRegisteredCustomerSelected));
            OnPropertyChanged(nameof(HasSelectedCustomer));
            OnPropertyChanged(nameof(PendingCreditAmount));
            OnPropertyChanged(nameof(IsCustomerInactive));
            OnPropertyChanged(nameof(IsBillingLocked));
            OnPropertyChanged(nameof(MenuOpacity));
            OnPropertyChanged(nameof(InactiveCustomerBannerVisible));
            IsCustomerHistoryOpen = false;
            (OpenCustomerHistoryCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
        private void LoadCustomerHistory(int id)
        {
            if (SelectedTab == null) return;
            var bills = _billRepo.GetBillsByCustomerId(id);
            SelectedTab.CustomerBills.Clear();
            foreach (var b in bills)
                SelectedTab.CustomerBills.Add(b);
            OnPropertyChanged(nameof(CustomerBills));
        }

        private void OpenCustomerHistory()
        {
            if (!IsRegisteredCustomerSelected || SelectedCustomer == null) return;

            LoadCustomerHistory(SelectedCustomer.CustomerId);
            PendingCreditAmount = _customerService.GetPendingCredit(SelectedCustomer.CustomerId);
            IsCustomerHistoryOpen = true;
        }

        private void SetPreviewHistoryBill(Bill bill)
        {
            if (SelectedTab == null) return;
            var fresh = _billRepo.GetById(bill.BillId) ?? bill;
            fresh.Customer ??= SelectedCustomer;
            SelectedTab.PreviewHistoryBill = fresh;
            OnPropertyChanged(nameof(PreviewHistoryBill));
        }

        private void ViewCustomerHistoryBill(Bill? bill)
        {
            if (bill == null) return;
            SetPreviewHistoryBill(bill);
            IsBillDetailOpen = true;
        }

        private void PayCustomerHistoryBill(Bill? bill)
        {
            if (bill == null || bill.RemainingAmount <= 0.01) return;
            SetPreviewHistoryBill(bill);
            HistoryPaymentAmount = "";
            HistoryPaymentNote = "";
            HistoryPaymentError = "";
            SelectedHistoryPaymentMethod = "Cash";
            SelectedHistoryAccount = HistoryActiveAccounts.FirstOrDefault();
            SelectedHistoryOnlineMethod = null;
            IsHistoryPaymentOpen = true;
        }
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

            foreach (var it in b.Items)
            {
                var currentItem = _itemService.GetItemById(it.ItemInternalId);
                double currentPrice = it.UnitPrice;
                if (it.TypeId.HasValue)
                {
                    var type = _itemTypeService.GetById(it.TypeId.Value);
                    if (type != null) currentPrice = type.Price;
                }
                else if (currentItem != null)
                {
                    var fallbackType = _itemTypeService.GetActiveByItemId(currentItem.Id)
                        .OrderBy(t => t.SortOrder)
                        .FirstOrDefault();
                    if (fallbackType != null)
                        currentPrice = fallbackType.Price;
                }

                var existing = SelectedTab.CartItems.FirstOrDefault(c => c.ItemId == it.ItemId && c.TypeId == it.TypeId);
                if (existing != null)
                {
                    existing.UnitPrice = currentPrice;
                    existing.Quantity += it.Quantity;
                    existing.IsCopied = true;
                }
                else
                {
                    SelectedTab.CartItems.Add(new CartItem 
                    { 
                        ItemId = it.ItemId, 
                        TypeId = it.TypeId,
                        TypeName = it.TypeName,
                        Unit = "piece",
                        Barcode = currentItem?.Barcode,
                        ItemDescription = currentItem?.Description ?? it.ItemDescription,
                        NameUrdu = currentItem?.NameUrdu,
                        UnitPrice = currentPrice, 
                        Quantity = it.Quantity,
                        IsCopied = true
                    });
                }
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
        private void SaveNewCustomer()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(NewCustomerName) || string.IsNullOrWhiteSpace(NewCustomerPhone))
                {
                    RegistrationErrorMessage = "Name and Phone are required.";
                    OnPropertyChanged(nameof(RegistrationErrorMessage));
                    return;
                }

                var existing = _customerService.GetCustomerByPhone(NewCustomerPhone);
                if (existing != null && existing.FullName.Equals("Walk-in Customer", StringComparison.OrdinalIgnoreCase))
                {
                    var confirmResult = MessageBox.Show(
                        $"A Walk-in Customer with the phone number '{NewCustomerPhone}' already exists.\n\nDo you want to convert this record to a registered customer? This will preserve all previous bills and purchase history.",
                        "Convert Walk-in Customer",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (confirmResult != MessageBoxResult.Yes)
                        return;
                }

                var customer = new Customer
                {
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
                IsRegistrationVisible = false;
                OnPropertyChanged(nameof(IsRegistrationVisible));
                ClearRegistrationForm();
            }
            catch (Exception ex)
            {
                RegistrationErrorMessage = ex.Message;
                OnPropertyChanged(nameof(RegistrationErrorMessage));
            }
        }
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
                OnPropertyChanged(nameof(IsCustomerDropDownOpen));
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
            if (e.PropertyName == nameof(CartItem.Quantity) || e.PropertyName == nameof(CartItem.UnitPrice))
                RecalculateTotal();
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

                LoadDashboardStats();
                SalesEvents.NotifyChanged();
            }
            catch (Exception ex)
            {
                HistoryPaymentError = ex.Message;
                AppLogger.Error("RecordHistoryPayment failed", ex);
            }
        }

        private void NotifyTabPropertiesChanged() { OnPropertyChanged(nameof(CartItems)); OnPropertyChanged(nameof(DiscountText)); OnPropertyChanged(nameof(TaxText)); OnPropertyChanged(nameof(CashReceivedText)); OnPropertyChanged(nameof(InvoiceNumber)); OnPropertyChanged(nameof(SelectedCustomer)); OnPropertyChanged(nameof(HasSelectedCustomer)); OnPropertyChanged(nameof(IsWalkIn)); OnPropertyChanged(nameof(IsWalkInCustomerSelected)); OnPropertyChanged(nameof(IsRegisteredCustomerSelected)); OnPropertyChanged(nameof(CustomerSearchQuery)); OnPropertyChanged(nameof(CustomerSearchResults)); OnPropertyChanged(nameof(SelectedSearchResult)); OnPropertyChanged(nameof(CustomerBills)); OnPropertyChanged(nameof(PreviewHistoryBill)); OnPropertyChanged(nameof(IsHistoryPaymentOpen)); OnPropertyChanged(nameof(HistoryPaymentAmount)); OnPropertyChanged(nameof(HistoryPaymentNote)); OnPropertyChanged(nameof(HistoryPaymentError)); OnPropertyChanged(nameof(PendingCreditAmount)); OnPropertyChanged(nameof(HasPendingCredit)); OnPropertyChanged(nameof(PendingCreditDisplay)); OnPropertyChanged(nameof(SelectedBillingAddress)); OnPropertyChanged(nameof(StatusMessage)); OnPropertyChanged(nameof(IsBillDetailOpen)); }

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

        public override void Dispose() { _timer.Stop(); base.Dispose(); }
    }
}
