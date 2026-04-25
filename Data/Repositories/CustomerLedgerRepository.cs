using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using GroceryPOS.Helpers;
using GroceryPOS.Models;

namespace GroceryPOS.Data.Repositories
{
    public class CustomerLedgerRepository
    {
        private static bool HasColumn(SqliteConnection conn, string tableName, string columnName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({tableName});";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetString(reader.GetOrdinal("name")).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private sealed class LedgerInsertContext
        {
            public int SequenceNo { get; set; }
            public double PreviousBalance { get; set; }
        }

        /// <summary>
        /// Records an entry in the ledger.
        /// CALCULATES THE RUNNING BALANCE based on the previous entry for this customer.
        /// This must be called within the same transaction as the Sale/Payment/Return.
        /// </summary>
        public void AddEntry(CustomerLedgerEntry entry, SqliteConnection conn, SqliteTransaction txn)
        {
            bool hasSequenceNo = HasColumn(conn, "CustomerLedger", "SequenceNo");
            var context = GetInsertContext(entry.CustomerId, conn, txn);
            entry.SequenceNo = hasSequenceNo ? context.SequenceNo + 1 : 0;
            entry.TransactionType = string.IsNullOrWhiteSpace(entry.TransactionType) ? entry.Type : entry.TransactionType;
            entry.RunningBalance = Math.Round(context.PreviousBalance + entry.Debit - entry.Credit, 2);

            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            if (hasSequenceNo)
            {
                cmd.CommandText = @"
                INSERT INTO CustomerLedger
                (CustomerId, EntryDate, Type, TransactionType, ReferenceId, SourceTable, SourceId, BillId, ReturnId, PaymentId, Description, Debit, Credit, RunningBalance, CreatedAtUtc, CreatedByUserId, SequenceNo)
                VALUES
                (@cid, @date, @type, @txnType, @ref, @sourceTable, @sourceId, @billId, @returnId, @paymentId, @desc, @debit, @credit, @bal, @createdAtUtc, @createdBy, @seq);
                SELECT last_insert_rowid();
            ";
            }
            else
            {
                cmd.CommandText = @"
                INSERT INTO CustomerLedger
                (CustomerId, EntryDate, Type, TransactionType, ReferenceId, SourceTable, SourceId, BillId, ReturnId, PaymentId, Description, Debit, Credit, RunningBalance, CreatedAtUtc, CreatedByUserId)
                VALUES
                (@cid, @date, @type, @txnType, @ref, @sourceTable, @sourceId, @billId, @returnId, @paymentId, @desc, @debit, @credit, @bal, @createdAtUtc, @createdBy);
                SELECT last_insert_rowid();
            ";
            }
            cmd.Parameters.AddWithValue("@cid", entry.CustomerId);
            cmd.Parameters.AddWithValue("@date", entry.EntryDate.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@type", entry.Type);
            cmd.Parameters.AddWithValue("@txnType", entry.TransactionType);
            cmd.Parameters.AddWithValue("@ref", (object?)entry.ReferenceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sourceTable", (object?)entry.SourceTable ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sourceId", entry.SourceId.HasValue ? entry.SourceId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@billId", entry.BillId.HasValue ? entry.BillId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@returnId", entry.ReturnId.HasValue ? entry.ReturnId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@paymentId", entry.PaymentId.HasValue ? entry.PaymentId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@desc", (object?)entry.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@debit", entry.Debit);
            cmd.Parameters.AddWithValue("@credit", entry.Credit);
            cmd.Parameters.AddWithValue("@bal", entry.RunningBalance);
            cmd.Parameters.AddWithValue("@createdAtUtc", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@createdBy", entry.CreatedByUserId.HasValue ? entry.CreatedByUserId.Value : DBNull.Value);
            if (hasSequenceNo)
            {
                cmd.Parameters.AddWithValue("@seq", entry.SequenceNo);
            }

            entry.LedgerId = Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void AppendSaleEntry(int customerId, int billId, double debitAmount, string description, DateTime entryDate, SqliteConnection conn, SqliteTransaction txn)
        {
            AddEntry(new CustomerLedgerEntry
            {
                CustomerId = customerId,
                EntryDate = entryDate,
                Type = "SALE",
                TransactionType = "SALE",
                ReferenceId = billId.ToString("D5"),
                SourceTable = "Bills",
                SourceId = billId,
                BillId = billId,
                Description = description,
                Debit = Math.Round(debitAmount, 2),
                Credit = 0
            }, conn, txn);
        }

        public void AppendPaymentEntry(int customerId, int billId, int paymentId, double creditAmount, string description, DateTime entryDate, string paymentMethod, SqliteConnection conn, SqliteTransaction txn)
        {
            string type = string.Equals(paymentMethod, "Recovery", StringComparison.OrdinalIgnoreCase) ? "RECOVERY" : "PAYMENT";
            AddEntry(new CustomerLedgerEntry
            {
                CustomerId = customerId,
                EntryDate = entryDate,
                Type = "PAYMENT",
                TransactionType = type,
                ReferenceId = billId.ToString("D5"),
                SourceTable = "bill_payment",
                SourceId = paymentId,
                BillId = billId,
                PaymentId = paymentId,
                Description = description,
                Debit = 0,
                Credit = Math.Round(creditAmount, 2)
            }, conn, txn);
        }

        public void AppendReturnEntry(int customerId, int billId, int returnId, double creditAmount, string description, DateTime entryDate, SqliteConnection conn, SqliteTransaction txn)
        {
            AddEntry(new CustomerLedgerEntry
            {
                CustomerId = customerId,
                EntryDate = entryDate,
                Type = "RETURN",
                TransactionType = "RETURN",
                ReferenceId = returnId.ToString("D5"),
                SourceTable = "BillReturns",
                SourceId = returnId,
                BillId = billId,
                ReturnId = returnId,
                Description = description,
                Debit = 0,
                Credit = Math.Round(creditAmount, 2)
            }, conn, txn);
        }

        public void AppendAdjustmentEntry(int customerId, double debit, double credit, string description, DateTime entryDate, SqliteConnection conn, SqliteTransaction txn)
        {
            AddEntry(new CustomerLedgerEntry
            {
                CustomerId = customerId,
                EntryDate = entryDate,
                Type = "ADJUSTMENT",
                TransactionType = "ADJUSTMENT",
                SourceTable = "Manual",
                Description = description,
                Debit = Math.Round(debit, 2),
                Credit = Math.Round(credit, 2)
            }, conn, txn);
        }

        private double GetLatestBalanceInternal(int customerId, SqliteConnection conn, SqliteTransaction? txn)
        {
            bool hasSequenceNo = HasColumn(conn, "CustomerLedger", "SequenceNo");
            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = hasSequenceNo ? @"
                SELECT RunningBalance
                FROM CustomerLedger
                WHERE CustomerId = @cid
                ORDER BY datetime(EntryDate) DESC, SequenceNo DESC, LedgerId DESC
                LIMIT 1;" : @"
                SELECT RunningBalance
                FROM CustomerLedger
                WHERE CustomerId = @cid
                ORDER BY datetime(EntryDate) DESC, LedgerId DESC
                LIMIT 1;";
            cmd.Parameters.AddWithValue("@cid", customerId);

            var result = cmd.ExecuteScalar();
            return result != null ? Convert.ToDouble(result) : 0;
        }

        private LedgerInsertContext GetInsertContext(int customerId, SqliteConnection conn, SqliteTransaction? txn)
        {
            bool hasSequenceNo = HasColumn(conn, "CustomerLedger", "SequenceNo");
            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = hasSequenceNo ? @"
                SELECT COALESCE(SequenceNo, 0), COALESCE(RunningBalance, 0)
                FROM CustomerLedger
                WHERE CustomerId = @cid
                ORDER BY datetime(EntryDate) DESC, SequenceNo DESC, LedgerId DESC
                LIMIT 1;" : @"
                SELECT 0, COALESCE(RunningBalance, 0)
                FROM CustomerLedger
                WHERE CustomerId = @cid
                ORDER BY datetime(EntryDate) DESC, LedgerId DESC
                LIMIT 1;";
            cmd.Parameters.AddWithValue("@cid", customerId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new LedgerInsertContext
                {
                    SequenceNo = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                    PreviousBalance = reader.IsDBNull(1) ? 0 : reader.GetDouble(1)
                };
            }

            return new LedgerInsertContext();
        }

        public List<CustomerLedgerEntry> GetByCustomer(int customerId)
        {
            var list = new List<CustomerLedgerEntry>();
            using var conn = DatabaseHelper.GetConnection();
            bool hasSequenceNo = HasColumn(conn, "CustomerLedger", "SequenceNo");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = hasSequenceNo ? @"
                SELECT LedgerId, CustomerId, EntryDate, Type, TransactionType, ReferenceId, SourceTable, SourceId, BillId, ReturnId, PaymentId, Description, Debit, Credit, RunningBalance, SequenceNo
                FROM CustomerLedger
                WHERE CustomerId = @cid
                ORDER BY datetime(EntryDate) ASC, SequenceNo ASC, LedgerId ASC;" : @"
                SELECT LedgerId, CustomerId, EntryDate, Type, TransactionType, ReferenceId, SourceTable, SourceId, BillId, ReturnId, PaymentId, Description, Debit, Credit, RunningBalance, 0 AS SequenceNo
                FROM CustomerLedger
                WHERE CustomerId = @cid
                ORDER BY datetime(EntryDate) ASC, LedgerId ASC;";
            cmd.Parameters.AddWithValue("@cid", customerId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new CustomerLedgerEntry
                {
                    LedgerId       = reader.GetInt32(0),
                    CustomerId     = reader.GetInt32(1),
                    EntryDate      = reader.GetDateTime(2),
                    Type           = reader.IsDBNull(3) ? "SALE" : reader.GetString(3),
                    TransactionType= reader.IsDBNull(4) ? (reader.IsDBNull(3) ? "SALE" : reader.GetString(3)) : reader.GetString(4),
                    ReferenceId    = reader.IsDBNull(5) ? null : reader.GetString(5),
                    SourceTable    = reader.IsDBNull(6) ? null : reader.GetString(6),
                    SourceId       = reader.IsDBNull(7) ? null : reader.GetInt64(7),
                    BillId         = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    ReturnId       = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    PaymentId      = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    Description    = reader.IsDBNull(11) ? null : reader.GetString(11),
                    Debit          = reader.IsDBNull(12) ? 0 : reader.GetDouble(12),
                    Credit         = reader.IsDBNull(13) ? 0 : reader.GetDouble(13),
                    RunningBalance = reader.IsDBNull(14) ? 0 : reader.GetDouble(14),
                    SequenceNo     = reader.IsDBNull(15) ? 0 : reader.GetInt32(15)
                });
            }
            return list;
        }

        public List<CustomerLedgerEntry> GetLedgerTimeline(int customerId, DateTime? from = null, DateTime? to = null)
        {
            var list = new List<CustomerLedgerEntry>();
            using var conn = DatabaseHelper.GetConnection();
            bool hasSequenceNo = HasColumn(conn, "CustomerLedger", "SequenceNo");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = hasSequenceNo ? @"
                SELECT LedgerId, CustomerId, EntryDate, Type, TransactionType, ReferenceId, SourceTable, SourceId, BillId, ReturnId, PaymentId, Description, Debit, Credit, RunningBalance, SequenceNo
                FROM CustomerLedger
                WHERE CustomerId = @cid
                  AND (@from IS NULL OR datetime(EntryDate) >= datetime(@from))
                  AND (@to IS NULL OR datetime(EntryDate) < datetime(@to))
                ORDER BY datetime(EntryDate) ASC, SequenceNo ASC, LedgerId ASC;" : @"
                SELECT LedgerId, CustomerId, EntryDate, Type, TransactionType, ReferenceId, SourceTable, SourceId, BillId, ReturnId, PaymentId, Description, Debit, Credit, RunningBalance, 0 AS SequenceNo
                FROM CustomerLedger
                WHERE CustomerId = @cid
                  AND (@from IS NULL OR datetime(EntryDate) >= datetime(@from))
                  AND (@to IS NULL OR datetime(EntryDate) < datetime(@to))
                ORDER BY datetime(EntryDate) ASC, LedgerId ASC;";
            cmd.Parameters.AddWithValue("@cid", customerId);
            cmd.Parameters.AddWithValue("@from", from.HasValue ? from.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
            cmd.Parameters.AddWithValue("@to", to.HasValue ? to.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new CustomerLedgerEntry
                {
                    LedgerId = reader.GetInt32(0),
                    CustomerId = reader.GetInt32(1),
                    EntryDate = reader.GetDateTime(2),
                    Type = reader.IsDBNull(3) ? "SALE" : reader.GetString(3),
                    TransactionType = reader.IsDBNull(4) ? (reader.IsDBNull(3) ? "SALE" : reader.GetString(3)) : reader.GetString(4),
                    ReferenceId = reader.IsDBNull(5) ? null : reader.GetString(5),
                    SourceTable = reader.IsDBNull(6) ? null : reader.GetString(6),
                    SourceId = reader.IsDBNull(7) ? null : reader.GetInt64(7),
                    BillId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    ReturnId = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    PaymentId = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    Description = reader.IsDBNull(11) ? null : reader.GetString(11),
                    Debit = reader.IsDBNull(12) ? 0 : reader.GetDouble(12),
                    Credit = reader.IsDBNull(13) ? 0 : reader.GetDouble(13),
                    RunningBalance = reader.IsDBNull(14) ? 0 : reader.GetDouble(14),
                    SequenceNo = reader.IsDBNull(15) ? 0 : reader.GetInt32(15)
                });
            }
            return list;
        }

        public List<BillDescription> GetSaleBreakdown(int billId)
        {
            var items = new List<BillDescription>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT bi.BillItemId, bi.BillId, bi.ItemId, bi.Quantity, bi.UnitPrice, COALESCE(bi.DiscountAmount, 0), i.Description
                FROM BillItems bi
                JOIN Items i ON i.ItemId = bi.ItemId
                WHERE bi.BillId = @billId
                ORDER BY bi.BillItemId ASC;";
            cmd.Parameters.AddWithValue("@billId", billId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new BillDescription
                {
                    BillItemId = reader.GetInt32(0),
                    BillId = reader.GetInt32(1),
                    ItemInternalId = reader.GetInt32(2),
                    ItemId = reader.GetInt32(2).ToString(),
                    Quantity = reader.GetDouble(3),
                    UnitPrice = reader.GetDouble(4),
                    DiscountAmount = reader.GetDouble(5),
                    ItemDescription = reader.IsDBNull(6) ? string.Empty : reader.GetString(6)
                });
            }
            return items;
        }

        public List<BillReturnItemAudit> GetReturnBreakdown(int returnId)
        {
            var items = new List<BillReturnItemAudit>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT i.Description, bri.Quantity, bri.UnitPrice
                FROM BillReturnItems bri
                JOIN BillItems bi ON bi.BillItemId = bri.BillItemId
                JOIN Items i ON i.ItemId = bi.ItemId
                WHERE bri.ReturnId = @rid
                ORDER BY bri.ReturnItemId ASC;";
            cmd.Parameters.AddWithValue("@rid", returnId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new BillReturnItemAudit
                {
                    ItemDescription = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    Quantity = Convert.ToInt32(reader.GetDouble(1)),
                    UnitPrice = reader.GetDouble(2)
                });
            }
            return items;
        }

        public void RebuildRunningBalances(int customerId)
        {
            using var conn = DatabaseHelper.GetConnection();
            bool hasSequenceNo = HasColumn(conn, "CustomerLedger", "SequenceNo");
            using var txn = conn.BeginTransaction();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = txn;
                cmd.CommandText = hasSequenceNo ? @"
                    SELECT LedgerId, Debit, Credit
                    FROM CustomerLedger
                    WHERE CustomerId = @cid
                    ORDER BY datetime(EntryDate) ASC, SequenceNo ASC, LedgerId ASC;" : @"
                    SELECT LedgerId, Debit, Credit
                    FROM CustomerLedger
                    WHERE CustomerId = @cid
                    ORDER BY datetime(EntryDate) ASC, LedgerId ASC;";
                cmd.Parameters.AddWithValue("@cid", customerId);

                var rows = new List<(int LedgerId, double Debit, double Credit)>();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        rows.Add((reader.GetInt32(0), reader.IsDBNull(1) ? 0 : reader.GetDouble(1), reader.IsDBNull(2) ? 0 : reader.GetDouble(2)));
                    }
                }

                double running = 0;
                int seq = 0;
                foreach (var row in rows)
                {
                    seq++;
                    running = Math.Round(running + row.Debit - row.Credit, 2);
                    using var upd = conn.CreateCommand();
                    upd.Transaction = txn;
                    upd.CommandText = hasSequenceNo
                        ? "UPDATE CustomerLedger SET RunningBalance = @bal, SequenceNo = @seq WHERE LedgerId = @id;"
                        : "UPDATE CustomerLedger SET RunningBalance = @bal WHERE LedgerId = @id;";
                    upd.Parameters.AddWithValue("@bal", running);
                    if (hasSequenceNo)
                    {
                        upd.Parameters.AddWithValue("@seq", seq);
                    }
                    upd.Parameters.AddWithValue("@id", row.LedgerId);
                    upd.ExecuteNonQuery();
                }

                txn.Commit();
            }
            catch
            {
                txn.Rollback();
                throw;
            }
        }

        public (double ClosingBalance, double DerivedOutstanding, double Drift) GetIntegritySnapshot(int customerId)
        {
            using var conn = DatabaseHelper.GetConnection();
            bool hasSequenceNo = HasColumn(conn, "CustomerLedger", "SequenceNo");
            double closing;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = hasSequenceNo ? @"
                    SELECT COALESCE(RunningBalance, 0)
                    FROM CustomerLedger
                    WHERE CustomerId = @cid
                    ORDER BY datetime(EntryDate) DESC, SequenceNo DESC, LedgerId DESC
                    LIMIT 1;" : @"
                    SELECT COALESCE(RunningBalance, 0)
                    FROM CustomerLedger
                    WHERE CustomerId = @cid
                    ORDER BY datetime(EntryDate) DESC, LedgerId DESC
                    LIMIT 1;";
                cmd.Parameters.AddWithValue("@cid", customerId);
                closing = Convert.ToDouble(cmd.ExecuteScalar());
            }

            double derived;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(
                        CASE
                            WHEN (
                                (SELECT COALESCE(SUM((bi.Quantity * bi.UnitPrice) - COALESCE(bi.DiscountAmount, 0)), 0)
                                 FROM BillItems bi WHERE bi.BillId = b.BillId)
                                + COALESCE(b.TaxAmount, 0)
                                - COALESCE(b.DiscountAmount, 0)
                                - COALESCE((SELECT SUM(bri.Quantity * bri.UnitPrice)
                                            FROM BillReturnItems bri
                                            JOIN BillReturns br ON br.ReturnId = bri.ReturnId
                                            WHERE br.BillId = b.BillId), 0)
                                - COALESCE(b.InitialPayment, 0)
                                - COALESCE((SELECT SUM(p.Amount) FROM bill_payment p WHERE p.BillId = b.BillId AND p.Type = 'payment'), 0)
                                + COALESCE((SELECT SUM(rf.Amount) FROM bill_payment rf WHERE rf.BillId = b.BillId AND rf.Type = 'refund'), 0)
                            ) > 0
                            THEN (
                                (SELECT COALESCE(SUM((bi.Quantity * bi.UnitPrice) - COALESCE(bi.DiscountAmount, 0)), 0)
                                 FROM BillItems bi WHERE bi.BillId = b.BillId)
                                + COALESCE(b.TaxAmount, 0)
                                - COALESCE(b.DiscountAmount, 0)
                                - COALESCE((SELECT SUM(bri.Quantity * bri.UnitPrice)
                                            FROM BillReturnItems bri
                                            JOIN BillReturns br ON br.ReturnId = bri.ReturnId
                                            WHERE br.BillId = b.BillId), 0)
                                - COALESCE(b.InitialPayment, 0)
                                - COALESCE((SELECT SUM(p.Amount) FROM bill_payment p WHERE p.BillId = b.BillId AND p.Type = 'payment'), 0)
                                + COALESCE((SELECT SUM(rf.Amount) FROM bill_payment rf WHERE rf.BillId = b.BillId AND rf.Type = 'refund'), 0)
                            )
                            ELSE 0
                        END
                    ), 0)
                    FROM Bills b
                    WHERE b.CustomerId = @cid
                      AND b.Status != 'Cancelled';";
                cmd.Parameters.AddWithValue("@cid", customerId);
                derived = Convert.ToDouble(cmd.ExecuteScalar());
            }

            double drift = Math.Round(closing - derived, 2);
            return (Math.Round(closing, 2), Math.Round(derived, 2), drift);
        }

        /// <summary>
        /// Used for high-level dashboard metrics or customer cards without loading full history.
        /// </summary>
        public double GetCurrentBalance(int customerId)
        {
             using var conn = DatabaseHelper.GetConnection();
             return GetLatestBalanceInternal(customerId, conn, null);
        }
    }
}
