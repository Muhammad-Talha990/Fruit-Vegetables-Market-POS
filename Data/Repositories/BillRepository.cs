using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using GroceryPOS.Helpers;
using GroceryPOS.Models;
using System.Collections.ObjectModel;

namespace GroceryPOS.Data.Repositories
{
    /// <summary>
    /// Data access for the Bill and BillDescription tables.
    /// Handles transactional bill saving with stock deduction.
    /// </summary>
    public class BillRepository
    {
        private readonly ItemRepository _itemRepo = new();
        private readonly CustomerLedgerRepository _ledgerRepo = new();

        // ────────────────────────────────────────────
        //  TRANSACTIONAL BILL SAVE
        // ────────────────────────────────────────────

        /// <summary>
        /// Saves a complete bill atomically:
        ///   1. Insert Bill header
        ///   2. Insert all BillDescription line items
        ///   3. Deduct stock for each item
        /// Rolls back everything if any step fails (including insufficient stock).
        /// </summary>
        /// <returns>The saved Bill with its generated BillId.</returns>
        public Bill SaveBillWithTransaction(Bill bill, List<BillDescription> items)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var txn = conn.BeginTransaction();

            try
            {
                var result = SaveBillInternal(bill, items, conn, txn);
                txn.Commit();
                return result;
            }
            catch (Exception ex)
            {
                txn.Rollback();
                AppLogger.Error("Bill transaction failed — rolled back", ex);
                throw;
            }
        }

        /// <summary>
        /// Saves a bill within an existing transaction. 
        /// </summary>
        public Bill SaveBillInternal(Bill bill, List<BillDescription> items, SqliteConnection conn, SqliteTransaction txn)
        {
            // ── Step 1: Insert Bill header ──
            using var billCmd = conn.CreateCommand();
            billCmd.Transaction = txn;
            billCmd.CommandText = @"
                INSERT INTO Bills (CustomerId, UserId, TaxAmount, DiscountAmount, Status, BillPaymentMethod, OnlinePaymentMethod, AccountId, InitialPayment, CreatedAt)
                VALUES (@cid, @uid, @tax, @disc, @status, @billPayMethod, @onlinePayMethod, @accountId, @initialPay, @createdAt);
                SELECT last_insert_rowid();
            ";
            billCmd.Parameters.AddWithValue("@cid", bill.CustomerId.HasValue ? (object)bill.CustomerId.Value : DBNull.Value);
            billCmd.Parameters.AddWithValue("@uid", bill.UserId.HasValue ? (object)bill.UserId.Value : DBNull.Value);
            billCmd.Parameters.AddWithValue("@tax", bill.TaxAmount);
            billCmd.Parameters.AddWithValue("@disc", bill.DiscountAmount);
            billCmd.Parameters.AddWithValue("@status", bill.Status ?? "Completed");
            billCmd.Parameters.AddWithValue("@billPayMethod", bill.PaymentMethod ?? "Cash");
            billCmd.Parameters.AddWithValue("@onlinePayMethod", string.IsNullOrEmpty(bill.OnlinePaymentMethod) ? (object)DBNull.Value : bill.OnlinePaymentMethod);
            billCmd.Parameters.AddWithValue("@accountId", bill.AccountId.HasValue ? (object)bill.AccountId.Value : DBNull.Value);
            billCmd.Parameters.AddWithValue("@initialPay", bill.InitialPayment);
            billCmd.Parameters.AddWithValue("@createdAt", bill.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));

            bill.BillId = Convert.ToInt32(billCmd.ExecuteScalar());

            // ── Step 2: Insert BillItems and Record Stock Changes ──
            foreach (var item in items)
            {
                item.BillId = bill.BillId;

                using var itemCmd = conn.CreateCommand();
                itemCmd.Transaction = txn;
                itemCmd.CommandText = @"
                    INSERT INTO BillItems (BillId, ItemId, Quantity, UnitPrice, DiscountAmount)
                    VALUES (@billId, @itemId, @qty, @price, @itemDisc);
                    
                    INSERT INTO InventoryLogs (ItemId, QuantityChange, ChangeType, ReferenceId, ReferenceType, LogDate)
                    VALUES (@itemId, @qtyLog, 'Sale', @billId, 'Bill', @logDate);
                ";
                itemCmd.Parameters.AddWithValue("@billId", item.BillId);
                itemCmd.Parameters.AddWithValue("@itemId", item.ItemInternalId);
                itemCmd.Parameters.AddWithValue("@qty", item.Quantity);
                itemCmd.Parameters.AddWithValue("@qtyLog", -item.Quantity); // Deduction
                itemCmd.Parameters.AddWithValue("@price", item.UnitPrice);
                itemCmd.Parameters.AddWithValue("@itemDisc", item.DiscountAmount);
                itemCmd.Parameters.AddWithValue("@logDate", bill.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                itemCmd.ExecuteNonQuery();
            }

            bill.Items = new ObservableCollection<BillDescription>(items);

            // ── Step 3: Record Customer Ledger Entries ──
            if (bill.CustomerId.HasValue)
            {
                // A. Record the SALE (Liability Created)
                _ledgerRepo.AddEntry(new CustomerLedgerEntry
                {
                    CustomerId = bill.CustomerId.Value,
                    Type = "SALE",
                    ReferenceId = bill.InvoiceNumber,
                    Description = $"Invoice #{bill.InvoiceNumber}",
                    Debit = bill.GrandTotal, // Full amount of bill
                    Credit = 0,
                    EntryDate = DateTime.Now
                }, conn, txn);

                // B. Record the PAYMENT (Liability Reduced) if anything was paid at checkout
                if (bill.InitialPayment > 0)
                {
                    _ledgerRepo.AddEntry(new CustomerLedgerEntry
                    {
                        CustomerId = bill.CustomerId.Value,
                        Type = "PAYMENT",
                        ReferenceId = bill.InvoiceNumber,
                        Description = $"Initial Payment (Ref: #{bill.InvoiceNumber})",
                        Debit = 0,
                        Credit = bill.InitialPayment,
                        EntryDate = DateTime.Now
                    }, conn, txn);
                }
            }

            AppLogger.Info($"3NF Bill saved: ID={bill.BillId} | Status={bill.Status} | InitialPay={bill.InitialPayment:N2}");
            return bill;
        }

        // ────────────────────────────────────────────
        //  READ operations
        // ────────────────────────────────────────────

        /// <summary>Gets a bill by ID including its line items.</summary>
        public Bill? GetById(int billId)
        {
            using var conn = DatabaseHelper.GetConnection();

            // Get bill header with calculated aggregates
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT b.*, 
                       u.Username, u.FullName as UserFullName, u.Role as UserRole,
                       c.FullName as CustomerName, c.Phone as CustomerPhone, c.Address as CustomerAddress,
                       a.AccountTitle, a.AccountType, a.BankName,
                       (SELECT COALESCE(SUM(Quantity * UnitPrice), 0) FROM BillItems WHERE BillId = b.BillId) as SubTotal,
                       (SELECT COALESCE(SUM(CASE WHEN Type = 'payment' THEN Amount WHEN Type = 'refund' THEN -Amount ELSE 0 END), 0)
                        FROM bill_payment
                        WHERE BillId = b.BillId) as AdditionalPaid,
                       (SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0) FROM BillReturnItems bri JOIN BillReturns br ON bri.ReturnId = br.ReturnId WHERE br.BillId = b.BillId) as TotalReturned
                FROM Bills b
                LEFT JOIN Users u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                LEFT JOIN Accounts a ON b.AccountId = a.Id
                WHERE b.BillId = @id;";
            cmd.Parameters.AddWithValue("@id", billId);

            Bill? bill = null;
            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                    bill = MapBill(reader);
            }

            if (bill != null)
            {
                LoadLineItems(conn, new List<Bill> { bill });
                LoadAuditLogs(bill, conn);
            }

            return bill;
        }

        public Bill? GetByInvoiceNumber(string invoiceNum)
        {
            if (string.IsNullOrEmpty(invoiceNum)) return null;
            // InvoiceNumber is just BillId formatted as D5. So we parse it back to ID.
            if (!int.TryParse(invoiceNum, out int billId)) return null;

            return GetById(billId);
        }

        /// <summary>Gets all bills ordered by date descending.</summary>
        public List<Bill> GetAll()
        {
            var bills = new List<Bill>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT b.*, 
                       u.Username, u.FullName as UserFullName, u.Role as UserRole,
                       c.FullName as CustomerName, c.Phone as CustomerPhone, c.Address as CustomerAddress,
                       a.AccountTitle, a.AccountType, a.BankName,
                       (SELECT COALESCE(SUM(Quantity * UnitPrice), 0) FROM BillItems WHERE BillId = b.BillId) as SubTotal,
                       (SELECT COALESCE(SUM(CASE WHEN p.Type = 'payment' THEN p.Amount WHEN p.Type = 'refund' THEN -p.Amount ELSE 0 END), 0)
                        FROM bill_payment p
                        WHERE p.BillId = b.BillId) as AdditionalPaid,
                       (SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0) FROM BillReturnItems bri JOIN BillReturns br ON bri.ReturnId = br.ReturnId WHERE br.BillId = b.BillId) as TotalReturned
                FROM Bills b
                LEFT JOIN Users u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                LEFT JOIN Accounts a ON b.AccountId = a.Id
                ORDER BY b.CreatedAt DESC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var bill = MapBill(reader);
                if (bill != null) bills.Add(bill);
            }

            LoadLineItems(conn, bills);
            return bills;
        }

        /// <summary>Gets bills within a date range.</summary>
        public List<Bill> GetByDateRange(DateTime from, DateTime to)
        {
            var bills = new List<Bill>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT b.*, 
                       u.Username, u.FullName as UserFullName, u.Role as UserRole,
                       c.FullName as CustomerName, c.Phone as CustomerPhone, c.Address as CustomerAddress,
                       a.AccountTitle, a.AccountType, a.BankName,
                       (SELECT COALESCE(SUM(Quantity * UnitPrice), 0) FROM BillItems WHERE BillId = b.BillId) as SubTotal,
                       (SELECT COALESCE(SUM(CASE WHEN p.Type = 'payment' THEN p.Amount WHEN p.Type = 'refund' THEN -p.Amount ELSE 0 END), 0)
                        FROM bill_payment p
                        WHERE p.BillId = b.BillId) as AdditionalPaid,
                       (SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0) FROM BillReturnItems bri JOIN BillReturns br ON bri.ReturnId = br.ReturnId WHERE br.BillId = b.BillId) as TotalReturned
                FROM Bills b
                LEFT JOIN Users u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                LEFT JOIN Accounts a ON b.AccountId = a.Id
                WHERE b.CreatedAt >= @from AND b.CreatedAt < @to 
                ORDER BY b.CreatedAt DESC;";
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var bill = MapBill(reader);
                if (bill != null) bills.Add(bill);
            }

            LoadLineItems(conn, bills);
            return bills;
        }

        /// <summary>Gets today's bills.</summary>
        public List<Bill> GetToday()
        {
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);
            return GetByDateRange(today, tomorrow);
        }

        /// <summary>Gets today's total revenue (Sales Value).</summary>
        public double GetTodayTotal()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            cmd.CommandText = @"
                SELECT COALESCE(SUM(bill_total), 0)
                FROM (
                    SELECT (SUM(bi.Quantity * bi.UnitPrice) + b.TaxAmount - b.DiscountAmount) as bill_total
                    FROM Bills b
                    JOIN BillItems bi ON b.BillId = bi.BillId
                    WHERE date(b.CreatedAt) = @today AND b.Status != 'Cancelled'
                    GROUP BY b.BillId
                );";
            cmd.Parameters.AddWithValue("@today", todayStr);
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets today's total remaining due amount (credit/udhar) for all bills created today.</summary>
        public double GetTodayTotalRemaining()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            // Formula: max(0, NetTotal - InitialPayment - credit payments)
            cmd.CommandText = @"
                SELECT COALESCE(SUM(
                    CASE
                        WHEN (
                            (SELECT COALESCE(SUM(bi.Quantity * bi.UnitPrice), 0) FROM BillItems bi WHERE bi.BillId = b.BillId)
                            + COALESCE(b.TaxAmount, 0)
                            - COALESCE(b.DiscountAmount, 0)
                            - COALESCE((SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0)
                                        FROM BillReturnItems bri
                                        JOIN BillReturns br ON bri.ReturnId = br.ReturnId
                                        WHERE br.BillId = b.BillId), 0)
                            - COALESCE(b.InitialPayment, 0)
                            - COALESCE((SELECT SUM(p.Amount) FROM bill_payment p WHERE p.BillId = b.BillId AND p.Type = 'payment'), 0)
                        ) > 0
                        THEN (
                            (SELECT COALESCE(SUM(bi.Quantity * bi.UnitPrice), 0) FROM BillItems bi WHERE bi.BillId = b.BillId)
                            + COALESCE(b.TaxAmount, 0)
                            - COALESCE(b.DiscountAmount, 0)
                            - COALESCE((SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0)
                                        FROM BillReturnItems bri
                                        JOIN BillReturns br ON bri.ReturnId = br.ReturnId
                                        WHERE br.BillId = b.BillId), 0)
                            - COALESCE(b.InitialPayment, 0)
                            - COALESCE((SELECT SUM(p.Amount) FROM bill_payment p WHERE p.BillId = b.BillId AND p.Type = 'payment'), 0)
                        )
                        ELSE 0
                    END
                ), 0)
                FROM Bills b
                WHERE date(b.CreatedAt) = @today AND b.Status != 'Cancelled';";
            cmd.Parameters.AddWithValue("@today", todayStr);
            return Math.Round(Convert.ToDouble(cmd.ExecuteScalar()), 2);
        }

        private double GetTodayTotalPaidInSales()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
                        // Unified Paid: InitialPayment (from Bills) + non-Sale Payments for today's bills.
            cmd.CommandText = @"
                SELECT 
                                        (SELECT COALESCE(SUM(InitialPayment), 0) FROM Bills WHERE date(CreatedAt) = @today AND Status != 'Cancelled')
                                        +
                                        (SELECT COALESCE(SUM(p.Amount), 0)
                                         FROM bill_payment p
                                         JOIN Bills b ON p.BillId = b.BillId
                                         WHERE date(b.CreatedAt) = @today 
                                             AND b.Status != 'Cancelled'
                                            AND p.Type = 'payment');";
            cmd.Parameters.AddWithValue("@today", todayStr);
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets today's cash collected from today's sale bills only (excludes credit recovery and return offsets).</summary>
        public double GetTodayTotalPaid()
        {
            // This should return what was paid on today's bills AT THE TIME of sale (initial) or today (subsequent)
            // But usually this metric specifically tracks cash flow related to today's sales.
            return GetTodayTotalPaidInSales();
        }

        /// <summary>Gets today's total cash refunded to customers from returns.</summary>
        public double GetTodayCashRefunded()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            cmd.CommandText = @"
                SELECT COALESCE(SUM(Amount), 0)
                FROM bill_payment
                WHERE date(CreatedAt) = @today
                  AND Type = 'refund';";
            cmd.Parameters.AddWithValue("@today", todayStr);
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets today's recovered credit: strictly only subsequent payments made after sale.</summary>
        public double GetTodayRecoveredCredit()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            // Count only payment entries (refunds are separate cash outflow).
            cmd.CommandText = @"
                SELECT COALESCE(SUM(Amount), 0)
                FROM bill_payment
                WHERE date(CreatedAt) = @today
                  AND Type = 'payment'
                  AND Amount > 0;";
            cmd.Parameters.AddWithValue("@today", todayStr);
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets the absolute total value of all return transactions today (cash refunds + credit offsets).</summary>
        public double GetTodayReturnsTotal()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            cmd.CommandText = @"
                SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0)
                FROM BillReturnItems bri
                JOIN BillReturns br ON bri.ReturnId = br.ReturnId
                WHERE date(br.ReturnedAt) = @today;";
            cmd.Parameters.AddWithValue("@today", todayStr);
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets today's net cash in drawer: Initial Cash Payments (Bills) + Subsequent Cash (Payments table).</summary>
        public double GetTodayCashInDrawer()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
                        // Unified Logic:
                        // 1. Sum of Initial Payments (Cash) from today's Bills
                        // 2. + subsequent cash payments today
                        // 3. - cash refunds today
            cmd.CommandText = @"
                SELECT 
                                        (SELECT COALESCE(SUM(InitialPayment), 0) 
                     FROM Bills 
                     WHERE date(CreatedAt) = @today 
                       AND BillPaymentMethod = 'Cash' 
                       AND Status != 'Cancelled')
                    +
                    (SELECT COALESCE(SUM(p.Amount), 0)
                                         FROM bill_payment p
                     JOIN Bills b ON p.BillId = b.BillId
                     WHERE date(p.CreatedAt) = @today
                       AND b.BillPaymentMethod = 'Cash'
                       AND b.Status != 'Cancelled'
                       AND p.Type = 'payment')
                    -
                    (SELECT COALESCE(SUM(p.Amount), 0)
                                         FROM bill_payment p
                                         JOIN Bills b ON p.BillId = b.BillId
                                         WHERE date(p.CreatedAt) = @today
                                            AND b.BillPaymentMethod = 'Cash'
                                             AND b.Status != 'Cancelled'
                                            AND p.Type = 'refund');";
            cmd.Parameters.AddWithValue("@today", todayStr);
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets today's payments received for Online bills only.</summary>
        public double GetTodayOnlinePayments()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            cmd.CommandText = @"
                SELECT 
                                        (SELECT COALESCE(SUM(InitialPayment), 0) 
                     FROM Bills 
                     WHERE date(CreatedAt) = @today 
                       AND BillPaymentMethod = 'Online' 
                       AND Status != 'Cancelled')
                    +
                                        (SELECT COALESCE(SUM(
                                            CASE WHEN p.Type = 'payment' THEN p.Amount
                                                 WHEN p.Type = 'refund' THEN -p.Amount
                                                 ELSE 0 END), 0)
                                         FROM bill_payment p
                     JOIN Bills b ON p.BillId = b.BillId
                     WHERE date(p.CreatedAt) = @today
                       AND b.Status != 'Cancelled'
                      AND b.BillPaymentMethod = 'Online');";
            cmd.Parameters.AddWithValue("@today", todayStr);
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets total return value for a date range.</summary>
        public double GetReturnsTotalByDateRange(DateTime from, DateTime to)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(RefundAmount), 0) FROM BillReturns
                WHERE datetime(ReturnedAt, 'localtime') >= @from AND datetime(ReturnedAt, 'localtime') < @to;";
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets sale bills only (no returns) for a date range.</summary>
        public List<Bill> GetSalesOnlyByDateRange(DateTime from, DateTime to)
        {
            // In 3NF, all entries in Bills are sales. Returns are separate.
            return GetByDateRange(from, to);
        }

        /// <summary>Gets total outstanding credit across all customers.</summary>
        public double GetOutstandingCreditTotal()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(
                    (SELECT COALESCE(SUM(bi.Quantity * bi.UnitPrice), 0) FROM BillItems bi WHERE bi.BillId = b.BillId)
                    + COALESCE(b.TaxAmount, 0)
                    - COALESCE(b.DiscountAmount, 0)
                    - COALESCE((SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0)
                                FROM BillReturnItems bri
                                JOIN BillReturns br ON bri.ReturnId = br.ReturnId
                                WHERE br.BillId = b.BillId), 0)
                    - COALESCE(b.InitialPayment, 0)
                    - COALESCE((SELECT SUM(p.Amount) FROM bill_payment p WHERE p.BillId = b.BillId AND p.Type = 'payment'), 0)
                ), 0)
                FROM Bills b
                WHERE b.Status != 'Cancelled';";
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets today's bill count.</summary>
        public int GetTodayCount()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Bills WHERE date(CreatedAt, 'localtime') = date('now', 'localtime');";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>Gets the next available bill ID.</summary>
        public int GetNextBillId()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(BillId), 0) + 1 FROM Bills;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }



        public void UpdateBillStatus(int billId, string status, SqliteConnection? conn = null, SqliteTransaction? txn = null)
        {
            bool manageConnection = (conn == null);
            if (manageConnection) conn = DatabaseHelper.GetConnection();

            try
            {
                using var cmd = conn!.CreateCommand();
                if (txn != null) cmd.Transaction = txn;
                cmd.CommandText = "UPDATE Bills SET Status = @status WHERE BillId = @id;";
                cmd.Parameters.AddWithValue("@status", status);
                cmd.Parameters.AddWithValue("@id", billId);
                cmd.ExecuteNonQuery();
            }
            finally
            {
                if (manageConnection) conn?.Dispose();
            }
        }

        public List<Bill> GetReturnsByParentId(int parentId)
        {
            // New 3NF logic: returns have their own table, we can wrap them as virtual 'Bill' objects for UI
            var bills = new List<Bill>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT r.ReturnId as BillId, r.ReturnedAt as CreatedAt, r.BillId as ParentId,
                       (SELECT UserId FROM Bills WHERE BillId = r.BillId) as UserId,
                       r.RefundAmount as TotalAmount
                FROM BillReturns r
                WHERE r.BillId = @pid
                ORDER BY r.ReturnedAt ASC;";
            cmd.Parameters.AddWithValue("@pid", parentId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                bills.Add(new Bill
                {
                    BillId = reader.GetInt32(0),
                    CreatedAt = reader.GetDateTime(1),
                    SubTotal = -reader.GetDouble(4), // Return value is negative
                    PaidAmount = reader.GetDouble(4), // Refunded cash
                    Status = "Completed",
                    Type = "Return",
                    ParentBillId = reader.GetInt32(2)
                });
            }
            return bills;
        }

        public List<Bill> GetBillsByCustomerId(int customerId)
        {
            var bills = new List<Bill>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT b.*, 
                       u.Username, u.FullName as UserFullName, u.Role as UserRole,
                       c.FullName as CustomerName, c.Phone as CustomerPhone, c.Address as CustomerAddress,
                       a.AccountTitle, a.AccountType, a.BankName,
                        (SELECT COALESCE(SUM(Quantity * UnitPrice), 0) FROM BillItems WHERE BillId = b.BillId) as SubTotal,
                        (SELECT COALESCE(SUM(CASE WHEN p.Type = 'payment' THEN p.Amount WHEN p.Type = 'refund' THEN -p.Amount ELSE 0 END), 0)
                         FROM bill_payment p
                         WHERE p.BillId = b.BillId) as AdditionalPaid,
                        (SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0) FROM BillReturnItems bri JOIN BillReturns br ON bri.ReturnId = br.ReturnId WHERE br.BillId = b.BillId) as TotalReturned,
                        b.BillPaymentMethod as PaymentMethod
                FROM Bills b
                LEFT JOIN Users u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                LEFT JOIN Accounts a ON b.AccountId = a.Id
                WHERE b.CustomerId = @cid
                ORDER BY b.CreatedAt DESC;";
            cmd.Parameters.AddWithValue("@cid", customerId);

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var bill = MapBill(reader);
                    if (bill != null) bills.Add(bill);
                }
            }
            LoadLineItems(conn, bills);
            return bills;
        }

        public Bill? GetLatestBillByCustomerId(int customerId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT b.*, 
                       u.Username, u.FullName as UserFullName, u.Role as UserRole,
                       c.FullName as CustomerName, c.Phone as CustomerPhone, c.Address as CustomerAddress,
                       a.AccountTitle, a.AccountType, a.BankName,
                        (SELECT COALESCE(SUM(Quantity * UnitPrice), 0) FROM BillItems WHERE BillId = b.BillId) as SubTotal,
                        (SELECT COALESCE(SUM(CASE WHEN p.Type = 'payment' THEN p.Amount WHEN p.Type = 'refund' THEN -p.Amount ELSE 0 END), 0)
                         FROM bill_payment p
                         WHERE p.BillId = b.BillId) as AdditionalPaid,
                        (SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0) FROM BillReturnItems bri JOIN BillReturns br ON bri.ReturnId = br.ReturnId WHERE br.BillId = b.BillId) as TotalReturned,
                        b.BillPaymentMethod as PaymentMethod
                FROM Bills b
                LEFT JOIN Users u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                LEFT JOIN Accounts a ON b.AccountId = a.Id
                WHERE b.CustomerId = @cid
                ORDER BY b.CreatedAt DESC LIMIT 1;";
            cmd.Parameters.AddWithValue("@cid", customerId);

            Bill? bill = null;
            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                    bill = MapBill(reader);
            }
            if (bill != null)
                LoadLineItems(conn, new List<Bill> { bill });

            return bill;
        }

        public (int BillCount, double TotalAmount) GetCustomerStats(int customerId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*), COALESCE(SUM(bill_total), 0)
                FROM (
                    SELECT (SUM(bi.Quantity * bi.UnitPrice) + COALESCE(b.TaxAmount, 0) - COALESCE(b.DiscountAmount, 0)
                            - COALESCE((SELECT SUM(bri.Quantity * bri.UnitPrice)
                                        FROM BillReturnItems bri
                                        JOIN BillReturns br ON bri.ReturnId = br.ReturnId
                                        WHERE br.BillId = b.BillId), 0)) as bill_total
                    FROM Bills b
                    JOIN BillItems bi ON b.BillId = bi.BillId
                    WHERE b.CustomerId = @cid AND b.Status != 'Cancelled'
                    GROUP BY b.BillId
                );";
            cmd.Parameters.AddWithValue("@cid", customerId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return (reader.GetInt32(0), reader.GetDouble(1));
            return (0, 0);
        }


        // ────────────────────────────────────────────
        //  Helper: get line items for a bill
        // ────────────────────────────────────────────

        private List<BillDescription> GetBillItems(SqliteConnection conn, int billId)
        {
            var items = new List<BillDescription>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT bi.*, i.Barcode, i.Description AS ItemDesc
                FROM BillItems bi
                LEFT JOIN Items i ON bi.ItemId = i.ItemId
                WHERE bi.BillId = @billId;
            ";
            cmd.Parameters.AddWithValue("@billId", billId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var barcodeOrd = reader.GetOrdinal("Barcode");
                items.Add(new BillDescription
                {
                    BillItemId      = reader.GetInt32(reader.GetOrdinal("BillItemId")),
                    BillId          = reader.GetInt32(reader.GetOrdinal("BillId")),
                    ItemInternalId  = reader.GetInt32(reader.GetOrdinal("ItemId")),
                    ItemId          = reader.GetInt32(reader.GetOrdinal("ItemId")).ToString(),
                    Barcode         = reader.IsDBNull(barcodeOrd) ? null : reader.GetString(barcodeOrd),
                    ItemDescription = reader.IsDBNull(reader.GetOrdinal("ItemDesc")) ? "" : reader.GetString(reader.GetOrdinal("ItemDesc")),
                    Quantity        = reader.GetDouble(reader.GetOrdinal("Quantity")),
                    UnitPrice       = reader.GetDouble(reader.GetOrdinal("UnitPrice")),
                    DiscountAmount  = reader.GetDouble(reader.GetOrdinal("DiscountAmount")),
                    TotalPrice      = reader.GetDouble(reader.GetOrdinal("Quantity")) * 
                                     reader.GetDouble(reader.GetOrdinal("UnitPrice")) - 
                                     reader.GetDouble(reader.GetOrdinal("DiscountAmount"))
                });
            }
            return items;
        }
        /// <summary>
        /// Batched load of line items for a list of bills to avoid N+1 query performance issues.
        /// </summary>
        private void LoadLineItems(SqliteConnection conn, List<Bill> bills)
        {
            if (bills == null || !bills.Any()) return;

            var billDict = bills.ToDictionary(b => b.BillId);
            var billIds = string.Join(",", billDict.Keys);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT bi.*, i.Barcode as ItemBarcode, i.Description as ItemName
                FROM BillItems bi
                JOIN Items i ON bi.ItemId = i.ItemId
                WHERE bi.BillId IN ({billIds});";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var billId = reader.GetInt32(reader.GetOrdinal("BillId"));
                if (billDict.TryGetValue(billId, out var bill))
                {
                    var barcodeOrd = reader.GetOrdinal("ItemBarcode");
                    bill.Items.Add(new BillDescription
                    {
                        BillItemId     = reader.GetInt32(reader.GetOrdinal("BillItemId")),
                        BillId         = billId,
                        ItemInternalId = reader.GetInt32(reader.GetOrdinal("ItemId")),
                        ItemId         = reader.GetInt32(reader.GetOrdinal("ItemId")).ToString(),
                        Barcode        = reader.IsDBNull(barcodeOrd) ? null : reader.GetString(barcodeOrd),
                        ItemDescription = reader.IsDBNull(reader.GetOrdinal("ItemName")) ? "" : reader.GetString(reader.GetOrdinal("ItemName")),
                        Quantity       = reader.GetDouble(reader.GetOrdinal("Quantity")),
                        UnitPrice      = reader.GetDouble(reader.GetOrdinal("UnitPrice")),
                        DiscountAmount = reader.GetDouble(reader.GetOrdinal("DiscountAmount")),
                        TotalPrice     = reader.GetDouble(reader.GetOrdinal("Quantity")) * 
                                         reader.GetDouble(reader.GetOrdinal("UnitPrice")) - 
                                         reader.GetDouble(reader.GetOrdinal("DiscountAmount"))
                    });
                }
            }
        }

        public void LoadAuditLogs(Bill bill)
        {
            using var conn = DatabaseHelper.GetConnection();
            LoadAuditLogs(bill, conn);
        }

        private void LoadAuditLogs(Bill bill, SqliteConnection conn)
        {
            // Clear existing logs to prevent duplicated items on refresh (The 'Stacking' Bug)
            bill.PaymentLogs.Clear();
            bill.ReturnLogs.Clear();

            if (bill.InitialPayment > 0)
            {
                bill.PaymentLogs.Add(new CreditPayment
                {
                    PaymentId = 0,
                    BillId = bill.BillId,
                    AmountPaid = bill.InitialPayment,
                    PaymentMethod = bill.PaymentMethod,
                    TransactionType = "Sale",
                    Note = "initial payment",
                    PaidAt = bill.CreatedAt
                });
            }

            // 1. Load Payment History
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT PaymentId, Amount, Type, CreatedAt
                    FROM bill_payment
                    WHERE BillId = @bid
                    ORDER BY CreatedAt ASC;";
                cmd.Parameters.AddWithValue("@bid", bill.BillId);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var type = reader.GetString(2);
                    bill.PaymentLogs.Add(new CreditPayment
                    {
                        PaymentId       = reader.GetInt32(0),
                        BillId          = bill.BillId,
                        AmountPaid      = reader.GetDouble(1),
                        TransactionType = type == "refund" ? "Refund" : "Payment",
                        PaidAt          = reader.GetDateTime(3)
                    });
                }
            }

            // 2. Load Return History (Headers)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT ReturnId, RefundAmount, ReturnedAt
                    FROM BillReturns
                    WHERE BillId = @bid
                    ORDER BY ReturnedAt ASC;";
                cmd.Parameters.AddWithValue("@bid", bill.BillId);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var ret = new ReturnAuditGroup
                    {
                        ReturnId     = reader.GetInt32(0),
                        RefundAmount = reader.GetDouble(1),
                        ReturnedAt   = reader.GetDateTime(2)
                    };
                    bill.ReturnLogs.Add(ret);
                }
            }

            // 3. Load Return Items for each return
            foreach (var ret in bill.ReturnLogs)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT ri.ReturnItemId, ri.BillItemId, ri.Quantity, ri.UnitPrice, i.Description
                    FROM BillReturnItems ri
                    JOIN BillItems bi ON ri.BillItemId = bi.BillItemId
                    JOIN Items i ON bi.ItemId = i.ItemId
                    WHERE ri.ReturnId = @rid;";
                cmd.Parameters.AddWithValue("@rid", ret.ReturnId);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    ret.Items.Add(new BillReturnItemAudit
                    {
                        ItemDescription = reader.GetString(4),
                        Quantity        = Convert.ToInt32(reader.GetDouble(2)),
                        UnitPrice       = reader.GetDouble(3)
                    });
                }
            }
        }

        // ────────────────────────────────────────────
        //  Mapper
        // ────────────────────────────────────────────

        public Bill? MapBill(SqliteDataReader reader)
        {
            var bill = new Bill
            {
                BillId         = reader.GetInt32(reader.GetOrdinal("BillId")),
                CustomerId     = reader.IsDBNull(reader.GetOrdinal("CustomerId")) ? null : (int?)reader.GetInt32(reader.GetOrdinal("CustomerId")),
                UserId         = reader.IsDBNull(reader.GetOrdinal("UserId")) ? null : (int?)reader.GetInt32(reader.GetOrdinal("UserId")),
                TaxAmount      = reader.GetDouble(reader.GetOrdinal("TaxAmount")),
                DiscountAmount = reader.GetDouble(reader.GetOrdinal("DiscountAmount")),
                Status         = reader.GetString(reader.GetOrdinal("Status")),
                CreatedAt      = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                PaymentMethod  = reader.HasColumn("BillPaymentMethod") && !reader.IsDBNull(reader.GetOrdinal("BillPaymentMethod"))
                    ? reader.GetString(reader.GetOrdinal("BillPaymentMethod"))
                    : reader.HasColumn("PaymentMethod") && !reader.IsDBNull(reader.GetOrdinal("PaymentMethod"))
                        ? reader.GetString(reader.GetOrdinal("PaymentMethod"))
                        : "Cash",
                OnlinePaymentMethod = reader.HasColumn("OnlinePaymentMethod") && !reader.IsDBNull(reader.GetOrdinal("OnlinePaymentMethod")) ? reader.GetString(reader.GetOrdinal("OnlinePaymentMethod")) : null,
                AccountId      = reader.HasColumn("AccountId") && !reader.IsDBNull(reader.GetOrdinal("AccountId")) ? (int?)reader.GetInt32(reader.GetOrdinal("AccountId")) : null
            };

            // Calculated aggregates (if present in query)
            if (reader.HasColumn("SubTotal")) bill.SubTotal = reader.GetDouble(reader.GetOrdinal("SubTotal"));
            double additionalPaid = 0;
            if (reader.HasColumn("AdditionalPaid")) additionalPaid = reader.GetDouble(reader.GetOrdinal("AdditionalPaid"));
            if (reader.HasColumn("TotalReturned")) bill.ReturnedAmount = reader.GetDouble(reader.GetOrdinal("TotalReturned"));

            // Read InitialPayment if column exists
            if (reader.HasColumn("InitialPayment") && !reader.IsDBNull(reader.GetOrdinal("InitialPayment")))
                bill.InitialPayment = reader.GetDouble(reader.GetOrdinal("InitialPayment"));

            bill.PaidAmount = Math.Round(bill.InitialPayment + additionalPaid, 2);

            // Map Account navigation
            if (bill.AccountId.HasValue && reader.HasColumn("AccountTitle") && !reader.IsDBNull(reader.GetOrdinal("AccountTitle")))
            {
                bill.Account = new Account
                {
                    Id           = bill.AccountId.Value,
                    AccountTitle = reader.GetString(reader.GetOrdinal("AccountTitle")),
                    AccountType  = reader.GetString(reader.GetOrdinal("AccountType")),
                    BankName     = reader.IsDBNull(reader.GetOrdinal("BankName")) ? null : reader.GetString(reader.GetOrdinal("BankName"))
                };
            }

            // Mapping Customer navigation
            if (bill.CustomerId.HasValue && reader.HasColumn("CustomerName") && !reader.IsDBNull(reader.GetOrdinal("CustomerName")))
            {
                bill.Customer = new Customer
                {
                    CustomerId = bill.CustomerId.Value,
                    FullName   = reader.GetString(reader.GetOrdinal("CustomerName")),
                    Phone      = reader.GetString(reader.GetOrdinal("CustomerPhone")),
                    Address    = reader.IsDBNull(reader.GetOrdinal("CustomerAddress")) ? null : reader.GetString(reader.GetOrdinal("CustomerAddress"))
                };
                bill.BillingAddress = bill.Customer.Address;
            }

            // Mapping User navigation
            if (bill.UserId.HasValue && reader.HasColumn("Username") && !reader.IsDBNull(reader.GetOrdinal("Username")))
            {
                bill.User = new User
                {
                    Id       = bill.UserId.Value,
                    Username = reader.GetString(reader.GetOrdinal("Username")),
                    FullName = reader.GetString(reader.GetOrdinal("UserFullName")),
                    Role     = reader.GetString(reader.GetOrdinal("UserRole"))
                };
            }

            return bill;
        }

        public void UpdatePrintStatus(int billId, bool isPrinted, DateTime? printedAt)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Bills 
                SET IsPrinted = @isPrinted, 
                    PrintedAt = @printedAt 
                WHERE BillId = @id;";
            cmd.Parameters.AddWithValue("@isPrinted", isPrinted ? 1 : 0);
            cmd.Parameters.AddWithValue("@printedAt", printedAt.HasValue ? (object)printedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
            cmd.Parameters.AddWithValue("@id", billId);
            cmd.ExecuteNonQuery();
        }

        // ────────────────────────────────────────────
        //  CREDIT / UDHAR read operations
        // ────────────────────────────────────────────

        /// <summary>
        /// Returns total online payments grouped by OnlinePaymentMethod for the given date range.
        /// Only counts Sale payments (excludes Return Offset) on Online bills.
        /// </summary>
        public Dictionary<string, double> GetOnlinePaymentBreakdown(DateTime from, DateTime to)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(a.AccountTitle, b.OnlinePaymentMethod, '(Unspecified)') as Method,
                       COALESCE(SUM(p.Amount), 0) as Total
                FROM Bills b
                                JOIN bill_payment p ON b.BillId = p.BillId
                LEFT JOIN Accounts a ON b.AccountId = a.Id
                WHERE b.BillPaymentMethod = 'Online'
                  AND b.CreatedAt >= @from AND b.CreatedAt < @to
                  AND b.Status != 'Cancelled'
                                    AND p.Type = 'payment'
                GROUP BY COALESCE(a.AccountTitle, b.OnlinePaymentMethod);
            ";
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result[reader.GetString(0)] = reader.GetDouble(1);

            return result;
        }

        /// <summary>Returns all credit bills for a customer, newest first.</summary>
        public List<Bill> GetCreditBillsByCustomer(int customerId)
        {
            var allBills = GetBillsByCustomerId(customerId);
            return allBills.Where(b => b.RemainingAmount > 0 && b.Status != "Cancelled").ToList();
        }

        /// <summary>
        /// Returns all bills for a customer for the ledger view (all statuses, Sale type only).
        /// </summary>
        public List<Bill> GetLedgerByCustomer(int customerId)
        {
            return GetBillsByCustomerId(customerId);
        }
    }
}
