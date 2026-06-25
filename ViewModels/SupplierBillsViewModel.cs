using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Microsoft.Win32;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using GroceryPOS.Helpers;
using GroceryPOS.Models;
using GroceryPOS.Services;

namespace GroceryPOS.ViewModels
{
    public class SupplierBillsViewModel : BaseViewModel
    {
        private readonly IStockService _stockService;
        private readonly IImageStorageService _imageService;
        private readonly GroceryPOS.Data.Repositories.ItemRepository _itemRepo;

        // ────────────────────────────────────────────
        //  STOCK CART (new primary purchase path)
        // ────────────────────────────────────────────

        /// <summary>The active stock purchase cart. Bound to the cart DataGrid.</summary>
        public ObservableCollection<StockPurchaseItem> CartItems { get; } = new();

        private double _cartTotal;
        /// <summary>Live-updating sum of all cart line totals.</summary>
        public double CartTotal { get => _cartTotal; private set => SetProperty(ref _cartTotal, value); }

        private string _cartStatus = string.Empty;
        /// <summary>Feedback message shown under the cart (success / error).</summary>
        public string CartStatus { get => _cartStatus; set => SetProperty(ref _cartStatus, value); }

        private bool _isCartBusy;
        /// <summary>True while the checkout async operation is running.</summary>
        public bool IsCartBusy { get => _isCartBusy; set { SetProperty(ref _isCartBusy, value); (CheckoutCartCommand as RelayCommand)?.RaiseCanExecuteChanged(); } }

        // Cart product search
        private string _cartSearchText = string.Empty;
        public string CartSearchText
        {
            get => _cartSearchText;
            set { if (SetProperty(ref _cartSearchText, value) && !string.IsNullOrWhiteSpace(value)) _ = ExecuteCartSearch(); }
        }

        private bool _isCartSearchOpen;
        public bool IsCartSearchOpen { get => _isCartSearchOpen; set => SetProperty(ref _isCartSearchOpen, value); }

        public ObservableCollection<Item> CartSearchResults { get; } = new();

        private Item? _cartSelectedResult;
        public Item? CartSelectedResult
        {
            get => _cartSelectedResult;
            set { if (SetProperty(ref _cartSelectedResult, value) && value != null) ExecuteAddItemToCart(value); }
        }

        private string _cartTempImagePath = string.Empty;
        public string CartTempImagePath { get => _cartTempImagePath; set { SetProperty(ref _cartTempImagePath, value); (CheckoutCartCommand as RelayCommand)?.RaiseCanExecuteChanged(); } }

        private BitmapImage? _cartImagePreview;
        public BitmapImage? CartImagePreview { get => _cartImagePreview; set => SetProperty(ref _cartImagePreview, value); }

        public ObservableCollection<Item> AllProducts { get; } = new();

        private Item? _selectedProductFromList;
        public Item? SelectedProductFromList
        {
            get => _selectedProductFromList;
            set
            {
                if (SetProperty(ref _selectedProductFromList, value))
                {
                    (AddSelectedFromListToCartCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private string _cartTotalText = "0";
        public string CartTotalText
        {
            get => _cartTotalText;
            set
            {
                if (SetProperty(ref _cartTotalText, value))
                {
                    if (double.TryParse(value, out double val))
                    {
                        _cartTotal = val;
                        OnPropertyChanged(nameof(CartTotal));
                    }
                }
            }
        }

        // Cart commands
        public ICommand AddToCartCommand { get; }
        public ICommand RemoveFromCartCommand { get; }
        public ICommand ClearCartCommand { get; }
        public ICommand CheckoutCartCommand { get; }
        public ICommand SelectCartImageCommand { get; }
        public ICommand AddSelectedFromListToCartCommand { get; }

        // ────────────────────────────────────────────
        //  Legacy supply history
        // ────────────────────────────────────────────

        public ObservableCollection<Stock> SupplyHistory { get; set; } = new();
        public ObservableCollection<Item> ProductSearchResults { get; set; } = new();
        public ObservableCollection<Item> MainProductSearchResults { get; set; } = new();

        private Stock? _selectedEntry;
        public Stock? SelectedEntry
        {
            get => _selectedEntry;
            set
            {
                if (SetProperty(ref _selectedEntry, value))
                {
                    LoadImagePreview(value?.ImagePath);
                }
            }
        }

        private string _searchBarcode = string.Empty;
        public string SearchBarcode 
        { 
            get => _searchBarcode; 
            set 
            { 
                if (SetProperty(ref _searchBarcode, value))
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        IsMainResultsOpen = false;
                        _ = LoadAllRecentSupplies();
                    }
                    else
                    {
                        _ = ExecuteLiveMainSearch();
                    }
                }
            } 
        }

        private Item? _foundItem;
        public Item? FoundItem
        {
            get => _foundItem;
            set
            {
                if (SetProperty(ref _foundItem, value))
                {
                    (SaveSupplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private BitmapImage? _billImagePreview;
        public BitmapImage? BillImagePreview
        {
            get => _billImagePreview;
            set => SetProperty(ref _billImagePreview, value);
        }

        private bool _isImageAvailable;
        public bool IsImageAvailable
        {
            get => _isImageAvailable;
            set => SetProperty(ref _isImageAvailable, value);
        }

        private BitmapImage? _formImagePreview;
        public BitmapImage? FormImagePreview
        {
            get => _formImagePreview;
            set
            {
                if (SetProperty(ref _formImagePreview, value))
                {
                    (SaveSupplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        // --- Registration Form Fields ---
        private int _formQuantity;
        public int FormQuantity 
        { 
            get => _formQuantity; 
            set
            {
                int validatedValue = value < 1 ? 1 : value;
                if (SetProperty(ref _formQuantity, validatedValue))
                {
                    (SaveSupplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private string _tempImagePath = string.Empty;
        public string TempImagePath { get => _tempImagePath; set => SetProperty(ref _tempImagePath, value); }

        private bool _isAddingNew;
        public bool IsAddingNew { get => _isAddingNew; set => SetProperty(ref _isAddingNew, value); }

        private bool _isEditing;
        public bool IsEditing { get => _isEditing; set => SetProperty(ref _isEditing, value); }

        private bool _isSearchResultsOpen;
        public bool IsSearchResultsOpen { get => _isSearchResultsOpen; set => SetProperty(ref _isSearchResultsOpen, value); }

        private bool _isMainResultsOpen;
        public bool IsMainResultsOpen { get => _isMainResultsOpen; set => SetProperty(ref _isMainResultsOpen, value); }

        private Item? _selectedProductResult;
        public Item? SelectedProductResult
        {
            get => _selectedProductResult;
            set
            {
                if (SetProperty(ref _selectedProductResult, value) && value != null)
                {
                    ExecuteSelectProduct(value);
                }
            }
        }

        private Item? _selectedMainProductResult;
        public Item? SelectedMainProductResult
        {
            get => _selectedMainProductResult;
            set
            {
                if (SetProperty(ref _selectedMainProductResult, value) && value != null)
                {
                    ExecuteSelectMainProduct(value);
                }
            }
        }

        private string _manualSearchBarcode = string.Empty;
        public string ManualSearchBarcode 
        { 
            get => _manualSearchBarcode; 
            set 
            { 
                if (SetProperty(ref _manualSearchBarcode, value))
                {
                    if (string.IsNullOrWhiteSpace(value))
                        IsSearchResultsOpen = false;
                    else
                        _ = ExecuteFindManualItem();
                }
            } 
        }

        private DateTime _currentSystemDate;
        public DateTime CurrentSystemDate { get => _currentSystemDate; set => SetProperty(ref _currentSystemDate, value); }

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        public ICommand SearchItemCommand { get; }
        public ICommand ShowAddFormCommand { get; }
        public ICommand SelectImageCommand { get; }
        public ICommand SaveSupplyCommand { get; }
        public ICommand CancelAddCommand { get; }
        public ICommand UpdateSupplyCommand { get; }
        public ICommand FindManualItemCommand { get; }
        public ICommand SelectProductCommand { get; }
        public ICommand SelectMainProductCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ViewFullBillCommand { get; }
        public ICommand ToggleSearchResultsCommand { get; }

        public SupplierBillsViewModel(IStockService stockService, IImageStorageService imageService, GroceryPOS.Data.Repositories.ItemRepository itemRepo)
        {
            _stockService = stockService;
            _imageService = imageService;
            _itemRepo     = itemRepo;

            // ── Cart commands ──
            AddToCartCommand    = new RelayCommand<Item>(ExecuteAddItemToCart);
            RemoveFromCartCommand = new RelayCommand<StockPurchaseItem>(ExecuteRemoveFromCart);
            ClearCartCommand    = new RelayCommand(ExecuteClearCart, () => CartItems.Count > 0);
            CheckoutCartCommand = new RelayCommand(async () => await ExecuteCheckoutCartAsync(),
                                                   () => CartItems.Count > 0 && !IsCartBusy && !string.IsNullOrEmpty(CartTempImagePath));
            SelectCartImageCommand = new RelayCommand(ExecuteSelectCartImage);
            AddSelectedFromListToCartCommand = new RelayCommand(ExecuteAddSelectedFromListToCart, () => SelectedProductFromList != null);

            // ── Legacy supply commands ──
            SearchItemCommand = new RelayCommand(async () => await ExecuteExplicitSearchItem());
            ShowAddFormCommand = new RelayCommand(ExecuteShowAddForm);
            SelectImageCommand = new RelayCommand(ExecuteSelectImage);
            SaveSupplyCommand = new RelayCommand(async () => await ExecuteSaveSupply(), () => FoundItem != null && FormQuantity > 0 && FormImagePreview != null);
            CancelAddCommand = new RelayCommand(() => IsAddingNew = false);
            UpdateSupplyCommand = new RelayCommand(async (s) => await ExecuteUpdateSupply(s as Stock));

            FindManualItemCommand = new RelayCommand(async () => await ExecuteExplicitManualSearch());
            SelectProductCommand = new RelayCommand((p) => ExecuteSelectProduct(p as Item));
            SelectMainProductCommand = new RelayCommand((p) => ExecuteSelectMainProduct(p as Item));
            RefreshCommand = new RelayCommand(async () => await LoadAllRecentSupplies());
            ViewFullBillCommand = new RelayCommand(ExecuteViewFullBill, () => SelectedEntry != null && !string.IsNullOrEmpty(SelectedEntry.ImagePath));
            ToggleSearchResultsCommand = new RelayCommand(async () => await ExecuteToggleSearchResults());

            CartItems.CollectionChanged += (s, e) => {
                if (e.NewItems != null)
                {
                    foreach (StockPurchaseItem item in e.NewItems)
                        item.PropertyChanged += OnCartItemPropertyChanged;
                }
                if (e.OldItems != null)
                {
                    foreach (StockPurchaseItem item in e.OldItems)
                        item.PropertyChanged -= OnCartItemPropertyChanged;
                }
                RecalculateCartTotal();
                (ClearCartCommand    as RelayCommand)?.RaiseCanExecuteChanged();
                (CheckoutCartCommand as RelayCommand)?.RaiseCanExecuteChanged();
            };

            // Subscribe to real-time stock changes
            _stockService.StockChanged += OnStockChanged;

            // Auto-load recent supplies on startup
            _ = LoadAllRecentSupplies();

            LoadInitialData();
        }

        private void LoadInitialData()
        {
            Task.Run(() => {
                try {
                    var items = _itemRepo.GetAll().OrderBy(i => i.Description).ToList();
                    Dispatch(() => {
                        AllProducts.Clear();
                        foreach (var item in items) AllProducts.Add(item);
                    });
                } catch (Exception ex) {
                    AppLogger.Error("Failed to load products for dropdown", ex);
                }
            });
        }
        
        private void OnCartItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StockPurchaseItem.Quantity) || e.PropertyName == nameof(StockPurchaseItem.CostPrice))
            {
                RecalculateCartTotal();
            }
        }

        private void ExecuteSelectProductForCart(Item? item)
        {
            if (item != null) ExecuteAddItemToCart(item);
        }

        private async void OnStockChanged()
        {
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (FoundItem != null)
                        await LoadSupplyHistory();
                    else
                        await LoadAllRecentSupplies();
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error("StockChanged handler failed", ex);
            }
        }

        private async Task LoadAllRecentSupplies()
        {
            try
            {
                StatusMessage = "Loading recent stock...";
                // Clear search state silently to avoid infinite recursion
                _searchBarcode = string.Empty;
                OnPropertyChanged(nameof(SearchBarcode));
                FoundItem = null;
                
                var allSupplies = await _stockService.GetAllRecentSuppliesAsync(50);
                SupplyHistory.Clear();
                foreach (var entry in allSupplies) SupplyHistory.Add(entry);
                StatusMessage = $"Showing {allSupplies.Count} recent stock entries.";
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load all supplies", ex);
                StatusMessage = "Error loading stock.";
                ShowPopupError("Error loading stock.");
            }
        }

        private void ExecuteShowAddForm()
        {
            AppLogger.Info("ExecuteShowAddForm called.");
            
            // Even if FoundItem is null, let's open it to show the user the form exists, 
            // but we'll handle the "no item" state in the UI.
            IsAddingNew = true;
            
            // Reset form
            IsEditing = false;
            ClearForm();
            ManualSearchBarcode = string.Empty;
            CurrentSystemDate = DateTime.Now;
            
            if (FoundItem == null)
            {
                StatusMessage = "Please search for an item first to record a stock entry.";
            }
            else
            {
                StatusMessage = $"Registering stock for: {FoundItem.Description}";
            }
        }

        private async Task ExecuteExplicitSearchItem()
        {
            if (IsMainResultsOpen && MainProductSearchResults.Count > 0)
            {
                SelectedMainProductResult = MainProductSearchResults[0];
            }
            else
            {
                await ExecuteSearchItem();
            }
        }

        private async Task ExecuteSearchItem()
        {
            if (string.IsNullOrWhiteSpace(SearchBarcode))
            {
                await LoadAllRecentSupplies();
                return;
            }

            try
            {
                StatusMessage = "Searching...";
                // We'll use a simple DB query via DatabaseHelper since we don't have a direct "GetItem" in StockService
                // But better to use the repository if possible. For now, let's assume we can fetch it.
                // In a real app, I'd inject ItemRepository here too.
                using var conn = Data.DatabaseHelper.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT i.*, COALESCE((SELECT SUM(QuantityChange) FROM InventoryLogs WHERE ItemId = i.ItemId), 0) as StockQuantity
                    FROM Items i WHERE i.Barcode = @bid OR i.Description LIKE @name LIMIT 1;";
                cmd.Parameters.AddWithValue("@bid", SearchBarcode);
                cmd.Parameters.AddWithValue("@name", $"%{SearchBarcode}%");

                using var reader = await cmd.ExecuteReaderAsync();
                if (reader.Read())
                {
                    FoundItem = new Item
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("ItemId")),
                        Barcode = reader.IsDBNull(reader.GetOrdinal("Barcode")) ? null : reader.GetString(reader.GetOrdinal("Barcode")),
                        Description = reader.GetString(reader.GetOrdinal("Description")),
                        StockQuantity = reader.GetDouble(reader.GetOrdinal("StockQuantity"))
                    };
                    await LoadSupplyHistory();
                    
                    // Clear search bar silently to keep filtered list
                    _searchBarcode = string.Empty;
                    OnPropertyChanged(nameof(SearchBarcode));
                }
                else
                {
                    FoundItem = null;
                    SupplyHistory.Clear();
                    StatusMessage = "Item not found.";
                    ShowPopupError("Item not found.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Search failed", ex);
                StatusMessage = "Search error.";
                ShowPopupError("Search error.");
            }
        }

        private async Task ExecuteUpdateSupply(Stock? entry)
        {
            if (entry == null) return;
            
            try
            {
                StatusMessage = "Loading entry for update...";
                
                // 1. Setup Form State
                IsEditing = true;
                IsAddingNew = true;
                FormQuantity = entry.Quantity;
                TempImagePath = string.Empty;
                ManualSearchBarcode = entry.ProductId;
                
                // 2. Fetch the item to show in form
                using var conn = Data.DatabaseHelper.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT i.*, COALESCE((SELECT SUM(QuantityChange) FROM InventoryLogs WHERE ItemId = i.ItemId), 0) as StockQuantity
                    FROM Items i WHERE i.Barcode = @bid LIMIT 1;";
                cmd.Parameters.AddWithValue("@bid", entry.ProductId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (reader.Read())
                {
                    FoundItem = new Item
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("ItemId")),
                        Barcode = reader.IsDBNull(reader.GetOrdinal("Barcode")) ? null : reader.GetString(reader.GetOrdinal("Barcode")),
                        Description = reader.GetString(reader.GetOrdinal("Description")),
                        StockQuantity = reader.GetDouble(reader.GetOrdinal("StockQuantity"))
                    };
                    ManualSearchBarcode = FoundItem.Description; // Show description in update modal box
                    StatusMessage = "Editing stock for: " + FoundItem.Description;
                    
                    if (!string.IsNullOrEmpty(entry.ImagePath) && File.Exists(entry.ImagePath))
                    {
                        LoadFormImagePreview(entry.ImagePath);
                    }
                }
                
                SelectedEntry = entry; // Keep track of which entry we are editing
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load supply for update", ex);
                StatusMessage = "Error loading update form.";
                ShowPopupError("Error loading update form.");
            }
        }

        private async Task ExecuteExplicitManualSearch()
        {
            if (IsSearchResultsOpen && ProductSearchResults.Count > 0)
            {
                SelectedProductResult = ProductSearchResults[0];
            }
            else
            {
                await ExecuteFindManualItem();
            }
        }

        private async Task ExecuteFindManualItem(string? filter = null)
        {
            try
            {
                string queryText = filter ?? ManualSearchBarcode;
                StatusMessage = "Looking for items...";
                using var conn = Data.DatabaseHelper.GetConnection();
                using var cmd = conn.CreateCommand();
                
                if (string.IsNullOrWhiteSpace(queryText))
                {
                    cmd.CommandText = @"SELECT i.*, COALESCE((SELECT SUM(QuantityChange) FROM InventoryLogs WHERE ItemId = i.ItemId), 0) as StockQuantity
                        FROM Items i ORDER BY i.Description LIMIT 50;";
                }
                else
                {
                    cmd.CommandText = @"SELECT i.*, COALESCE((SELECT SUM(QuantityChange) FROM InventoryLogs WHERE ItemId = i.ItemId), 0) as StockQuantity
                        FROM Items i WHERE i.Barcode = @bid OR i.Description LIKE @name LIMIT 50;";
                    cmd.Parameters.AddWithValue("@bid", queryText);
                    cmd.Parameters.AddWithValue("@name", $"%{queryText}%");
                }

                ProductSearchResults.Clear();
                using var reader = await cmd.ExecuteReaderAsync();
                while (reader.Read())
                {
                    ProductSearchResults.Add(new Item
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("ItemId")),
                        Barcode = reader.IsDBNull(reader.GetOrdinal("Barcode")) ? null : reader.GetString(reader.GetOrdinal("Barcode")),
                        Description = reader.GetString(reader.GetOrdinal("Description")),
                        StockQuantity = reader.GetDouble(reader.GetOrdinal("StockQuantity"))
                    });
                }

                if (ProductSearchResults.Count > 0)
                {
                    IsSearchResultsOpen = true;
                    StatusMessage = $"Found {ProductSearchResults.Count} matching items.";
                }
                else
                {
                    IsSearchResultsOpen = false;
                    StatusMessage = "No matching items found.";
                    ShowPopupError("No matching items found.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Manual search failed", ex);
                StatusMessage = "Error finding item.";
                ShowPopupError("Error finding item.");
            }
        }

        private async Task ExecuteToggleSearchResults()
        {
            if (IsSearchResultsOpen)
            {
                IsSearchResultsOpen = false;
            }
            else
            {
                // When toggling manually, if we have a full description, show all to allow selection of another
                bool isFullDescription = FoundItem != null && ManualSearchBarcode == FoundItem.Description;
                await ExecuteFindManualItem(isFullDescription ? "" : null);
                IsSearchResultsOpen = ProductSearchResults.Count > 0;
            }
        }

        private async Task ExecuteLiveMainSearch()
        {
            if (string.IsNullOrWhiteSpace(SearchBarcode)) return;

            try
            {
                using var conn = Data.DatabaseHelper.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT i.*, COALESCE((SELECT SUM(QuantityChange) FROM InventoryLogs WHERE ItemId = i.ItemId), 0) as StockQuantity
                    FROM Items i WHERE i.Barcode = @bid OR i.Description LIKE @name LIMIT 10;";
                cmd.Parameters.AddWithValue("@bid", SearchBarcode);
                cmd.Parameters.AddWithValue("@name", $"%{SearchBarcode}%");

                MainProductSearchResults.Clear();
                using var reader = await cmd.ExecuteReaderAsync();
                while (reader.Read())
                {
                    MainProductSearchResults.Add(new Item
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("ItemId")),
                        Barcode = reader.IsDBNull(reader.GetOrdinal("Barcode")) ? null : reader.GetString(reader.GetOrdinal("Barcode")),
                        Description = reader.GetString(reader.GetOrdinal("Description")),
                        StockQuantity = reader.GetDouble(reader.GetOrdinal("StockQuantity"))
                    });
                }

                IsMainResultsOpen = MainProductSearchResults.Count > 0;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Live main search failed", ex);
            }
        }

        private void ExecuteSelectMainProduct(Item? item)
        {
            if (item == null) return;
            
            FoundItem = item;
            // Clear search bar silently to keep filtered list
            _searchBarcode = string.Empty;
            OnPropertyChanged(nameof(SearchBarcode));
            IsMainResultsOpen = false;
            _ = LoadSupplyHistory();
        }

        private void ExecuteSelectProduct(Item? item)
        {
            if (item == null) return;
            
            FoundItem = item;
            ManualSearchBarcode = item.Description; 
            IsSearchResultsOpen = false;
            StatusMessage = "Item selected: " + item.Description;
            _ = LoadSupplyHistory();
        }

        private async Task LoadSupplyHistory()
        {
            if (FoundItem == null) return;
            try
            {
                var history = await _stockService.GetSupplyHistoryAsync(FoundItem.Barcode ?? "");
                SupplyHistory.Clear();
                foreach (var entry in history) SupplyHistory.Add(entry);
                StatusMessage = $"Found {history.Count} stock entries.";
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load stock history", ex);
                StatusMessage = "Error loading history.";
                ShowPopupError("Error loading history.");
            }
        }

        private void ExecuteSelectImage()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files (*.jpg, *.jpeg, *.png)|*.jpg;*.jpeg;*.png",
                Title = "Select Stock Bill Image"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TempImagePath = openFileDialog.FileName;
                StatusMessage = "Image selected: " + Path.GetFileName(TempImagePath);
                LoadFormImagePreview(TempImagePath);
            }
        }

        private void LoadFormImagePreview(string path)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                FormImagePreview = bitmap;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error loading form image preview", ex);
                FormImagePreview = null;
            }
        }

        private async Task ExecuteSaveSupply()
        {
            if (FoundItem == null) return;
            try
            {
                var entry = new Stock
                {
                    ProductId = FoundItem.Barcode ?? "",
                    Quantity = FormQuantity
                };

                if (IsEditing && SelectedEntry != null)
                {
                    entry.Id = SelectedEntry.Id;
                    await _stockService.UpdateSupplyAsync(entry, TempImagePath);
                    StatusMessage = "Stock updated successfully!";
                }
                else
                {
                    await _stockService.RegisterSupplyAsync(entry, TempImagePath);
                    StatusMessage = "Stock registered successfully!";
                }
                
                IsAddingNew = false;
                
                // Refresh logic: If searching, refresh that item. If not, refresh all recent.
                if (string.IsNullOrWhiteSpace(SearchBarcode))
                    await LoadAllRecentSupplies();
                else
                    await ExecuteSearchItem(); 

                ClearForm();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to save stock", ex);
                ShowPopupError("Error registering stock: " + ex.Message);
            }
        }

        private void ExecuteViewFullBill()
        {
            if (SelectedEntry == null || string.IsNullOrEmpty(SelectedEntry.ImagePath)) return;
            
            try
            {
                if (File.Exists(SelectedEntry.ImagePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = SelectedEntry.ImagePath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    ShowPopupError("Bill image file not found on disk.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to open bill image", ex);
                ShowPopupError("Error opening bill image: " + ex.Message);
            }
        }

        private void ClearForm()
        {
            FormQuantity = 0;
            TempImagePath = string.Empty;
            FormImagePreview = null;
        }

        private void LoadImagePreview(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                BillImagePreview = null;
                IsImageAvailable = false;
                if (!string.IsNullOrEmpty(path)) { StatusMessage = "Image file missing."; ShowPopupError("Image file missing."); }
                return;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                
                BillImagePreview = bitmap;
                IsImageAvailable = true;
                StatusMessage = "Previewing stock bill image.";
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Error loading preview for {path}", ex);
                BillImagePreview = null;
                IsImageAvailable = false;
                StatusMessage = "Error loading image preview.";
                ShowPopupError("Error loading image preview.");
            }
        }

        // ────────────────────────────────────────────
        //  CART METHODS
        // ────────────────────────────────────────────

        private async Task ExecuteCartSearch()
        {
            if (string.IsNullOrWhiteSpace(CartSearchText)) return;
            try
            {
                var results = await Task.Run(() =>
                    new Data.Repositories.ItemRepository().Search(CartSearchText));
                Dispatch(() =>
                {
                    CartSearchResults.Clear();
                    foreach (var r in results.Take(10)) CartSearchResults.Add(r);
                    IsCartSearchOpen = CartSearchResults.Count > 0;
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error("Cart search failed", ex);
            }
        }

        private void ExecuteAddSelectedFromListToCart()
        {
            if (SelectedProductFromList == null) return;
            ExecuteAddItemToCart(SelectedProductFromList);
            SelectedProductFromList = null; // Clear after adding
            OnPropertyChanged(nameof(SelectedProductFromList));
        }

        private void ExecuteAddItemToCart(Item? item)
        {
            if (item == null) return;

            // If already in cart, just increment quantity
            var existing = CartItems.FirstOrDefault(ci => ci.ItemId == item.Id);
            if (existing != null)
            {
                existing.Quantity++;
            }
            else
            {
                CartItems.Add(new StockPurchaseItem
                {
                    ItemId          = item.Id,
                    ItemDescription = item.Description,
                    Barcode         = item.Barcode,
                    Quantity        = 1,
                    CostPrice       = item.CostPrice
                });
            }

            RecalculateCartTotal();
            (ClearCartCommand    as RelayCommand)?.RaiseCanExecuteChanged();
            (CheckoutCartCommand as RelayCommand)?.RaiseCanExecuteChanged();

            // Clear search box after selection
            _cartSearchText = string.Empty;
            OnPropertyChanged(nameof(CartSearchText));
            IsCartSearchOpen = false;
            _cartSelectedResult = null;
            OnPropertyChanged(nameof(CartSelectedResult));

            CartStatus = string.Empty;
        }

        private void RecalculateCartTotal()
        {
            CartTotal = Math.Round(CartItems.Sum(ci => ci.LineTotal), 2);
            CartTotalText = CartTotal.ToString("F2");
            (CheckoutCartCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void ExecuteRemoveFromCart(StockPurchaseItem? item)
        {
            if (item == null) return;
            CartItems.Remove(item);
            RecalculateCartTotal();
            (ClearCartCommand    as RelayCommand)?.RaiseCanExecuteChanged();
            (CheckoutCartCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void ExecuteClearCart()
        {
            CartItems.Clear();
            CartTotal = 0;
            CartStatus = string.Empty;
            CartTempImagePath = string.Empty;
            CartImagePreview = null;
            (ClearCartCommand    as RelayCommand)?.RaiseCanExecuteChanged();
            (CheckoutCartCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private async Task ExecuteCheckoutCartAsync()
        {
            if (CartItems.Count == 0)
            {
                CartStatus = "Cart is empty. Add at least one product.";
                return;
            }

            // Validate each line: qty > 0 and cost > 0
            foreach (var ci in CartItems)
            {
                if (ci.Quantity <= 0)
                {
                    CartStatus = $"'{ci.ItemDescription}' has an invalid quantity (must be > 0).";
                    return;
                }
                if (ci.CostPrice <= 0)
                {
                    CartStatus = $"'{ci.ItemDescription}' has an invalid cost price (must be > 0).";
                    return;
                }
            }

            IsCartBusy = true;
            CartStatus = "Saving purchase…";

            try
            {
                if (string.IsNullOrEmpty(CartTempImagePath))
                {
                    CartStatus = "⚠️ Bill image is mandatory to complete purchase.";
                    return;
                }

                var purchase = new StockPurchase
                {
                    Items = CartItems.ToList(),
                    TotalAmount = CartTotal
                };

                var saved = await _stockService.RegisterPurchaseAsync(purchase, CartTempImagePath);

                Dispatch(() =>
                {
                    string msg = $"✓ Purchase #{saved.PurchaseId} saved — {saved.Items.Count} item(s), Rs. {saved.TotalAmount:N2} deducted from Cash in Drawer.";
                    CartStatus = msg;
                    ExecuteClearCart();
                    IsAddingNew = false;
                    ShowPopupSuccess(msg);
                    _ = LoadAllRecentSupplies();
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error("Stock cart checkout failed", ex);
                CartStatus = $"Error: {ex.Message}";
            }
            finally
            {
                IsCartBusy = false;
            }
        }
        private void ExecuteSelectCartImage()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files (*.jpg, *.jpeg, *.png)|*.jpg;*.jpeg;*.png",
                Title = "Select Supplier Bill Image"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                CartTempImagePath = openFileDialog.FileName;
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(CartTempImagePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    CartImagePreview = bitmap;
                    CartStatus = string.Empty;
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Error loading cart image preview", ex);
                    CartImagePreview = null;
                    CartStatus = "Error loading image.";
                }
            }
        }
    }
}
