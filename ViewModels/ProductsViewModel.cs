using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Models;
using FruitVegetableMarketPOS.Services;

namespace FruitVegetableMarketPOS.ViewModels
{
    /// <summary>
    /// Item catalog: English/Urdu name + category only.
    /// Prices and types are set on Billing → Add Today.
    /// </summary>
    public class ProductsViewModel : BaseViewModel
    {
        private readonly ItemService _itemService;
        private readonly ItemTypeService _itemTypeService;
        private readonly CategoryService _categoryService;

        public ObservableCollection<Item> Products { get; set; } = new();
        public ObservableCollection<string> Categories { get; set; } = new();
        public ObservableCollection<Category> CategoryList { get; } = new();
        public ObservableCollection<PosCategoryChip> CategoryFilters { get; } = new();

        private Category? _selectedCategoryFilter;
        public Category? SelectedCategoryFilter
        {
            get => _selectedCategoryFilter;
            set
            {
                if (SetProperty(ref _selectedCategoryFilter, value))
                    SearchProducts();
            }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { SetProperty(ref _searchText, value); SearchProducts(); }
        }

        private Item? _selectedProduct;
        public Item? SelectedProduct
        {
            get => _selectedProduct;
            set
            {
                if (SetProperty(ref _selectedProduct, value))
                    LoadProductToForm(value);
            }
        }

        private string _formName = string.Empty;
        public string FormName { get => _formName; set => SetProperty(ref _formName, value); }

        private string _formNameUrdu = string.Empty;
        public string FormNameUrdu { get => _formNameUrdu; set => SetProperty(ref _formNameUrdu, value); }

        private string _formCategory = string.Empty;
        public string FormCategory { get => _formCategory; set => SetProperty(ref _formCategory, value); }

        private int? _formCategoryId;
        public int? FormCategoryId
        {
            get => _formCategoryId;
            set => SetProperty(ref _formCategoryId, value);
        }

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set => SetProperty(ref _isEditing, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private string? _originalBarcode;

        public ICommand AddCommand { get; }
        public ICommand UpdateCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand ClearFormCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SelectCategoryFilterCommand { get; }

        public ProductsViewModel(
            ItemService itemService,
            ItemTypeService itemTypeService,
            CategoryService categoryService)
        {
            _itemService = itemService;
            _itemTypeService = itemTypeService;
            _categoryService = categoryService;

            AddCommand = new RelayCommand(AddProduct);
            UpdateCommand = new RelayCommand(UpdateProduct);
            DeleteCommand = new RelayCommand(DeleteProduct);
            ClearFormCommand = new RelayCommand(ClearForm);
            RefreshCommand = new RelayCommand(ExecuteRefreshProducts);
            SelectCategoryFilterCommand = new RelayCommand(obj =>
            {
                if (obj is not PosCategoryChip chip) return;
                SelectedCategoryFilter = chip.Category;
                foreach (var c in CategoryFilters)
                    c.IsSelected = ReferenceEquals(c, chip);
            });

            LoadCategories();
            LoadProducts();
        }

        public void OnActivated()
        {
            LoadCategories();
            LoadProducts();
        }

        private void LoadCategories()
        {
            Categories.Clear();
            CategoryList.Clear();
            CategoryFilters.Clear();

            CategoryFilters.Add(new PosCategoryChip { Label = "All (تمام)", Category = null, IsSelected = SelectedCategoryFilter == null });

            foreach (var cat in _categoryService.GetAllActive()
                         .Where(c => c.Name is "Fruits" or "Vegetables")
                         .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name))
            {
                CategoryList.Add(cat);
                if (!Categories.Contains(cat.Name))
                    Categories.Add(cat.Name);
                CategoryFilters.Add(new PosCategoryChip
                {
                    Label = cat.ChipLabel,
                    Category = cat,
                    IsSelected = SelectedCategoryFilter?.CategoryId == cat.CategoryId
                });
            }
        }

        private void LoadProducts()
        {
            try
            {
                IEnumerable<Item> products = string.IsNullOrWhiteSpace(SearchText)
                    ? _itemService.GetActiveItems()
                    : _itemService.SearchItems(SearchText);

                if (SelectedCategoryFilter != null)
                    products = products.Where(p => p.CategoryId == SelectedCategoryFilter.CategoryId);

                products = products
                    .OrderBy(p => int.TryParse(p.PosCode, out var n) ? n : int.MaxValue)
                    .ThenBy(p => p.Description);

                Dispatch(() =>
                {
                    Products.Clear();
                    foreach (var p in products)
                        Products.Add(p);
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error("LoadProducts failed", ex);
            }
        }

        private void ExecuteRefreshProducts()
        {
            SearchText = string.Empty;
        }

        private void ClearForm()
        {
            IsEditing = false;
            if (_selectedProduct != null)
            {
                _selectedProduct = null;
                OnPropertyChanged(nameof(SelectedProduct));
            }
            _originalBarcode = null;
            FormName = string.Empty;
            FormNameUrdu = string.Empty;
            FormCategory = string.Empty;
            FormCategoryId = null;
            StatusMessage = "Form cleared.";
        }

        private void SearchProducts() => LoadProducts();

        private void LoadProductToForm(Item? item)
        {
            if (item == null)
            {
                IsEditing = false;
                _originalBarcode = null;
                FormName = string.Empty;
                FormNameUrdu = string.Empty;
                FormCategory = string.Empty;
                FormCategoryId = null;
                return;
            }

            IsEditing = true;
            _originalBarcode = item.Barcode;
            FormName = item.Description;
            FormNameUrdu = item.NameUrdu ?? string.Empty;
            FormCategory = item.ItemCategory ?? string.Empty;
            FormCategoryId = item.CategoryId;
        }

        private Item BuildItemFromForm(int id)
        {
            var category = CategoryList.FirstOrDefault(c =>
                c.CategoryId == FormCategoryId ||
                c.Name.Equals(FormCategory?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase));

            // Catalog only — unit prices/types live on Billing → Add Today (ItemTypes).
            var existing = SelectedProduct;

            return new Item
            {
                Id = id,
                Barcode = existing?.Barcode ?? _originalBarcode,
                Description = FormName.Trim(),
                NameUrdu = string.IsNullOrWhiteSpace(FormNameUrdu) ? null : FormNameUrdu.Trim(),
                CategoryId = category?.CategoryId ?? FormCategoryId,
                ItemCategory = category?.Name
                               ?? (string.IsNullOrWhiteSpace(FormCategory) ? null : FormCategory.Trim()),
                IsActive = true
            };
        }

        private string AllocateNextPosCode()
        {
            var max = _itemService.GetActiveItems()
                .Select(i => int.TryParse(i.PosCode, out var n) ? n : 0)
                .DefaultIfEmpty(0)
                .Max();
            return (max + 1).ToString();
        }

        private void AddProduct()
        {
            try
            {
                if (!ValidateForm()) return;

                var item = BuildItemFromForm(0);
                item.Barcode = AllocateNextPosCode();

                _itemService.AddItem(item);

                // Default Type 1 so the item can be added on Billing; price set there.
                _itemTypeService.ReplaceWithNumberedTypes(item.Id, new[] { 0.0 });

                StatusMessage = $"✓ Item '{item.Description}' added (#{item.Barcode}). Set price on Billing.";
                ShowPopupSuccess($"'{item.Description}' added successfully!");

                ClearForm();
                LoadProducts();
                LoadCategories();
                CatalogEvents.NotifyChanged();
            }
            catch (Exception ex)
            {
                var errorMsg = ex.InnerException?.Message ?? ex.Message;
                StatusMessage = $"✗ Error: {errorMsg}";
                ShowPopupError(errorMsg);
                AppLogger.Error("Add item failed", ex);
            }
        }

        private void UpdateProduct()
        {
            try
            {
                if (SelectedProduct == null) { ShowPopupError("Please select an item to update."); return; }
                if (!ValidateForm()) return;

                var item = BuildItemFromForm(SelectedProduct.Id);
                _itemService.UpdateItem(item, _originalBarcode);

                StatusMessage = $"✓ '{FormName}' name updated!";
                ShowPopupSuccess($"'{FormName}' updated successfully!");
                ClearForm();
                LoadProducts();
                LoadCategories();
                CatalogEvents.NotifyChanged();
            }
            catch (Exception ex)
            {
                var errorMsg = ex.InnerException?.Message ?? ex.Message;
                StatusMessage = $"✗ Error: {errorMsg}";
                ShowPopupError(errorMsg);
                AppLogger.Error("Update item failed", ex);
            }
        }

        private void DeleteProduct()
        {
            if (SelectedProduct == null) { ShowPopupError("Please select an item to delete."); return; }

            int itemId = SelectedProduct.Id;
            string description = SelectedProduct.Description;

            var result = MessageBox.Show(
                $"Are you sure you want to delete '{description}'?\n\nThis deactivates the item; it is not removed from bill history.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                _itemService.DeleteItem(itemId);
                StatusMessage = $"✓ Item '{description}' deleted.";
                ClearForm();
                LoadProducts();
                LoadCategories();
                CatalogEvents.NotifyChanged();
            }
            catch (Exception ex)
            {
                ShowPopupError(ex.Message);
                AppLogger.Error("Delete item failed", ex);
            }
        }

        private bool ValidateForm()
        {
            if (string.IsNullOrWhiteSpace(FormName))
            { ShowPopupError("English product name is required."); return false; }
            if (string.IsNullOrWhiteSpace(FormNameUrdu))
            { ShowPopupError("Urdu name (نام اردو) is required."); return false; }
            if (FormCategoryId == null && string.IsNullOrWhiteSpace(FormCategory))
            { ShowPopupError("Please select a category."); return false; }

            return true;
        }
    }
}
