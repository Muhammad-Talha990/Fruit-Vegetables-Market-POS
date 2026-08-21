using System;
using System.Collections.Generic;
using System.Linq;
using FruitVegetableMarketPOS.Data.Repositories;
using FruitVegetableMarketPOS.Models;
using FruitVegetableMarketPOS.Helpers;

namespace FruitVegetableMarketPOS.Services
{
    /// <summary>
    /// Single payment module for store credit:
    ///   • Single-bill Pay Due  → RecordPayment (cash may exceed remaining → change)
    ///   • Pay Dues (all bills) → RecordDuesPayment (FIFO; same cash/change rules)
    /// Online payments never produce change and cannot exceed the amount due.
    /// </summary>
    public class CreditService
    {
        private readonly CreditPaymentRepository _creditRepo;
        private readonly BillRepository _billRepo;
        private readonly CustomerRepository _customerRepo;
        private readonly ItemRepository _itemRepo;

        public CreditService(
            CreditPaymentRepository creditRepo,
            BillRepository billRepo,
            CustomerRepository customerRepo,
            ItemRepository itemRepo)
        {
            _creditRepo   = creditRepo;
            _billRepo     = billRepo;
            _customerRepo = customerRepo;
            _itemRepo     = itemRepo;
        }

        // ────────────────────────────────────────────
        //  LEDGER
        // ────────────────────────────────────────────

        public Bill? GetBillById(int billId) => _billRepo.GetById(billId);

        public List<Bill> GetLedger(int customerId) =>
            _billRepo.GetLedgerByCustomer(customerId);

        public List<Bill> GetPendingBills(int customerId) =>
            _billRepo.GetCreditBillsByCustomer(customerId);

        public (double TotalPurchased, double TotalPaid, double TotalPending) GetPendingSummary(int customerId)
        {
            var ledger = GetLedger(customerId);
            double totalPurchased = ledger.Sum(b => b.GrandTotal);
            double totalPaid = ledger.Sum(b => b.PaidAmount);
            double totalPending = ledger.Sum(b => b.RemainingAmount);
            return (Math.Round(totalPurchased, 2), Math.Round(totalPaid, 2), Math.Round(totalPending, 2));
        }

        public static bool IsOnlineMethod(string? paymentMethod) =>
            !string.IsNullOrWhiteSpace(paymentMethod) &&
            !string.Equals(paymentMethod.Trim(), "Cash", StringComparison.OrdinalIgnoreCase);

        // ────────────────────────────────────────────
        //  UNIFIED SINGLE-BILL PAYMENT
        // ────────────────────────────────────────────

        /// <summary>
        /// Records payment against one bill — same rules as Pay Dues:
        /// Cash may exceed remaining (change given); Online cannot exceed remaining.
        /// Only the applied (capped) amount is written to the ledger.
        /// </summary>
        public BillPaymentResult RecordPayment(
            int billId,
            double cashReceived,
            string? note = null,
            string paymentMethod = "Cash")
        {
            if (cashReceived <= 0)
                throw new ArgumentException("Payment amount must be greater than zero.");

            cashReceived = Math.Round(cashReceived, 2);

            var bill = _billRepo.GetById(billId)
                ?? throw new InvalidOperationException($"Bill #{billId} not found.");

            if (bill.RemainingAmount <= 0.001)
                throw new InvalidOperationException("This bill is already fully paid.");

            double remaining = Math.Round(bill.RemainingAmount, 2);
            bool online = IsOnlineMethod(paymentMethod);

            if (online && cashReceived > remaining + 0.001)
            {
                throw new InvalidOperationException(
                    $"Online amount (Rs. {cashReceived:N2}) cannot exceed remaining balance (Rs. {remaining:N2}).\n" +
                    "آن لائن رقم باقی واجب الادا سے زیادہ نہیں ہو سکتی۔");
            }

            double applied = Math.Min(cashReceived, remaining);
            applied = Math.Round(applied, 2);
            if (applied <= 0)
                throw new InvalidOperationException("Nothing to apply against this bill.");

            double changeGiven = online ? 0 : Math.Max(0, Math.Round(cashReceived - applied, 2));

            var transactionTime = DateTimeHelper.CaptureTransactionTime();
            _creditRepo.RecordPayment(new CreditPayment
            {
                BillId = billId,
                AmountPaid = applied,
                Note = note,
                PaymentMethod = paymentMethod
            }, transactionTime);

            var updated = _billRepo.GetById(billId)
                ?? throw new InvalidOperationException("Bill lost after payment.");

            return new BillPaymentResult
            {
                Bill = updated,
                CashReceived = cashReceived,
                AppliedAmount = applied,
                ChangeGiven = changeGiven,
                RemainingAfter = Math.Round(updated.RemainingAmount, 2)
            };
        }

        // ────────────────────────────────────────────
        //  PAY DUES (FIFO multi-bill) — same cash/change rules
        // ────────────────────────────────────────────

        /// <summary>
        /// Pays customer dues FIFO by bill date. Excess cash becomes change.
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
            bool online = IsOnlineMethod(paymentMethod);

            var pendingBills = _billRepo.GetBillsByCustomerId(customerId)
                .Where(b => b.HasPendingCredit)
                .OrderBy(b => b.CreatedAt)
                .ThenBy(b => b.BillId)
                .ToList();

            if (pendingBills.Count == 0)
                throw new InvalidOperationException("This customer has no pending dues.");

            double totalPending = Math.Round(pendingBills.Sum(b => b.RemainingAmount), 2);
            if (online && cashReceived > totalPending + 0.001)
            {
                throw new InvalidOperationException(
                    $"Online amount (Rs. {cashReceived:N2}) cannot exceed total pending (Rs. {totalPending:N2}).\n" +
                    "آن لائن رقم کل واجب الادا سے زیادہ نہیں ہو سکتی۔");
            }

            double cashLeft = cashReceived;
            double applied = 0;
            var allocations = new List<DuesPaymentAllocation>();

            foreach (var bill in pendingBills)
            {
                if (cashLeft <= 0.001)
                    break;

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

                // Exact apply (already capped) via shared module
                var billResult = RecordPayment(fresh.BillId, pay, payNote, paymentMethod);

                allocations.Add(new DuesPaymentAllocation
                {
                    BillId = fresh.BillId,
                    InvoiceNumber = fresh.InvoiceNumber,
                    AmountPaid = billResult.AppliedAmount,
                    RemainingAfter = billResult.RemainingAfter
                });

                cashLeft = Math.Round(cashLeft - billResult.AppliedAmount, 2);
                applied = Math.Round(applied + billResult.AppliedAmount, 2);
            }

            if (applied <= 0)
                throw new InvalidOperationException("No dues could be paid with the given amount.");

            double remainingPending = Math.Round(
                _billRepo.GetBillsByCustomerId(customerId).Sum(b => b.RemainingAmount), 2);
            double changeGiven = online ? 0 : Math.Max(0, Math.Round(cashReceived - applied, 2));

            return new DuesPaymentResult
            {
                CashReceived = cashReceived,
                AppliedAmount = applied,
                ChangeGiven = changeGiven,
                RemainingPending = remainingPending,
                Allocations = allocations
            };
        }

        public List<CreditPayment> GetPaymentHistory(int billId) =>
            _creditRepo.GetPaymentsForBill(billId);

        /// <summary>Later recoveries (Pay Dues / Pay Due) for the customer statement. Not sale-time InitialPayment.</summary>
        public List<CreditPayment> GetRecoveriesForCustomer(int customerId) =>
            _creditRepo.GetRecoveriesForCustomer(customerId);

        public double GetPendingCredit(int customerId) =>
            Math.Round(_billRepo.GetBillsByCustomerId(customerId).Sum(b => b.RemainingAmount), 2);

        // ────────────────────────────────────────────
        //  OPENING BALANCE (previous paper dues)
        // ────────────────────────────────────────────

        /// <summary>
        /// Creates a one-line unpaid opening-balance bill so migrated previous dues
        /// appear in Pending Credit and can be cleared via Pay Dues (FIFO).
        /// Does not affect normal POS sales metrics (IsOpeningBalance = true).
        /// </summary>
        public Bill CreateOpeningBalance(int customerId, double amount, string? note = null, int? userId = null)
        {
            amount = Math.Round(amount, 2);
            if (amount <= 0)
                throw new ArgumentException("Opening balance amount must be greater than zero.");

            var customer = _customerRepo.GetById(customerId)
                ?? throw new InvalidOperationException("Customer not found.");

            if (customer.FullName.Equals("Walk-in Customer", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Opening balance cannot be added for walk-in customers. Register the customer first.");

            if (!customer.IsActive)
                throw new InvalidOperationException("Cannot add opening balance for an inactive customer.");

            var item = EnsureOpeningBalanceItem();
            var transactionTime = DateTimeHelper.CaptureTransactionTime();

            string itemLabel = string.IsNullOrWhiteSpace(note)
                ? OpeningBalanceConstants.ItemDescription
                : $"{OpeningBalanceConstants.ItemDescription} — {note.Trim()}";

            var lines = new List<BillDescription>
            {
                new BillDescription
                {
                    ItemId = item.Id.ToString(),
                    ItemInternalId = item.Id,
                    ItemDescription = OpeningBalanceConstants.ItemDescription,
                    ItemName = itemLabel,
                    Quantity = 1,
                    UnitPrice = amount,
                    Unit = "piece",
                    DiscountAmount = 0,
                    TotalPrice = amount
                }
            };

            var bill = new Bill
            {
                CreatedAt = transactionTime,
                SubTotal = amount,
                DiscountAmount = 0,
                TaxAmount = 0,
                CashReceived = 0,
                ChangeGiven = 0,
                UserId = userId,
                CustomerId = customerId,
                PaidAmount = 0,
                InitialPayment = 0,
                PaymentMethod = "Cash",
                Status = "Completed",
                IsOpeningBalance = true,
                OpeningBalanceNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            };

            var saved = _billRepo.SaveBillWithTransaction(bill, lines);
            AppLogger.Info(
                $"Opening balance created: CustomerId={customerId}, BillId={saved.BillId}, Amount={amount:N2}");
            return saved;
        }

        private Item EnsureOpeningBalanceItem()
        {
            var existing = _itemRepo.GetByBarcode(OpeningBalanceConstants.ItemBarcode);
            if (existing != null)
                return existing;

            var item = new Item
            {
                Barcode = OpeningBalanceConstants.ItemBarcode,
                Description = OpeningBalanceConstants.ItemDescription,
                NameUrdu = OpeningBalanceConstants.ItemNameUrdu,
                IsActive = false
            };
            _itemRepo.Add(item);
            return item;
        }
    }

    /// <summary>Result of a single-bill payment (Pay Due / history payment).</summary>
    public class BillPaymentResult
    {
        public Bill Bill { get; set; } = null!;
        public double CashReceived { get; set; }
        public double AppliedAmount { get; set; }
        public double ChangeGiven { get; set; }
        public double RemainingAfter { get; set; }
        public bool IsFullyPaid => RemainingAfter <= 0.01;
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
