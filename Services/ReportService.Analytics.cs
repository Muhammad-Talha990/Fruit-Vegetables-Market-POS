using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using FruitVegetableMarketPOS.Data;
using FruitVegetableMarketPOS.Data.Repositories;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Services
{
    /// <summary>
    /// Wholesale-style analytics over Bills / BillItems / bill_payment.
    /// Opening-balance bills and cancelled sales are excluded.
    /// </summary>
    public partial class ReportService
    {
        private const string BillNetSql = @"
            (COALESCE((SELECT SUM((bi.Quantity * bi.UnitPrice) - COALESCE(bi.DiscountAmount, 0))
                       FROM BillItems bi WHERE bi.BillId = b.BillId), 0)
             + COALESCE(b.TaxAmount, 0)
             - COALESCE(b.DiscountAmount, 0))";

        private const string BillPaidSql = @"
            (COALESCE(b.InitialPayment, 0)
             + COALESCE((SELECT SUM(p.Amount) FROM bill_payment p
                         WHERE p.BillId = b.BillId AND LOWER(TRIM(p.Type)) = 'payment'), 0)
             - COALESCE((SELECT SUM(rf.Amount) FROM bill_payment rf
                         WHERE rf.BillId = b.BillId AND LOWER(TRIM(rf.Type)) = 'refund'), 0))";

        private const string BillAppliedSql = $@"
            CASE
                WHEN {BillPaidSql} <= 0 THEN 0
                WHEN {BillNetSql} > 0.01 AND {BillPaidSql} > {BillNetSql} THEN {BillNetSql}
                ELSE {BillPaidSql}
            END";

        public Bill? GetBillById(int billId) => _billRepo.GetById(billId);

        public List<Bill> GetSaleBills(DateTime from, DateTime to, string? paymentMethod = null, int? accountId = null, string? customerId = null)
        {
            var list = new List<Bill>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            var customerFilter = "";
            if (customerId == "")
                customerFilter = " AND b.CustomerId IS NULL";
            else if (!string.IsNullOrWhiteSpace(customerId))
                customerFilter = " AND b.CustomerId = @cid";

            cmd.CommandText = $@"
                SELECT b.*,
                       u.Username, u.FullName as UserFullName, u.Role as UserRole,
                       c.FullName as CustomerName, c.Phone as CustomerPhone, c.Address as CustomerAddress,
                       a.AccountTitle, a.AccountType, a.BankName,
                       (SELECT COALESCE(SUM(Quantity * UnitPrice), 0) FROM BillItems WHERE BillId = b.BillId) as SubTotal,
                       (SELECT COALESCE(SUM(CASE WHEN p.Type = 'payment' THEN p.Amount ELSE 0 END), 0)
                        FROM bill_payment p WHERE p.BillId = b.BillId) as AdditionalPaid,
                       (SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0)
                        FROM BillReturnItems bri
                        JOIN BillReturns br ON bri.ReturnId = br.ReturnId
                        WHERE br.BillId = b.BillId) as TotalReturned
                FROM Bills b
                LEFT JOIN Users u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                LEFT JOIN Accounts a ON b.AccountId = a.Id
                WHERE {SaleWhere("b", paymentMethod, accountId)}{customerFilter}
                ORDER BY b.CreatedAt DESC;";
            AddRangeParams(cmd, from, to, accountId);
            if (!string.IsNullOrWhiteSpace(customerId) && int.TryParse(customerId, out var cid))
                cmd.Parameters.AddWithValue("@cid", cid);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var bill = _billRepo.MapBill(reader);
                if (bill != null) list.Add(bill);
            }
            return list;
        }

        public ReportKpis GetKpis(DateTime from, DateTime to, string? paymentMethod = null, int? accountId = null)
        {
            var kpis = new ReportKpis();
            using var conn = DatabaseHelper.GetConnection();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    SELECT
                        COUNT(*) AS Bills,
                        COALESCE(SUM({BillNetSql} - COALESCE(b.TaxAmount, 0) + COALESCE(b.DiscountAmount, 0)), 0) AS Gross,
                        COALESCE(SUM(COALESCE(b.DiscountAmount, 0)), 0) AS Disc,
                        COALESCE(SUM({BillNetSql}), 0) AS Net
                    FROM Bills b
                    WHERE {SaleWhere("b", paymentMethod, accountId)};";
                AddRangeParams(cmd, from, to, accountId);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    kpis.BillCount = Convert.ToInt32(r["Bills"]);
                    kpis.GrossSales = Math.Round(Convert.ToDouble(r["Gross"]), 2);
                    kpis.Discounts = Math.Round(Convert.ToDouble(r["Disc"]), 2);
                    kpis.NetSales = Math.Round(Convert.ToDouble(r["Net"]), 2);
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    SELECT COALESCE(SUM(bi.Quantity), 0)
                    FROM BillItems bi
                    INNER JOIN Bills b ON b.BillId = bi.BillId
                    WHERE {SaleWhere("b", paymentMethod, accountId)};";
                AddRangeParams(cmd, from, to, accountId);
                kpis.QuantitySold = Convert.ToDouble(cmd.ExecuteScalar());
            }

            kpis.AverageBillValue = kpis.BillCount > 0 ? Math.Round(kpis.NetSales / kpis.BillCount, 2) : 0;
            ApplyReceivedKpis(kpis, from, to, paymentMethod, accountId);
            kpis.OutstandingCredit = GetOutstandingCreditTotal();
            return kpis;
        }

        public List<(DateTime Date, double Revenue, double Quantity, int Bills)> GetDailySeries(
            DateTime from, DateTime to, string? paymentMethod = null, int? accountId = null)
        {
            var list = new List<(DateTime, double, double, int)>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT date(b.CreatedAt) AS D,
                       COALESCE(SUM({BillNetSql}), 0) AS Rev,
                       COUNT(*) AS Bills
                FROM Bills b
                WHERE {SaleWhere("b", paymentMethod, accountId)}
                GROUP BY date(b.CreatedAt)
                ORDER BY D;";
            AddRangeParams(cmd, from, to, accountId);
            var days = new Dictionary<string, (double Rev, int Bills)>(StringComparer.Ordinal);
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    var key = SqliteDateKey(r, 0);
                    if (key.Length == 0) continue;
                    days[key] = (Convert.ToDouble(r["Rev"]), Convert.ToInt32(r["Bills"]));
                }
            }

            using var qtyCmd = conn.CreateCommand();
            qtyCmd.CommandText = $@"
                SELECT date(b.CreatedAt) AS D, COALESCE(SUM(bi.Quantity), 0) AS Qty
                FROM BillItems bi
                INNER JOIN Bills b ON b.BillId = bi.BillId
                WHERE {SaleWhere("b", paymentMethod, accountId)}
                GROUP BY date(b.CreatedAt);";
            AddRangeParams(qtyCmd, from, to, accountId);
            var qty = new Dictionary<string, double>(StringComparer.Ordinal);
            using (var qr = qtyCmd.ExecuteReader())
            {
                while (qr.Read())
                {
                    var key = SqliteDateKey(qr, 0);
                    if (key.Length == 0) continue;
                    qty[key] = Convert.ToDouble(qr[1]);
                }
            }

            var spanDays = Math.Max(1, (to.Date - from.Date).Days);
            if (spanDays <= 45)
            {
                for (var d = from.Date; d < to.Date; d = d.AddDays(1))
                {
                    var key = d.ToString("yyyy-MM-dd");
                    days.TryGetValue(key, out var stats);
                    qty.TryGetValue(key, out var q);
                    list.Add((d, stats.Rev, q, stats.Bills));
                }
            }
            else
            {
                foreach (var kv in days)
                {
                    if (!DateTime.TryParse(kv.Key, out var d)) continue;
                    qty.TryGetValue(kv.Key, out var q);
                    list.Add((d, kv.Value.Rev, q, kv.Value.Bills));
                }
                list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            }
            return list;
        }

        public List<MonthlyBucket> GetMonthlyBuckets(DateTime from, DateTime to, string? paymentMethod = null, int? accountId = null)
        {
            var map = new Dictionary<string, MonthlyBucket>(StringComparer.Ordinal);
            using var conn = DatabaseHelper.GetConnection();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    SELECT strftime('%Y-%m', b.CreatedAt) AS M,
                           COALESCE(SUM({BillNetSql}), 0) AS Rev,
                           COUNT(*) AS Bills
                    FROM Bills b
                    WHERE {SaleWhere("b", paymentMethod, accountId)}
                    GROUP BY M
                    ORDER BY M;";
                AddRangeParams(cmd, from, to, accountId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var bucket = EnsureMonth(map, r.GetString(0));
                    bucket.Revenue = Convert.ToDouble(r["Rev"]);
                    bucket.Bills = Convert.ToInt32(r["Bills"]);
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    SELECT strftime('%Y-%m', b.CreatedAt) AS M, COALESCE(SUM(bi.Quantity), 0)
                    FROM BillItems bi
                    INNER JOIN Bills b ON b.BillId = bi.BillId
                    WHERE {SaleWhere("b", paymentMethod, accountId)}
                    GROUP BY M;";
                AddRangeParams(cmd, from, to, accountId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    EnsureMonth(map, r.GetString(0)).Quantity = Convert.ToDouble(r[1]);
            }

            var list = new List<MonthlyBucket>(map.Values);
            list.Sort((a, b) => string.CompareOrdinal(a.MonthKey, b.MonthKey));
            return list;
        }

        public List<ItemSalesRow> GetItemSales(DateTime from, DateTime to, string? itemSearch = null,
            string? paymentMethod = null, int? accountId = null)
        {
            var rows = new List<ItemSalesRow>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            var itemFilter = string.IsNullOrWhiteSpace(itemSearch)
                ? ""
                : " AND (IFNULL(bi.ItemName,'') LIKE @item OR IFNULL(i.Description,'') LIKE @item OR IFNULL(i.NameUrdu,'') LIKE @item OR IFNULL(bi.TypeName,'') LIKE @item)";
            cmd.CommandText = $@"
                SELECT
                    'id:' || CAST(bi.ItemId AS TEXT) || ':' || LOWER(TRIM(COALESCE(bi.TypeName, ''))) AS ItemKey,
                    MAX(COALESCE(NULLIF(bi.ItemName, ''), i.Description, 'Unknown')) AS ItemName,
                    MAX(i.NameUrdu) AS NameUrdu,
                    MAX(COALESCE(NULLIF(bi.TypeName, ''), '')) AS TypeName,
                    SUM(bi.Quantity) AS Qty,
                    SUM((bi.Quantity * bi.UnitPrice) - COALESCE(bi.DiscountAmount, 0)) AS Revenue,
                    COUNT(*) AS Lines,
                    COUNT(DISTINCT bi.BillId) AS Bills
                FROM BillItems bi
                INNER JOIN Bills b ON b.BillId = bi.BillId
                LEFT JOIN Items i ON i.ItemId = bi.ItemId
                WHERE {SaleWhere("b", paymentMethod, accountId)}{itemFilter}
                GROUP BY ItemKey
                ORDER BY Revenue DESC;";
            AddRangeParams(cmd, from, to, accountId);
            if (!string.IsNullOrWhiteSpace(itemSearch))
                cmd.Parameters.AddWithValue("@item", $"%{itemSearch.Trim()}%");

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.GetString(1);
                var typeName = r.IsDBNull(3) ? "" : r.GetString(3);
                if (!string.IsNullOrWhiteSpace(typeName))
                    name = $"{name} - {typeName}";
                rows.Add(new ItemSalesRow
                {
                    ItemKey = r.GetString(0),
                    ItemName = name,
                    NameUrdu = r.IsDBNull(2) ? null : r.GetString(2),
                    QuantitySold = Convert.ToDouble(r["Qty"]),
                    Revenue = Convert.ToDouble(r["Revenue"]),
                    LineCount = Convert.ToInt32(r["Lines"]),
                    BillCount = Convert.ToInt32(r["Bills"])
                });
            }
            return rows;
        }

        public List<ItemLineDetail> GetItemLineDetails(DateTime from, DateTime to, string itemKey,
            string? paymentMethod = null, int? accountId = null)
        {
            var rows = new List<ItemLineDetail>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT b.BillId, b.CreatedAt, bi.Quantity, bi.UnitPrice,
                       ((bi.Quantity * bi.UnitPrice) - COALESCE(bi.DiscountAmount, 0)) AS LineTotal
                FROM BillItems bi
                INNER JOIN Bills b ON b.BillId = bi.BillId
                WHERE {SaleWhere("b", paymentMethod, accountId)}
                  AND ('id:' || CAST(bi.ItemId AS TEXT) || ':' || LOWER(TRIM(COALESCE(bi.TypeName, '')))) = @key
                ORDER BY b.CreatedAt, bi.BillItemId;";
            AddRangeParams(cmd, from, to, accountId);
            cmd.Parameters.AddWithValue("@key", itemKey ?? "");
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetInt32(0);
                rows.Add(new ItemLineDetail
                {
                    InternalBillId = id,
                    BillId = id.ToString("D5"),
                    BillDateTime = r.IsDBNull(1) ? DateTime.Now : Convert.ToDateTime(r.GetValue(1)),
                    Quantity = Convert.ToDouble(r["Quantity"]),
                    Price = Convert.ToDouble(r["UnitPrice"]),
                    LineTotal = Convert.ToDouble(r["LineTotal"])
                });
            }
            return rows;
        }

        public List<PaymentMethodRow> GetPaymentBreakdown(DateTime from, DateTime to,
            string? paymentMethod = null, int? accountId = null)
        {
            var map = new Dictionary<string, PaymentMethodRow>(StringComparer.OrdinalIgnoreCase);
            void Add(string method, int count, double amount)
            {
                var key = string.IsNullOrWhiteSpace(method) ? "Cash" : method.Trim();
                key = key.Equals("Cash", StringComparison.OrdinalIgnoreCase) ? "Cash" : "Online";
                if (!map.TryGetValue(key, out var row))
                {
                    row = new PaymentMethodRow { Method = key };
                    map[key] = row;
                }
                row.TransactionCount += count;
                row.Amount += amount;
            }

            using var conn = DatabaseHelper.GetConnection();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    SELECT COALESCE(NULLIF(TRIM(b.BillPaymentMethod), ''), 'Cash') AS Method,
                           COUNT(*) AS N,
                           COALESCE(SUM({BillAppliedSql}), 0) AS Amt
                    FROM Bills b
                    WHERE {SaleWhere("b", paymentMethod, accountId)}
                    GROUP BY Method;";
                AddRangeParams(cmd, from, to, accountId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    Add(r.GetString(0), Convert.ToInt32(r["N"]), Convert.ToDouble(r["Amt"]));
            }

            if (!accountId.HasValue)
            {
                using var cmd = conn.CreateCommand();
                var payFilter = "";
                if (!string.IsNullOrWhiteSpace(paymentMethod))
                {
                    payFilter = paymentMethod.Equals("Cash", StringComparison.OrdinalIgnoreCase)
                        ? " AND LOWER(TRIM(COALESCE(p.PaymentMethod, 'Cash'))) = 'cash'"
                        : " AND LOWER(TRIM(COALESCE(p.PaymentMethod, 'Cash'))) != 'cash'";
                }
                cmd.CommandText = $@"
                    SELECT COALESCE(NULLIF(TRIM(p.PaymentMethod), ''), 'Cash') AS Method,
                           COUNT(*) AS N,
                           COALESCE(SUM(p.Amount), 0) AS Amt
                    FROM bill_payment p
                    INNER JOIN Bills b ON b.BillId = p.BillId
                    WHERE datetime(p.CreatedAt) >= datetime(@from) AND datetime(p.CreatedAt) < datetime(@to)
                      AND LOWER(TRIM(p.Type)) = 'payment'
                      AND datetime(p.CreatedAt) > datetime(b.CreatedAt)
                      AND b.Status != 'Cancelled'{payFilter}
                    GROUP BY Method;";
                cmd.Parameters.AddWithValue("@from", from.ToDbString());
                cmd.Parameters.AddWithValue("@to", to.ToDbString());
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    Add(r.IsDBNull(0) ? "Cash" : r.GetString(0), Convert.ToInt32(r["N"]), Convert.ToDouble(r["Amt"]));
            }

            var total = 0d;
            foreach (var row in map.Values) total += row.Amount;
            foreach (var row in map.Values)
                row.Percent = total > 0 ? row.Amount / total * 100 : 0;

            var list = new List<PaymentMethodRow>(map.Values);
            list.Sort((a, b) => b.Amount.CompareTo(a.Amount));
            return list;
        }

        public List<ReceiptLedgerRow> GetReceiptLedger(DateTime from, DateTime to,
            string? paymentMethod = null, int? accountId = null)
        {
            var rows = new List<ReceiptLedgerRow>();
            foreach (var bill in GetSaleBills(from, to, paymentMethod, accountId))
            {
                rows.Add(new ReceiptLedgerRow
                {
                    Kind = "Bill",
                    BillId = bill.InvoiceDisplay,
                    DateTime = bill.BillDateTime,
                    CustomerName = bill.CustomerDisplayName ?? "",
                    Method = bill.PaymentDisplayText,
                    Received = bill.AppliedReceived
                });
            }

            if (!accountId.HasValue)
            {
                using var conn = DatabaseHelper.GetConnection();
                using var cmd = conn.CreateCommand();
                var payFilter = "";
                if (!string.IsNullOrWhiteSpace(paymentMethod))
                {
                    payFilter = paymentMethod.Equals("Cash", StringComparison.OrdinalIgnoreCase)
                        ? " AND LOWER(TRIM(COALESCE(p.PaymentMethod, 'Cash'))) = 'cash'"
                        : " AND LOWER(TRIM(COALESCE(p.PaymentMethod, 'Cash'))) != 'cash'";
                }
                cmd.CommandText = $@"
                    SELECT p.PaymentId, p.BillId, p.Amount, p.CreatedAt,
                           COALESCE(NULLIF(TRIM(p.PaymentMethod), ''), 'Cash') AS Method,
                           COALESCE(NULLIF(TRIM(c.FullName), ''), 'Walk-in') AS Name
                    FROM bill_payment p
                    INNER JOIN Bills b ON b.BillId = p.BillId
                    LEFT JOIN Customers c ON c.CustomerId = b.CustomerId
                    WHERE datetime(p.CreatedAt) >= datetime(@from) AND datetime(p.CreatedAt) < datetime(@to)
                      AND LOWER(TRIM(p.Type)) = 'payment'
                      AND datetime(p.CreatedAt) > datetime(b.CreatedAt)
                      AND b.Status != 'Cancelled'{payFilter}
                    ORDER BY p.CreatedAt DESC;";
                cmd.Parameters.AddWithValue("@from", from.ToDbString());
                cmd.Parameters.AddWithValue("@to", to.ToDbString());
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var method = r.GetString(4);
                    var isCash = method.Equals("Cash", StringComparison.OrdinalIgnoreCase);
                    rows.Add(new ReceiptLedgerRow
                    {
                        Kind = "Payment",
                        PaymentId = r.GetInt32(0).ToString(),
                        BillId = r.GetInt32(1).ToString("D5"),
                        DateTime = Convert.ToDateTime(r.GetValue(3)),
                        CustomerName = r.GetString(5),
                        Method = isCash ? "Cash" : $"Online ({method})",
                        Received = Math.Round(Convert.ToDouble(r["Amount"]), 2)
                    });
                }
            }

            rows.Sort((a, b) => b.DateTime.CompareTo(a.DateTime));
            return rows;
        }

        public List<AccountReceiptRow> GetAccountReceipts(DateTime from, DateTime to,
            string? paymentMethod = null, int? accountId = null)
        {
            var rows = new List<AccountReceiptRow>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT b.AccountId,
                       COALESCE(NULLIF(TRIM(a.AccountTitle), ''), NULLIF(TRIM(b.OnlinePaymentMethod), ''), 'Online') AS Title,
                       a.AccountType,
                       COUNT(*) AS N,
                       COALESCE(SUM({BillAppliedSql}), 0) AS Amt
                FROM Bills b
                LEFT JOIN Accounts a ON a.Id = b.AccountId
                WHERE {SaleWhere("b", paymentMethod, accountId)}
                  AND LOWER(TRIM(COALESCE(b.BillPaymentMethod, 'Cash'))) != 'cash'
                GROUP BY b.AccountId, Title, a.AccountType
                ORDER BY Amt DESC;";
            AddRangeParams(cmd, from, to, accountId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                rows.Add(new AccountReceiptRow
                {
                    AccountId = r.IsDBNull(0) ? null : r.GetInt32(0),
                    AccountName = r.IsDBNull(1) ? "Online" : r.GetString(1),
                    AccountType = r.IsDBNull(2) ? null : r.GetString(2),
                    TransactionCount = Convert.ToInt32(r["N"]),
                    AmountReceived = Convert.ToDouble(r["Amt"])
                });
            }
            return rows;
        }

        public List<CustomerSalesRow> GetCustomerSales(DateTime from, DateTime to,
            string? paymentMethod = null, int? accountId = null)
        {
            var rows = new List<CustomerSalesRow>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT COALESCE(CAST(b.CustomerId AS TEXT), '') AS Cid,
                       COALESCE(NULLIF(TRIM(c.FullName), ''), 'Walk-in') AS Name,
                       COUNT(*) AS Bills,
                       COALESCE(SUM({BillNetSql}), 0) AS Purchases,
                       COALESCE(SUM({BillAppliedSql}), 0) AS Paid,
                       MAX(b.CreatedAt) AS LastDt
                FROM Bills b
                LEFT JOIN Customers c ON c.CustomerId = b.CustomerId
                WHERE {SaleWhere("b", paymentMethod, accountId)}
                GROUP BY Cid, Name
                ORDER BY Purchases DESC;";
            AddRangeParams(cmd, from, to, accountId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                DateTime? last = null;
                if (!r.IsDBNull(r.GetOrdinal("LastDt")))
                {
                    try { last = Convert.ToDateTime(r.GetValue(r.GetOrdinal("LastDt"))); }
                    catch { /* ignore */ }
                }
                rows.Add(new CustomerSalesRow
                {
                    CustomerId = r.GetString(0),
                    CustomerName = r.GetString(1),
                    BillCount = Convert.ToInt32(r["Bills"]),
                    TotalPurchases = Convert.ToDouble(r["Purchases"]),
                    Payments = Convert.ToDouble(r["Paid"]),
                    LastTransaction = last
                });
            }

            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.CustomerId) || !int.TryParse(row.CustomerId, out var cid))
                    continue;
                using var cred = conn.CreateCommand();
                cred.CommandText = $@"
                    SELECT COALESCE(SUM(
                        CASE WHEN ({BillNetSql} - {BillPaidSql}) > 0
                             THEN ({BillNetSql} - {BillPaidSql}) ELSE 0 END
                    ), 0)
                    FROM Bills b
                    WHERE b.CustomerId = @id AND b.Status != 'Cancelled';";
                cred.Parameters.AddWithValue("@id", cid);
                row.Outstanding = Convert.ToDouble(cred.ExecuteScalar() ?? 0);
            }
            return rows;
        }

        private void ApplyReceivedKpis(ReportKpis kpis, DateTime from, DateTime to, string? paymentMethod, int? accountId)
        {
            var isCashOnly = string.Equals(paymentMethod, "Cash", StringComparison.OrdinalIgnoreCase);
            var isOnlineOnly = !string.IsNullOrWhiteSpace(paymentMethod) && !isCashOnly;

            if (accountId.HasValue)
            {
                kpis.CashReceived = 0;
                kpis.OnlineReceived = Math.Round(SumAppliedReceived(from, to, "Online", accountId), 2);
                kpis.RecoveredCredit = 0;
                return;
            }

            var cash = isOnlineOnly ? 0 : SumAppliedReceived(from, to, "Cash", null)
                + SumRecovered(from, to, cashOnly: true);
            var online = isCashOnly ? 0 : SumAppliedReceived(from, to, "Online", null)
                + SumRecovered(from, to, cashOnly: false);
            kpis.CashReceived = Math.Round(cash, 2);
            kpis.OnlineReceived = Math.Round(online, 2);
            kpis.RecoveredCredit = Math.Round(
                isCashOnly ? SumRecovered(from, to, true)
                : isOnlineOnly ? SumRecovered(from, to, false)
                : SumRecovered(from, to, null), 2);
        }

        private static double SumAppliedReceived(DateTime from, DateTime to, string? paymentMethod, int? accountId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT COALESCE(SUM({BillAppliedSql}), 0)
                FROM Bills b
                WHERE {SaleWhere("b", paymentMethod, accountId)};";
            AddRangeParams(cmd, from, to, accountId);
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        private static double SumRecovered(DateTime from, DateTime to, bool? cashOnly)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            var methodFilter = cashOnly switch
            {
                true => " AND LOWER(TRIM(COALESCE(p.PaymentMethod, 'Cash'))) = 'cash'",
                false => " AND LOWER(TRIM(COALESCE(p.PaymentMethod, 'Cash'))) != 'cash'",
                _ => ""
            };
            cmd.CommandText = $@"
                SELECT COALESCE(SUM(p.Amount), 0)
                FROM bill_payment p
                INNER JOIN Bills b ON b.BillId = p.BillId
                WHERE datetime(p.CreatedAt) >= datetime(@from) AND datetime(p.CreatedAt) < datetime(@to)
                  AND LOWER(TRIM(p.Type)) = 'payment'
                  AND datetime(p.CreatedAt) > datetime(b.CreatedAt)
                  AND b.Status != 'Cancelled'{methodFilter};";
            cmd.Parameters.AddWithValue("@from", from.ToDbString());
            cmd.Parameters.AddWithValue("@to", to.ToDbString());
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        private static string SaleWhere(string b, string? paymentMethod = null, int? accountId = null)
        {
            var sql = $"datetime({b}.CreatedAt) >= datetime(@from) AND datetime({b}.CreatedAt) < datetime(@to)"
                    + $" AND COALESCE({b}.IsOpeningBalance, 0) = 0"
                    + $" AND {b}.Status != 'Cancelled'";
            if (!string.IsNullOrWhiteSpace(paymentMethod))
            {
                sql += paymentMethod.Equals("Cash", StringComparison.OrdinalIgnoreCase)
                    ? $" AND LOWER(TRIM(COALESCE({b}.BillPaymentMethod, 'Cash'))) = 'cash'"
                    : $" AND LOWER(TRIM(COALESCE({b}.BillPaymentMethod, 'Cash'))) != 'cash'";
            }
            if (accountId.HasValue)
                sql += $" AND {b}.AccountId = @acct";
            return sql;
        }

        private static void AddRangeParams(SqliteCommand cmd, DateTime from, DateTime to, int? accountId = null)
        {
            cmd.Parameters.AddWithValue("@from", from.ToDbString());
            cmd.Parameters.AddWithValue("@to", to.ToDbString());
            if (accountId.HasValue)
                cmd.Parameters.AddWithValue("@acct", accountId.Value);
        }

        private static string SqliteDateKey(SqliteDataReader r, int ordinal)
        {
            if (r.IsDBNull(ordinal)) return "";
            var value = r.GetValue(ordinal);
            if (value is DateTime dt) return dt.ToString("yyyy-MM-dd");
            var text = Convert.ToString(value)?.Trim() ?? "";
            return text.Length >= 10 ? text[..10] : text;
        }

        private static MonthlyBucket EnsureMonth(Dictionary<string, MonthlyBucket> map, string key)
        {
            if (!map.TryGetValue(key, out var bucket))
            {
                var label = key;
                if (DateTime.TryParse(key + "-01", out var dt))
                    label = dt.ToString("MMMM yyyy");
                bucket = new MonthlyBucket { MonthKey = key, MonthLabel = label };
                map[key] = bucket;
            }
            return bucket;
        }
    }
}
