using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using GroceryPOS.Helpers;
using GroceryPOS.Models;

namespace GroceryPOS.Data.Repositories
{
    /// <summary>
    /// Data access for CreditPayments — individual payment installments against credit bills.
    /// Also updates the parent Bill's PaidAmount / RemainingAmount / PaymentStatus atomically.
    /// </summary>
    public class CreditPaymentRepository
    {
        private readonly CustomerLedgerRepository _ledgerRepo = new();
        // ────────────────────────────────────────────
        //  RECORD a payment (atomic log)
        // ────────────────────────────────────────────

        /// <summary>
        /// Records a payment installment in the unified Payments table.
        /// </summary>
        public void RecordPayment(CreditPayment payment)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var txn = conn.BeginTransaction();

            try
            {
                // 1. Get CustomerId from Bill
                int customerId = 0;
                string invoiceNum = "";
                using (var cmdBill = conn.CreateCommand())
                {
                    cmdBill.Transaction = txn;
                    cmdBill.CommandText = "SELECT CustomerId, BillId FROM Bills WHERE BillId = @bid;";
                    cmdBill.Parameters.AddWithValue("@bid", payment.BillId);
                    using var reader = cmdBill.ExecuteReader();
                    if (reader.Read())
                    {
                        customerId = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                        invoiceNum = reader.GetInt32(1).ToString("D5");
                    }
                }

                // 2. Record Payment
                using var cmd = conn.CreateCommand();
                cmd.Transaction = txn;
                cmd.CommandText = @"
                    INSERT INTO bill_payment (BillId, Amount, Type, PaymentMethod, CreatedAt)
                    VALUES (@bid, @amt, 'payment', @method, @at);
                    SELECT last_insert_rowid();";
                
                cmd.Parameters.AddWithValue("@bid", payment.BillId);
                cmd.Parameters.AddWithValue("@amt", Math.Round(payment.AmountPaid, 2));
                cmd.Parameters.AddWithValue("@method", payment.PaymentMethod ?? "Cash");
                cmd.Parameters.AddWithValue("@at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                
                payment.PaymentId = Convert.ToInt32(cmd.ExecuteScalar());

                // 3. Record Ledger Entry
                if (customerId > 0)
                {
                    _ledgerRepo.AppendPaymentEntry(
                        customerId,
                        payment.BillId,
                        payment.PaymentId,
                        payment.AmountPaid,
                        $"Payment received (Invoice #{invoiceNum})",
                        DateTime.Now,
                        "Recovery",
                        conn,
                        txn);
                }

                txn.Commit();
                AppLogger.Info($"CreditPayment recorded: BillId={payment.BillId}, Amount={payment.AmountPaid:N2}, Method={payment.PaymentMethod}");
            }
            catch (Exception ex)
            {
                txn.Rollback();
                AppLogger.Error("Failed to record credit payment transaction", ex);
                throw;
            }
        }

        // ────────────────────────────────────────────
        //  READ operations
        // ────────────────────────────────────────────

        /// <summary>Returns all payment installments for a specific bill, newest first.</summary>
        public List<CreditPayment> GetPaymentsForBill(int billId)
        {
            var list = new List<CreditPayment>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT PaymentId, BillId, Amount, CreatedAt, Type
                FROM bill_payment
                WHERE BillId = @bid
                ORDER BY CreatedAt DESC;";
            cmd.Parameters.AddWithValue("@bid", billId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new CreditPayment
                {
                    PaymentId       = reader.GetInt32(0),
                    BillId          = reader.GetInt32(1),
                    AmountPaid      = reader.GetDouble(2),
                    PaidAt          = reader.GetDateTime(3),
                    TransactionType = reader.GetString(4)
                });
            }
            return list;
        }

        /// <summary>Returns total amount paid across all installments for a bill.</summary>
        public double GetTotalPaidForBill(int billId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(Amount), 0) FROM bill_payment WHERE BillId = @bid AND Type = 'payment';";
            cmd.Parameters.AddWithValue("@bid", billId);
            return Convert.ToDouble(cmd.ExecuteScalar());
        }
    }
}
