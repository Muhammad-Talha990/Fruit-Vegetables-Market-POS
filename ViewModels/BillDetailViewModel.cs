using System.Windows;
using GroceryPOS.Models;

namespace GroceryPOS.ViewModels
{
    /// <summary>
    /// Simple ViewModel for the BillDetailWindow popup.
    /// Wraps a fully-loaded Bill object and exposes UI helper properties.
    /// </summary>
    public class BillDetailViewModel : BaseViewModel
    {
        public Bill Bill { get; }

        public string CustomerName => Bill.Customer?.FullName ?? "Walk-in Customer";
        public string CustomerPhone => Bill.Customer?.Phone ?? "—";

        public bool HasPaymentLogs => Bill.PaymentLogs.Count > 0;
        public bool HasReturnLogs => Bill.ReturnLogs.Count > 0;

        public BillDetailViewModel(Bill bill)
        {
            Bill = bill;
        }
    }
}
