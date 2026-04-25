using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using GroceryPOS.ViewModels;

namespace GroceryPOS.Models
{
    public class BillingTab : BaseViewModel
    {
        private string _tabName = string.Empty;
        public string TabName
        {
            get => _tabName;
            set => SetProperty(ref _tabName, value);
        }

        private string _invoiceNumber = "00000";
        public string InvoiceNumber
        {
            get => _invoiceNumber;
            set => SetProperty(ref _invoiceNumber, value);
        }

        private int? _customerId;
        public int? CustomerId
        {
            get => _customerId;
            set => SetProperty(ref _customerId, value);
        }

        private Customer? _customer;
        public Customer? Customer
        {
            get => _customer;
            set => SetProperty(ref _customer, value);
        }

        private string _customerSearchQuery = string.Empty;
        public string CustomerSearchQuery
        {
            get => _customerSearchQuery;
            set => SetProperty(ref _customerSearchQuery, value);
        }

        public ObservableCollection<Customer> CustomerSearchResults { get; set; } = new();
        
        private Customer? _selectedSearchResult;
        public Customer? SelectedSearchResult 
        { 
            get => _selectedSearchResult; 
            set => SetProperty(ref _selectedSearchResult, value); 
        }

        public ObservableCollection<Bill> CustomerBills { get; set; } = new();

        public ObservableCollection<CartItem> CartItems { get; set; } = new();

        private string _discountText = "0";
        public string DiscountText
        {
            get => _discountText;
            set => SetProperty(ref _discountText, value);
        }

        private string _taxText = "0";
        public string TaxText
        {
            get => _taxText;
            set => SetProperty(ref _taxText, value);
        }

        private string _cashReceivedText = "0";
        public string CashReceivedText
        {
            get => _cashReceivedText;
            set => SetProperty(ref _cashReceivedText, value);
        }

        // Helper to check if this tab is the active one (managed by ViewModel)
        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }
        private double _pendingCreditAmount;
        public double PendingCreditAmount
        {
            get => _pendingCreditAmount;
            set => SetProperty(ref _pendingCreditAmount, value);
        }

        private Bill? _previewHistoryBill;
        public Bill? PreviewHistoryBill
        {
            get => _previewHistoryBill;
            set => SetProperty(ref _previewHistoryBill, value);
        }

        private bool _isHistoryPaymentOpen;
        public bool IsHistoryPaymentOpen
        {
            get => _isHistoryPaymentOpen;
            set => SetProperty(ref _isHistoryPaymentOpen, value);
        }

        private string _historyPaymentAmount = string.Empty;
        public string HistoryPaymentAmount
        {
            get => _historyPaymentAmount;
            set => SetProperty(ref _historyPaymentAmount, value);
        }

        private string _historyPaymentNote = string.Empty;
        public string HistoryPaymentNote
        {
            get => _historyPaymentNote;
            set => SetProperty(ref _historyPaymentNote, value);
        }

        private string _historyPaymentError = string.Empty;
        public string HistoryPaymentError
        {
            get => _historyPaymentError;
            set => SetProperty(ref _historyPaymentError, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }
        
        private bool _isBillDetailOpen;
        public bool IsBillDetailOpen
        {
            get => _isBillDetailOpen;
            set => SetProperty(ref _isBillDetailOpen, value);
        }

        // ── Payment Method for History Payment ──
        private string _selectedHistoryPaymentMethod = "Cash";
        public string SelectedHistoryPaymentMethod
        {
            get => _selectedHistoryPaymentMethod;
            set => SetProperty(ref _selectedHistoryPaymentMethod, value);
        }

        private Account? _selectedHistoryAccount;
        public Account? SelectedHistoryAccount
        {
            get => _selectedHistoryAccount;
            set => SetProperty(ref _selectedHistoryAccount, value);
        }

        private string? _selectedHistoryOnlineMethod;
        public string? SelectedHistoryOnlineMethod
        {
            get => _selectedHistoryOnlineMethod;
            set => SetProperty(ref _selectedHistoryOnlineMethod, value);
        }

        public int? LoadedHistoryBillId { get; set; }
    }
}
