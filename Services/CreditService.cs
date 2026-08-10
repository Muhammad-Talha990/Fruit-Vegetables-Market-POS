using System;
using System.Collections.Generic;
using System.Linq;
using FruitVegetableMarketPOS.Data.Repositories;
using FruitVegetableMarketPOS.Models;
using FruitVegetableMarketPOS.Helpers;

namespace FruitVegetableMarketPOS.Services
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

        public CreditService(CreditPaymentRepository creditRepo, BillRepository billRepo, CustomerRepository customerRepo)
        {
            _creditRepo   = creditRepo;
            _billRepo     = billRepo;
            _customerRepo = customerRepo;
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
        /// Purchased = sum of bill amounts; Paid = sum paid; Pending = sum remaining.
        /// </summary>
        public (double TotalPurchased, double TotalPaid, double TotalPending) GetPendingSummary(int customerId)
        {
            var ledger = GetLedger(customerId);
            double totalPurchased = ledger.Sum(b => b.GrandTotal);
            double totalPaid = ledger.Sum(b => b.PaidAmount);
            double totalPending = ledger.Sum(b => b.RemainingAmount);
            return (Math.Round(totalPurchased, 2), Math.Round(totalPaid, 2), Math.Round(totalPending, 2));
        }

        /// <summary>
        /// Pays customer dues FIFO by bill date: apply cash to oldest pending bills first.
        /// Excess cash becomes change; leftover unpaid dues remain pending.
        /// </summary>
        public DuesPaymentResult RecordDuesPayment(
            int customerId,
            double cashReceived,
            string? note = null,
            string paymentMethod = "Cash")
        {
            if (cashReceived <= 0)
                throw new ArgumentException("Cash received must be greater than zero.");

            cashReceived = Math.Round(cashReceived, 2);

            var pendingBills = _billRepo.GetBillsByCustomerId(customerId)
                .Where(b => b.HasPendingCredit)
                .OrderBy(b => b.CreatedAt)
                .ThenBy(b => b.BillId)
                .ToList();

            if (pendingBills.Count == 0)
                throw new InvalidOperationException("This customer has no pending dues.");

            double cashLeft = cashReceived;
            double applied = 0;
            var allocations = new List<DuesPaymentAllocation>();

            foreach (var bill in pendingBills)
            {
                if (cashLeft <= 0.001)
                    break;

                // Always re-read so RemainingAmount is current
                var fresh = _billRepo.GetById(bill.BillId);
                if (fresh == null || !fresh.HasPendingCredit)
                    continue;

                double pay = Math.Min(cashLeft, fresh.RemainingAmount);
                pay = Math.Round(pay, 2);
                if (pay <= 0)
                    continue;

                string payNote = string.IsNullOrWhiteSpace(note)
                    ? "Pay Dues (FIFO)"
                    : note.Trim();

                RecordPayment(fresh.BillId, pay, payNote, paymentMethod);

                var after = _billRepo.GetById(fresh.BillId);
                allocations.Add(new DuesPaymentAllocation
                {
                    BillId = fresh.BillId,
                    InvoiceNumber = fresh.InvoiceNumber,
                    AmountPaid = pay,
                    RemainingAfter = after?.RemainingAmount ?? Math.Round(fresh.RemainingAmount - pay, 2)
                });

                cashLeft = Math.Round(cashLeft - pay, 2);
                applied = Math.Round(applied + pay, 2);
            }

            if (applied <= 0)
                throw new InvalidOperationException("No dues could be paid with the given amount.");

            double remainingPending = Math.Round(
                _billRepo.GetBillsByCustomerId(customerId).Sum(b => b.RemainingAmount), 2);
            double changeGiven = Math.Max(0, Math.Round(cashReceived - applied, 2));

            return new DuesPaymentResult
            {
                CashReceived = cashReceived,
                AppliedAmount = applied,
                ChangeGiven = changeGiven,
                RemainingPending = remainingPending,
                Allocations = allocations
            };
        }

        // ────────────────────────────────────────────
        //  PAYMENT RECORDING
        // ────────────────────────────────────────────

        /// <summary>
        /// Records a payment against a specific credit bill.
        /// Validates: amount > 0, not exceed remaining balance.
        /// </summary>
        public Bill RecordPayment(int billId, double amount, string? note = null, string paymentMethod = "Cash")
        {
            if (amount <= 0)
                throw new ArgumentException("Payment amount must be greater than zero.");

            var bill = _billRepo.GetById(billId)
                ?? throw new InvalidOperationException($"Bill #{billId} not found.");

            if (bill.RemainingAmount <= 0)
                throw new InvalidOperationException("This bill is already fully paid.");

            if (amount > bill.RemainingAmount + 0.001)
                throw new InvalidOperationException(
                    $"Payment amount (Rs. {amount:N2}) exceeds remaining balance (Rs. {bill.RemainingAmount:N2}). Overpayment is not allowed.");

            var transactionTime = DateTimeHelper.CaptureTransactionTime();

            var payment = new CreditPayment
            {
                BillId = billId,
                AmountPaid = Math.Round(amount, 2),
                Note = note,
                PaymentMethod = paymentMethod
            };

            _creditRepo.RecordPayment(payment, transactionTime);

            return _billRepo.GetById(billId) ?? throw new InvalidOperationException("Bill lost after payment.");
        }

        // ────────────────────────────────────────────
        //  PAYMENT HISTORY
        // ────────────────────────────────────────────

        /// <summary>Returns all payment installments for a specific bill.</summary>
        public List<CreditPayment> GetPaymentHistory(int billId) =>
            _creditRepo.GetPaymentsForBill(billId);
    }

    /// <summary>Result of a FIFO multi-bill dues payment.</summary>
    public class DuesPaymentResult
    {
        public double CashReceived { get; set; }
        public double AppliedAmount { get; set; }
        public double ChangeGiven { get; set; }
        public double RemainingPending { get; set; }
        public List<DuesPaymentAllocation> Allocations { get; set; } = new();
        public bool IsFullyCleared => RemainingPending <= 0.01;
    }

    public class DuesPaymentAllocation
    {
        public int BillId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public double AmountPaid { get; set; }
        public double RemainingAfter { get; set; }
    }
}
