using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using GroceryPOS.Data;
using GroceryPOS.Data.Repositories;
using GroceryPOS.Exceptions;
using GroceryPOS.Models;
using Microsoft.Data.Sqlite;
using GroceryPOS.Helpers;

namespace GroceryPOS.Services
{
    public class ReturnService : IReturnService
    {
        private readonly BillRepository _billRepo;
        private readonly BillReturnRepository _returnRepo;
        private readonly DataCacheService _cache;
        private readonly IStockService _stockService;
        private readonly CreditPaymentRepository _creditRepo;
        private readonly CustomerLedgerRepository _ledgerRepo = new();

        public ReturnService(
            BillRepository billRepo, 
            BillReturnRepository returnRepo, 
            DataCacheService cache, 
            IStockService stockService,
            CreditPaymentRepository creditRepo)
        {
            _billRepo = billRepo;
            _returnRepo = returnRepo;
            _cache = cache;
            _stockService = stockService;
            _creditRepo = creditRepo;
        }

        public async Task<Bill?> GetBillForReturn(int billId)
        {
            return await Task.Run(() => _billRepo.GetById(billId));
        }

        public int GetTotalReturnedQuantity(int originalBillId, string productId)
        {
            int itemId = int.TryParse(productId, out var id) ? id : 0;
            return _returnRepo.GetTotalReturnedQuantity(originalBillId, itemId);
        }

        public async Task<bool> ValidateReturnQuantity(int originalBillId, string productId, int requestedQuantity)
        {
            return await Task.Run(() =>
            {
                var originalBill = _billRepo.GetById(originalBillId);
                if (originalBill == null) return false;

                var origItem = originalBill.Items.FirstOrDefault(i => i.ItemId == productId);
                if (origItem == null) return false;

                int alreadyReturned = GetTotalReturnedQuantity(originalBillId, productId);
                return (alreadyReturned + requestedQuantity) <= origItem.Quantity;
            });
        }

        public async Task<(Bill Original, List<Bill> Returns)> GetBillWithReturnHistory(int billId)
        {
            return await Task.Run(() =>
            {
                var original = _billRepo.GetById(billId);
                if (original == null) throw new BusinessException($"Bill #{billId} not found.");

                var returns = _billRepo.GetReturnsByParentId(billId);
                return (original, returns);
            });
        }

        public async Task<Bill> CreateReturn(int originalBillId, int? userId, List<BillDescription> items)
        {
             if (items == null || !items.Any())
                throw new BusinessException("No items selected for return.");

            return await Task.Run(() =>
            {
                var originalBill = _billRepo.GetById(originalBillId);
                if (originalBill == null)
                    throw new BusinessException($"Original bill #{originalBillId} not found.");

                using var conn = DatabaseHelper.GetConnection();
                using var txn = conn.BeginTransaction();

                try
                {
                    double totalReturnValue = 0;
                    var processedItems = new List<(int BillItemId, int Qty, double Price)>();

                    foreach (var returnItem in items)
                    {
                        var origItem = originalBill.Items.FirstOrDefault(i => i.ItemId == returnItem.ItemId);
                        if (origItem == null)
                            throw new BusinessException($"Item {returnItem.ItemId} was not part of original bill #{originalBillId}.");

                        int alreadyReturned = _returnRepo.GetTotalReturnedQuantity(originalBillId, origItem.ItemInternalId);
                        int remaining = (int)origItem.Quantity - alreadyReturned;
                        
                        if (returnItem.Quantity > remaining)
                            throw new BusinessException($"Return quantity exceeds original sold quantity for {origItem.ItemDescription}. (Requested: {returnItem.Quantity}, Available: {remaining})");

                        if (returnItem.Quantity <= 0) continue;

                        processedItems.Add((origItem.BillItemId, (int)returnItem.Quantity, origItem.UnitPrice));
                        totalReturnValue += (returnItem.Quantity * origItem.UnitPrice);
                    }

                    if (!processedItems.Any())
                        throw new BusinessException("No valid items to return.");

                    // ── Validation: ReturnAmount must NOT exceed TotalAmount ──
                    if (totalReturnValue > originalBill.GrandTotal + 0.01)
                        throw new BusinessException($"Return amount (Rs. {totalReturnValue:N2}) exceeds bill total (Rs. {originalBill.GrandTotal:N2}).");

                    double remainingAmount = originalBill.RemainingAmount;
                    double creditToReduce = Math.Min(remainingAmount, totalReturnValue);
                    double cashRefund = Math.Round(totalReturnValue - creditToReduce, 2);
                    creditToReduce = Math.Round(creditToReduce, 2);

                    // 1. Create Return Header
                    int returnId = _returnRepo.InsertReturnHeader(originalBillId, cashRefund, conn, txn);

                    // 2. Create Return Items & Update Stock
                    foreach (var item in processedItems)
                    {
                        _returnRepo.InsertReturnItem(returnId, item.BillItemId, item.Qty, item.Price, conn, txn);
                    }

                    // 3. If return is larger than pending receivable, refund only the surplus in cash.
                    double cashToRefund = Math.Max(0, Math.Round(totalReturnValue - remainingAmount, 2));

                    if (cashToRefund > 0)
                    {
                        InsertPaymentEntry(conn, txn, originalBillId, cashToRefund, "refund");
                    }

                    // 4. Record Ledger Entry (Return clears liability)
                    if (originalBill.CustomerId.HasValue)
                    {
                        string returnInvoiceNum = returnId.ToString("D5");
                        _ledgerRepo.AddEntry(new CustomerLedgerEntry
                        {
                            CustomerId = originalBill.CustomerId.Value,
                            Type = "RETURN",
                            ReferenceId = returnInvoiceNum,
                            Description = $"Items Returned (Ref: Inv #{originalBill.InvoiceNumber})",
                            Debit = 0,
                            Credit = totalReturnValue,
                            EntryDate = DateTime.Now
                        }, conn, txn);
                    }

                    txn.Commit();

                    // Update Cache & Notify
                    foreach (var item in processedItems)
                    {
                        var origItem = originalBill.Items.First(i => i.BillItemId == item.BillItemId);
                        _cache.UpdateStockInCache(origItem.ItemInternalId, item.Qty);
                    }
                    _stockService.NotifyChanged();

                    // Return a virtual Bill object representing the 'Return Transaction' for the UI
                    var returnBill = new Bill
                    {
                        BillId = returnId,
                        Type = "Return",
                        ParentBillId = originalBillId,
                        CashReceived = cashRefund,
                        RemainingDueAfterThisReturn = creditToReduce,
                        CreatedAt = DateTime.Now,
                        CustomerId = originalBill.CustomerId,
                        Customer = originalBill.Customer,
                        BillingAddress = originalBill.BillingAddress,
                        Status = creditToReduce > 0 && cashRefund > 0 ? "Mixed"
                               : creditToReduce > 0 ? "CreditOnly"
                               : "CashOnly",
                        Items = new ObservableCollection<BillDescription>(processedItems.Select(pi =>
                        {
                            var origItem = originalBill.Items.First(i => i.BillItemId == pi.BillItemId);
                            return new BillDescription
                            {
                                ItemId = origItem.ItemId,
                                ItemDescription = origItem.ItemDescription,
                                Quantity = pi.Qty,
                                UnitPrice = pi.Price,
                                TotalPrice = pi.Qty * pi.Price
                            };
                        }))
                    };
                    return returnBill;
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Return processing failed", ex);
                    try { if (txn.Connection != null) txn.Rollback(); } catch { }
                    if (ex is BusinessException) throw;
                    throw new BusinessException($"Return failed: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Inserts a single payment entry within a transaction.
        /// </summary>
        private void InsertPaymentEntry(SqliteConnection conn, SqliteTransaction txn, int billId, double amount, string type)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = @"
                INSERT INTO bill_payment (BillId, Amount, Type, CreatedAt)
                VALUES (@bid, @amt, @type, @at);";
            cmd.Parameters.AddWithValue("@bid", billId);
            cmd.Parameters.AddWithValue("@amt", Math.Round(amount, 2));
            cmd.Parameters.AddWithValue("@type", type);
            cmd.Parameters.AddWithValue("@at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        public int GetRemainingReturnableQuantity(int billId, string productId, int originalQuantity)
        {
            int alreadyReturned = GetTotalReturnedQuantity(billId, productId);
            return originalQuantity - alreadyReturned;
        }

        public async Task<List<BillReturn>> GetReturnHistory(int billId)
        {
            return await Task.Run(() => _returnRepo.GetByOriginalBillId(billId));
        }

        public async Task<Bill> ProcessReturn(int originalBillId, int? userId, List<BillDescription> itemsToReturn)
        {
            return await CreateReturn(originalBillId, userId, itemsToReturn);
        }
    }
}
