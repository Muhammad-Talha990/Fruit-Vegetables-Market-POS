using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GroceryPOS.Helpers;
using GroceryPOS.Models;
using GroceryPOS.Services;

namespace GroceryPOS.ViewModels
{
    /// <summary>
    /// ViewModel for the Customer Management screen.
    /// Supports viewing all customers (including inactive), searching, editing, and soft-deleting.
    /// </summary>
    public class CustomerManagementViewModel : BaseViewModel
    {
        private readonly CustomerService _customerService;
        private readonly IStockService _stockService;
        private readonly Action _onStockChangedHandler;

        // ── Collections ──
        public ObservableCollection<Customer> AllCustomers { get; } = new();
        public ObservableCollection<Customer> FilteredCustomers { get; } = new();

        // ── Search ──
        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                    ApplyFilter();
            }
        }

        private bool _showInactive;
        public bool ShowInactive
        {
            get => _showInactive;
            set
            {
                if (SetProperty(ref _showInactive, value))
                {
                    LoadCustomers();
                }
            }
        }

        // ── Selection ──
        private Customer? _selectedCustomer;
        public Customer? SelectedCustomer
        {
            get => _selectedCustomer;
            set => SetProperty(ref _selectedCustomer, value);
        }

        // ── Edit Dialog ──
        private bool _isEditDialogOpen;
        public bool IsEditDialogOpen
        {
            get => _isEditDialogOpen;
            set => SetProperty(ref _isEditDialogOpen, value);
        }

        // Editable fields in dialog
        private string _editFullName = string.Empty;
        public string EditFullName
        {
            get => _editFullName;
            set => SetProperty(ref _editFullName, value);
        }

        private string _editPhone = string.Empty;
        public string EditPhone
        {
            get => _editPhone;
            set => SetProperty(ref _editPhone, value);
        }

        private string _editPhone2 = string.Empty;
        public string EditPhone2
        {
            get => _editPhone2;
            set => SetProperty(ref _editPhone2, value);
        }

        private string _editAddress = string.Empty;
        public string EditAddress
        {
            get => _editAddress;
            set => SetProperty(ref _editAddress, value);
        }

        private string _editAddress2 = string.Empty;
        public string EditAddress2
        {
            get => _editAddress2;
            set => SetProperty(ref _editAddress2, value);
        }

        private string _editAddress3 = string.Empty;
        public string EditAddress3
        {
            get => _editAddress3;
            set => SetProperty(ref _editAddress3, value);
        }

        private string _editError = string.Empty;
        public string EditError
        {
            get => _editError;
            set => SetProperty(ref _editError, value);
        }

        private string _dialogTitle = "✏ Edit Customer";
        public string DialogTitle
        {
            get => _dialogTitle;
            set => SetProperty(ref _dialogTitle, value);
        }

        private int _editingCustomerId;

        // ── Status / Counts ──
        public int TotalCustomers => AllCustomers.Count;
        public int ActiveCustomers => AllCustomers.Count(c => c.IsActive);
        public string StatusMessage { get; private set; } = string.Empty;

        // ── Commands ──
        public ICommand RefreshCommand { get; }
        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand SaveEditCommand { get; }
        public ICommand CancelEditCommand { get; }
        public ICommand DeactivateCommand { get; }
        public ICommand ReactivateCommand { get; }

        /// <summary>Raised when the user wants to view a customer's ledger.</summary>
        public event Action<int>? ViewLedgerRequested;

        public ICommand ViewLedgerCommand { get; }

        public CustomerManagementViewModel(CustomerService customerService, IStockService stockService)
        {
            _customerService = customerService;
            _stockService = stockService;
            _onStockChangedHandler = OnStockChanged;

            // Real-time refresh when returns or payments are recorded
            _stockService.StockChanged += _onStockChangedHandler;

            RefreshCommand     = new RelayCommand(_ => LoadCustomers());
            AddCommand         = new RelayCommand(_ => OpenAddDialog());
            EditCommand        = new RelayCommand(obj => OpenEditDialog(obj as Customer));
            SaveEditCommand    = new RelayCommand(_ => SaveEdit());
            CancelEditCommand  = new RelayCommand(_ => CloseEditDialog());
            DeactivateCommand  = new RelayCommand(obj => DeactivateCustomer(obj as Customer));
            ReactivateCommand  = new RelayCommand(obj => ReactivateCustomer(obj as Customer));
            ViewLedgerCommand  = new RelayCommand(obj =>
            {
                if (obj is Customer c) ViewLedgerRequested?.Invoke(c.CustomerId);
            });

            LoadCustomers();
        }

        // ────────────────────────────────────────────
        //  LOAD & FILTER
        // ────────────────────────────────────────────

        public void LoadCustomers()
        {
            try
            {
                var list = _customerService.GetAllCustomers(activeOnly: !ShowInactive);
                AllCustomers.Clear();
                foreach (var c in list)
                    AllCustomers.Add(c);

                ApplyFilter();
                OnPropertyChanged(nameof(TotalCustomers));
                OnPropertyChanged(nameof(ActiveCustomers));
            }
            catch (Exception ex)
            {
                AppLogger.Error("CustomerManagementViewModel.LoadCustomers failed", ex);
                SetStatus("⚠ Failed to load customers.");
                ShowPopupError("Failed to load customers.");
            }
        }

        private void ApplyFilter()
        {
            string q = SearchQuery.Trim().ToLowerInvariant();
            FilteredCustomers.Clear();

            foreach (var c in AllCustomers)
            {
                if (string.IsNullOrEmpty(q)
                    || c.FullName.ToLowerInvariant().Contains(q)
                    || c.PrimaryPhone.Contains(q)
                    || (c.SecondaryPhone?.Contains(q) ?? false))
                {
                    FilteredCustomers.Add(c);
                }
            }
        }

        // ────────────────────────────────────────────
        //  EDIT DIALOG
        // ────────────────────────────────────────────

        private void OpenAddDialog()
        {
            _editingCustomerId = 0;
            EditFullName = string.Empty;
            EditPhone    = string.Empty;
            EditPhone2   = string.Empty;
            EditAddress  = string.Empty;
            EditAddress2 = string.Empty;
            EditAddress3 = string.Empty;
            EditError    = string.Empty;
            DialogTitle  = "➕ Add New Customer";
            IsEditDialogOpen = true;
        }

        private void OpenEditDialog(Customer? customer)
        {
            if (customer == null) return;
            _editingCustomerId = customer.CustomerId;
            EditFullName = customer.FullName;
            EditPhone    = customer.PrimaryPhone;
            EditPhone2   = customer.SecondaryPhone ?? string.Empty;
            EditAddress  = customer.Address ?? string.Empty;
            EditAddress2 = customer.Address2 ?? string.Empty;
            EditAddress3 = customer.Address3 ?? string.Empty;
            EditError    = string.Empty;
            DialogTitle  = "✏ Edit Customer";
            IsEditDialogOpen = true;
        }

        private void CloseEditDialog()
        {
            IsEditDialogOpen = false;
            EditError = string.Empty;
        }

        private void SaveEdit()
        {
            try
            {
                EditError = string.Empty;
                
                string fullName = EditFullName?.Trim() ?? string.Empty;
                string primaryPhone = EditPhone?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(fullName))
                {
                    EditError = "⚠ Customer Name is required.";
                    ShowPopupError("Customer Name is required.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(primaryPhone))
                {
                    EditError = "⚠ Primary Phone is required.";
                    ShowPopupError("Primary Phone is required.");
                    return;
                }

                var customer = new Customer
                {
                    CustomerId     = _editingCustomerId,
                    FullName       = fullName,
                    PrimaryPhone   = primaryPhone,
                    SecondaryPhone = string.IsNullOrWhiteSpace(EditPhone2) ? null : EditPhone2.Trim(),
                    Address        = string.IsNullOrWhiteSpace(EditAddress) ? null : EditAddress.Trim(),
                    Address2       = string.IsNullOrWhiteSpace(EditAddress2) ? null : EditAddress2.Trim(),
                    Address3       = string.IsNullOrWhiteSpace(EditAddress3) ? null : EditAddress3.Trim()
                };

                if (_editingCustomerId == 0)
                {
                    _customerService.RegisterCustomer(customer);
                    SetStatus($"✓ Customer '{customer.FullName}' registered successfully.");
                }
                else
                {
                    _customerService.UpdateCustomer(customer);
                    SetStatus($"✓ Customer '{customer.FullName}' updated successfully.");
                }

                CloseEditDialog();
                LoadCustomers();
            }
            catch (Exception ex)
            {
                EditError = ex.Message;
                ShowPopupError(ex.Message);
                AppLogger.Error("CustomerManagementViewModel.SaveEdit failed", ex);
            }
        }

        // ────────────────────────────────────────────
        //  SOFT DELETE / REACTIVATE
        // ────────────────────────────────────────────

        private void DeactivateCustomer(Customer? customer)
        {
            if (customer == null) return;

            var result = MessageBox.Show(
                $"Deactivate customer '{customer.FullName}'?\n\nThey will no longer appear in the billing search but their records will be preserved.",
                "Confirm Deactivation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                _customerService.DeactivateCustomer(customer.CustomerId);
                LoadCustomers();
                SetStatus($"✓ Customer '{customer.FullName}' deactivated.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("CustomerManagementViewModel.DeactivateCustomer failed", ex);
                ShowPopupError($"Could not deactivate customer: {ex.Message}");
            }
        }

        private void ReactivateCustomer(Customer? customer)
        {
            if (customer == null) return;

            try
            {
                _customerService.ReactivateCustomer(customer.CustomerId);
                LoadCustomers();
                SetStatus($"✓ Customer '{customer.FullName}' reactivated.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("CustomerManagementViewModel.ReactivateCustomer failed", ex);
                ShowPopupError($"Could not reactivate customer: {ex.Message}");
            }
        }

        // ────────────────────────────────────────────
        //  Helpers
        // ────────────────────────────────────────────

        private void SetStatus(string msg)
        {
            StatusMessage = msg;
            OnPropertyChanged(nameof(StatusMessage));
        }

        private void OnStockChanged()
        {
            Dispatch(LoadCustomers);
        }

        public override void Dispose()
        {
            if (_stockService != null)
                _stockService.StockChanged -= _onStockChangedHandler;
            base.Dispose();
        }
    }
}
