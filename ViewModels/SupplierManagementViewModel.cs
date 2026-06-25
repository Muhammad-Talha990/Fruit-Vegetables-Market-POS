using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using GroceryPOS.Models;
using GroceryPOS.Services;

namespace GroceryPOS.ViewModels
{
    public class SupplierManagementViewModel : BaseViewModel
    {
        private readonly SupplierService _supplierService;
        private readonly ItemService _itemService;

        public ObservableCollection<Supplier> Suppliers { get; } = new();
        public ObservableCollection<SupplierProduct> AssignedProducts { get; } = new();
        public ObservableCollection<Item> AvailableItems { get; } = new();

        private Supplier? _selectedSupplier;
        public Supplier? SelectedSupplier
        {
            get => _selectedSupplier;
            set
            {
                if (SetProperty(ref _selectedSupplier, value))
                {
                    LoadAssignedProducts();
                    if (value != null)
                    {
                        EditPhoneNumber = value.PhoneNumber;
                        EditName = value.Name;
                        EditCompany = value.CompanyName;
                        EditEmail = value.Email;
                        EditAddress = value.Address;
                        IsEditMode = true;
                    }
                    else
                    {
                        ResetForm();
                    }
                }
            }
        }

        #region Form Properties
        private string _editPhoneNumber = string.Empty;
        public string EditPhoneNumber { get => _editPhoneNumber; set => SetProperty(ref _editPhoneNumber, value); }

        private string _editName = string.Empty;
        public string EditName { get => _editName; set => SetProperty(ref _editName, value); }

        private string? _editCompany;
        public string? EditCompany { get => _editCompany; set => SetProperty(ref _editCompany, value); }

        private string? _editEmail;
        public string? EditEmail { get => _editEmail; set => SetProperty(ref _editEmail, value); }

        private string? _editAddress;
        public string? EditAddress { get => _editAddress; set => SetProperty(ref _editAddress, value); }

        private bool _isEditMode;
        public bool IsEditMode { get => _isEditMode; set => SetProperty(ref _isEditMode, value); }

        private string? _errorMessage;
        public string? ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
        #endregion

        #region Mapping Properties
        private Item? _selectedItemToAssign;
        public Item? SelectedItemToAssign { get => _selectedItemToAssign; set => SetProperty(ref _selectedItemToAssign, value); }

        private double? _newSupplyPrice;
        public double? NewSupplyPrice { get => _newSupplyPrice; set => SetProperty(ref _newSupplyPrice, value); }
        #endregion

        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand AssignProductCommand { get; }
        public ICommand UnassignProductCommand { get; }

        public SupplierManagementViewModel(SupplierService supplierService, ItemService itemService)
        {
            _supplierService = supplierService;
            _itemService = itemService;

            SaveCommand = new RelayCommand(async _ => await ExecuteSave(), _ => CanSave());
            DeleteCommand = new RelayCommand(async _ => await ExecuteDelete(), _ => SelectedSupplier != null);
            ResetCommand = new RelayCommand(_ => ResetForm());
            AssignProductCommand = new RelayCommand(async _ => await ExecuteAssign(), _ => SelectedSupplier != null && SelectedItemToAssign != null);
            UnassignProductCommand = new RelayCommand(async p => await ExecuteUnassign(p), _ => SelectedSupplier != null);

            LoadSuppliers();
            LoadItems();
        }

        private void LoadSuppliers()
        {
            Task.Run(async () =>
            {
                var list = await _supplierService.GetAllSuppliersAsync();
                App.Current.Dispatcher.Invoke(() =>
                {
                    Suppliers.Clear();
                    foreach (var s in list) Suppliers.Add(s);
                });
            });
        }

        private void LoadItems()
        {
            Task.Run(async () =>
            {
                var list = _itemService.GetAllItems();
                App.Current.Dispatcher.Invoke(() =>
                {
                    AvailableItems.Clear();
                    foreach (var i in list) AvailableItems.Add(i);
                });
            });
        }

        private void LoadAssignedProducts()
        {
            AssignedProducts.Clear();
            if (SelectedSupplier == null) return;

            Task.Run(async () =>
            {
                var list = await _supplierService.GetProductsBySupplierAsync(SelectedSupplier.PhoneNumber);
                App.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var sp in list) AssignedProducts.Add(sp);
                });
            });
        }

        private bool CanSave()
        {
            // Just basic null checks for the command to be active, 
            // the actual validation will happen in ExecuteSave to show popups.
            return !string.IsNullOrWhiteSpace(EditName) && !string.IsNullOrWhiteSpace(EditPhoneNumber);
        }

        private async Task ExecuteSave()
        {
            // Validate Phone Number: Must be numeric, 11 digits, and start with 0
            bool isNumeric = EditPhoneNumber.All(char.IsDigit);
            bool isCorrectLength = EditPhoneNumber.Length == 11;
            bool startsWithZero = EditPhoneNumber.StartsWith("0");

            if (!isNumeric || !isCorrectLength || !startsWithZero)
            {
                string msg = "Invalid Phone Number!\n\n";
                if (!startsWithZero) msg += "- Must start with '0'\n";
                if (!isCorrectLength) msg += "- Must be exactly 11 digits\n";
                if (!isNumeric) msg += "- Must contain only numbers\n";

                MessageBox.Show(msg, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Validate Email: If provided, must end with @gmail.com
            if (!string.IsNullOrWhiteSpace(EditEmail))
            {
                if (!EditEmail.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Invalid Email Format!\n\nEmail must end with @gmail.com", 
                                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var supplier = new Supplier
            {
                PhoneNumber = EditPhoneNumber,
                Name = EditName,
                CompanyName = EditCompany,
                Email = EditEmail,
                Address = EditAddress
            };

            bool success = await _supplierService.SaveSupplierAsync(supplier, IsEditMode);
            if (success)
            {
                LoadSuppliers();
                ResetForm();
            }
            else
            {
                MessageBox.Show("Failed to save supplier. This phone number might already be registered to another supplier.", 
                                "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ExecuteDelete()
        {
            if (SelectedSupplier == null) return;
            if (MessageBox.Show($"Are you sure you want to delete {SelectedSupplier.Name}?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.No) return;

            bool success = await _supplierService.DeleteSupplierAsync(SelectedSupplier.PhoneNumber);
            if (success)
            {
                LoadSuppliers();
                ResetForm();
            }
        }

        private async Task ExecuteAssign()
        {
            if (SelectedSupplier == null || SelectedItemToAssign == null) return;

            var mapping = new SupplierProduct
            {
                SupplierPhone = SelectedSupplier.PhoneNumber,
                ProductId = SelectedItemToAssign.Id,
                SupplyPrice = NewSupplyPrice,
                SupplyDate = DateTime.Now
            };

            bool success = await _supplierService.AssignProductAsync(mapping);
            if (success)
            {
                LoadAssignedProducts();
                SelectedItemToAssign = null;
                NewSupplyPrice = null;
            }
        }

        private async Task ExecuteUnassign(object? param)
        {
            if (SelectedSupplier == null || param is not SupplierProduct mapping) return;

            bool success = await _supplierService.UnassignProductAsync(SelectedSupplier.PhoneNumber, mapping.ProductId);
            if (success)
            {
                LoadAssignedProducts();
            }
        }

        private void ResetForm()
        {
            SelectedSupplier = null;
            EditPhoneNumber = string.Empty;
            EditName = string.Empty;
            EditCompany = null;
            EditEmail = null;
            EditAddress = null;
            IsEditMode = false;
        }
    }
}
