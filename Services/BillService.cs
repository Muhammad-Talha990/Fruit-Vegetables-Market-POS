using System;
using System.Collections.Generic;
using System.Linq;
using GroceryPOS.Data.Repositories;
using GroceryPOS.Helpers;
using GroceryPOS.Models;

namespace GroceryPOS.Services
{
    /// <summary>
    /// Business logic for billing operations.
    /// Implements bill calculation rules, validation, and transactional saving.
    /// Replaces the old SaleService.
    /// 
    /// Business Rules:
    ///   SubTotal       = Σ(Quantity × UnitPrice)
    ///   GrandTotal     = SubTotal - DiscountAmount + TaxAmount
    ///   ChangeGiven    = CashReceived - GrandTotal
    /// </summary>
    public class BillService
    {
        private readonly BillRepository _billRepo;
        private readonly DataCacheService _cache;
        private readonly IStockService _stockService;

        public BillService(BillRepository billRepo, DataCacheService cache, IStockService stockService)
        {
            _billRepo = billRepo;
            _cache = cache;
            _stockService = stockService;
        }

        // ────────────────────────────────────────────
        //  BILL COMPLETION
        // ────────────────────────────────────────────

        /// <summary>
        /// Validates inputs, calculates totals, and saves the bill atomically.
        /// For credit sales: paidAmount &lt; grandTotal is only allowed for registered customers (non-null customerId).
        /// Walk-in customers (customerId == null) must pay in full.
        /// </summary>
        /// <param name="paidAmount">
        /// Amount physically paid now. Defaults to grandTotal (full payment).
        /// Pass less than grandTotal for a credit/udhar sale (registered customers only).
        /// </param>
        public Bill CompleteBill(int? userId, int? customerId, List<BillDescription> items,
            double discountAmount, double taxAmount, double cashReceived, double paidAmount = -1, string? billingAddress = null, string paymentMethod = "Cash", string? onlinePaymentMethod = null, int? accountId = null)
        {
            // ── Validate inputs ──
            if (items == null || items.Count == 0)
                throw new InvalidOperationException("Cannot complete bill with no items.");

            if (discountAmount < 0)
                throw new ArgumentException("Discount amount cannot be negative.");

            if (taxAmount < 0)
                throw new ArgumentException("Tax amount cannot be negative.");

            // ── Validate stock and resolve internal IDs ──
            foreach (var item in items)
            {
                // ItemId is now the string form of the integer DB Id
                if (!int.TryParse(item.ItemId, out var internalId))
                    throw new InvalidOperationException($"Invalid product identifier '{item.ItemId}'.");

                item.ItemInternalId = internalId;

                var cachedItem = _cache.GetItemById(internalId);
                if (cachedItem == null)
                    throw new InvalidOperationException($"Item with ID '{item.ItemId}' not found.");

                if (!_stockService.IsStockAvailable(internalId, item.Quantity, out double available))
                    throw new InvalidOperationException($"Insufficient stock for item {cachedItem.Description}. Available: {available}, Required: {item.Quantity}");
            }

            // ── Calculate totals per business rules ──
            foreach (var item in items)
                item.TotalPrice = item.Quantity * item.UnitPrice;

            double subTotal  = items.Sum(i => i.TotalPrice);
            double grandTotal = Math.Round(subTotal - discountAmount + taxAmount, 2);

            // If paidAmount was not provided, default to full payment
            if (paidAmount < 0) paidAmount = grandTotal;
            paidAmount = Math.Round(paidAmount, 2);

            // ── Enforce credit rules ──
            if (paidAmount < grandTotal)
            {
                if (customerId == null)
                    throw new InvalidOperationException("Credit sales are not allowed for walk-in customers. Please enter the full amount or register the customer.");

                if (paidAmount < 0)
                    throw new ArgumentException("Paid amount cannot be negative.");
            }
            else
            {
                // If paying more than total, cap at total (no credit given)
                paidAmount = grandTotal;
            }

            double changeGiven    = Math.Round(cashReceived - paidAmount, 2);
            double remainingAmount = Math.Round(grandTotal - paidAmount, 2);

            // Derive payment status (2-value: Paid or PartialPaid)
            string paymentStatus = remainingAmount <= 0 ? "Paid" : "PartialPaid";

            // ── Build Bill object ──
            var bill = new Bill
            {
                CreatedAt       = DateTime.Now,
                SubTotal        = subTotal,
                DiscountAmount  = discountAmount,
                TaxAmount       = taxAmount,
                CashReceived    = cashReceived,
                ChangeGiven     = Math.Max(0, changeGiven),
                UserId          = userId,
                CustomerId      = customerId,
                PaidAmount      = paidAmount,
                InitialPayment  = paidAmount, // Store first payment at bill creation time
                BillingAddress  = billingAddress,
                PaymentMethod   = paymentMethod,
                // Only store the sub-method for online payments; null it for cash to keep data clean
                OnlinePaymentMethod = (paymentMethod == "Online") ? onlinePaymentMethod : null,
                AccountId = (paymentMethod == "Online") ? accountId : null
            };

            // ── Save atomically (bill + items + stock) ──
            var savedBill = _billRepo.SaveBillWithTransaction(bill, items);

            // ── Update Cache & UI after successful commit ──
            foreach (var item in items)
                _cache.UpdateStockInCache(item.ItemInternalId, -item.Quantity);

            _stockService.NotifyChanged();

            return savedBill;
        }

        // ────────────────────────────────────────────
        //  READ operations
        // ────────────────────────────────────────────

        public List<Bill> GetTodayBills() => _billRepo.GetToday();

        public List<Bill> GetBillsByDateRange(DateTime from, DateTime to) => _billRepo.GetByDateRange(from, to);

        public double GetTodayTotal() => _billRepo.GetTodayTotal();

        public int GetTodayBillCount() => _billRepo.GetTodayCount();
        
        public double GetTodayTotalCredit() => _billRepo.GetTodayTotalRemaining();

        public double GetTodayTotalCash() => _billRepo.GetTodayTotalPaid();

        public double GetTodayRecoveredCredit() => _billRepo.GetTodayRecoveredCredit();

        public Bill? GetBillById(int billId) => _billRepo.GetById(billId);

        public Bill? GetLatestBillByCustomer(int customerId) => _billRepo.GetLatestBillByCustomerId(customerId);

        public List<Bill> GetBillsByCustomerId(int customerId) => _billRepo.GetBillsByCustomerId(customerId);

        public string GetNextInvoiceNumber()
        {
            int nextId = _billRepo.GetNextBillId();
            return nextId.ToString("D5");
        }

        // ── Return Stats ──────────────────────────────
        public double GetTodayReturnsTotal()  => _billRepo.GetTodayReturnsTotal();
        public double GetTodayCashRefunded()  => _billRepo.GetTodayCashRefunded();
        public double GetTodayStoreCredit()   => _billRepo.GetTodayStoreCredit();
        public double GetTodayNetSales()      => GetTodayTotal() - GetTodayReturnsTotal();
        public List<Bill> GetSalesOnlyByDateRange(DateTime from, DateTime to) => _billRepo.GetSalesOnlyByDateRange(from, to);

        // ── Payment Method Stats ─────────────────────
        public double GetTodayCashInDrawer()    => _billRepo.GetTodayCashInDrawer();
        public double GetTodayOnlinePayments()  => _billRepo.GetTodayOnlinePayments();

        /// <summary>
        /// Returns online payment totals grouped by sub-method (Easypaisa, JazzCash, Bank Transfer)
        /// for the given date range.
        /// </summary>
        public Dictionary<string, double> GetOnlinePaymentBreakdown(DateTime from, DateTime to)
            => _billRepo.GetOnlinePaymentBreakdown(from, to);
    }
}
