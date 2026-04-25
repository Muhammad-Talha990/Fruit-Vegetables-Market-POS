using System;
using System.Collections.Generic;
using System.Linq;
using GroceryPOS.Data.Repositories;
using GroceryPOS.Models;

namespace GroceryPOS.Services
{
    /// <summary>
    /// Business logic for the Store Credit system.
    /// Handles ledger queries, payment recording, and validation.
    /// </summary>
    public class CreditService
    {
        private readonly CreditPaymentRepository _creditRepo;
        private readonly BillRepository _billRepo;
        private readonly CustomerRepository _customerRepo;
        private readonly IStockService _stockService;

        public CreditService(CreditPaymentRepository creditRepo, BillRepository billRepo, CustomerRepository customerRepo, IStockService stockService)
        {
            _creditRepo   = creditRepo;
            _billRepo     = billRepo;
            _customerRepo = customerRepo;
            _stockService = stockService;
        }

        // ────────────────────────────────────────────
        //  LEDGER
        // ────────────────────────────────────────────

        /// <summary>Gets a single bill by ID with fresh data from DB.</summary>
        public Bill? GetBillById(int billId) => _billRepo.GetById(billId);

        /// <summary>
        /// Returns the full bill ledger for a customer (Sale bills only, all payment statuses).
        /// </summary>
        public List<Bill> GetLedger(int customerId) =>
            _billRepo.GetLedgerByCustomer(customerId);

        /// <summary>
        /// Returns outstanding (credit) bills for a customer.
        /// </summary>
        public List<Bill> GetPendingBills(int customerId) =>
            _billRepo.GetCreditBillsByCustomer(customerId);

        /// <summary>
        /// Returns summary totals for the ledger footer.
        /// </summary>
        public (double TotalCredit, double TotalPaid, double TotalPending) GetPendingSummary(int customerId)
        {
            var ledger = GetLedger(customerId);
            double totalCredit  = ledger.Sum(b => b.GrandTotal);
            double totalPending = ledger.Sum(b => b.RemainingAmount);
            double totalPaid    = totalCredit - totalPending;
            return (Math.Round(totalCredit, 2), Math.Round(totalPaid, 2), Math.Round(totalPending, 2));
        }

        // ────────────────────────────────────────────
        //  PAYMENT RECORDING
        // ────────────────────────────────────────────

        /// <summary>
        /// Records a payment against a specific credit bill.
        /// Validates: amount > 0, not exceed remaining balance.
        /// </summary>
        /// <param name="billId">The bill being paid.</param>
        /// <param name="amount">Payment amount (must be > 0 and ≤ remaining).</param>
        /// <param name="note">Optional cashier note.</param>
        /// <param name="paymentMethod">Payment method: 'Cash' (default) or 'Online'.</param>
        /// <returns>Updated bill state after payment.</returns>
        public Bill RecordPayment(int billId, double amount, string? note = null, string paymentMethod = "Cash")
        {
            if (amount <= 0)
                throw new ArgumentException("Payment amount must be greater than zero.");

            // Fetch current bill to check remaining
            var bill = _billRepo.GetById(billId)
                ?? throw new InvalidOperationException($"Bill #{billId} not found.");

            if (bill.RemainingAmount <= 0)
                throw new InvalidOperationException("This bill is already fully paid.");

            if (amount > bill.RemainingAmount + 0.001) // tolerance for floating point
                throw new InvalidOperationException(
                    $"Payment amount (Rs. {amount:N2}) exceeds remaining balance (Rs. {bill.RemainingAmount:N2}). Overpayment is not allowed.");

            var payment = new CreditPayment
            {
                BillId     = billId,
                AmountPaid = Math.Round(amount, 2),
                Note       = note,
                PaymentMethod = paymentMethod
            };

            _creditRepo.RecordPayment(payment);
            
            // Trigger global UI refresh (Dashboard metrics, Reports, etc.)
            _stockService.NotifyChanged();

            return _billRepo.GetById(billId) ?? throw new InvalidOperationException("Bill lost after payment.");
        }

        // ────────────────────────────────────────────
        //  PAYMENT HISTORY
        // ────────────────────────────────────────────

        /// <summary>Returns all payment installments for a specific bill.</summary>
        public List<CreditPayment> GetPaymentHistory(int billId) =>
            _creditRepo.GetPaymentsForBill(billId);
    }
}
