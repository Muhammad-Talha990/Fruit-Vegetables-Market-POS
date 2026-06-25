using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using GroceryPOS.Data;
using GroceryPOS.Helpers;
using GroceryPOS.Models;

namespace GroceryPOS.Data.Repositories
{
    /// <summary>
    /// Data access for stock purchase transactions (StockPurchases + StockPurchaseItems).
    ///
    /// Business Rules:
    ///   - A purchase is atomic: header + items + InventoryLogs all commit together.
    ///   - DateTime is captured ONCE (passed in) to ensure all rows share the same timestamp.
    ///   - TotalAmount is stored on the master record and deducted from Cash in Drawer.
    /// </summary>
    public class StockPurchaseRepository
    {
        // ────────────────────────────────────────────
        //  WRITE operations
        // ────────────────────────────────────────────

        /// <summary>
        /// Atomically saves a stock purchase:
        ///   1. Insert StockPurchases header
        ///   2. Insert StockPurchaseItems
        ///   3. Insert InventoryLogs (Purchase entries) — all with the SAME timestamp
        /// Rolls back entirely on any failure.
        /// </summary>
        public StockPurchase SavePurchaseWithTransaction(StockPurchase purchase)
        {
            if (purchase.Items == null || purchase.Items.Count == 0)
                throw new InvalidOperationException("Cannot save a stock purchase with no items.");

            // Capture time ONCE for the entire transaction
            DateTime txnTime = DateTimeHelper.CaptureTransactionTime();
            purchase.PurchaseAt = txnTime;
            purchase.RecalculateTotal();

            using var conn = DatabaseHelper.GetConnection();
            using var txn = conn.BeginTransaction();

            try
            {
                // ── Step 1: Insert master purchase record ──
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = txn;
                    cmd.CommandText = @"
                        INSERT INTO StockPurchases (PurchaseAt, TotalAmount, CreatedByUserId, ImagePath)
                        VALUES (@at, @total, @uid, @img);
                        SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("@at",    txnTime.ToDbString());
                    cmd.Parameters.AddWithValue("@total", purchase.TotalAmount);
                    cmd.Parameters.AddWithValue("@uid",   purchase.CreatedByUserId.HasValue
                                                             ? (object)purchase.CreatedByUserId.Value
                                                             : DBNull.Value);
                    cmd.Parameters.AddWithValue("@img",   (object?)purchase.ImagePath ?? DBNull.Value);
                    purchase.PurchaseId = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // ── Step 2: Insert items + inventory log entries ──
                foreach (var item in purchase.Items)
                {
                    item.PurchaseId = purchase.PurchaseId;

                    using var itemCmd = conn.CreateCommand();
                    itemCmd.Transaction = txn;
                    itemCmd.CommandText = @"
                        INSERT INTO StockPurchaseItems (PurchaseId, ItemId, Quantity, CostPrice)
                        VALUES (@pid, @iid, @qty, @cost);

                        INSERT INTO InventoryLogs (ItemId, QuantityChange, ChangeType, ReferenceId, ReferenceType, LogDate)
                        VALUES (@iid, @qty, 'Purchase', @pid, 'Supply', @logDate);";
                    itemCmd.Parameters.AddWithValue("@pid",     purchase.PurchaseId);
                    itemCmd.Parameters.AddWithValue("@iid",     item.ItemId);
                    itemCmd.Parameters.AddWithValue("@qty",     item.Quantity);
                    itemCmd.Parameters.AddWithValue("@cost",    item.CostPrice);
                    itemCmd.Parameters.AddWithValue("@logDate", txnTime.ToDbString());  // SAME timestamp for every item
                    itemCmd.ExecuteNonQuery();
                }

                txn.Commit();
                AppLogger.Info($"StockPurchase saved: ID={purchase.PurchaseId} | Items={purchase.Items.Count} | Total=Rs.{purchase.TotalAmount:N2} | At={txnTime:yyyy-MM-dd HH:mm:ss}");
                return purchase;
            }
            catch (Exception ex)
            {
                txn.Rollback();
                AppLogger.Error("StockPurchase transaction failed — rolled back", ex);
                throw;
            }
        }

        // ────────────────────────────────────────────
        //  READ operations
        // ────────────────────────────────────────────

        /// <summary>Returns the most recent stock purchases with their items.</summary>
        public List<StockPurchase> GetRecentPurchases(int limit = 50)
        {
            var purchases = new List<StockPurchase>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT sp.PurchaseId, sp.PurchaseAt, sp.TotalAmount, sp.CreatedByUserId, sp.ImagePath
                FROM StockPurchases sp
                ORDER BY sp.PurchaseAt DESC
                LIMIT @limit;";
            cmd.Parameters.AddWithValue("@limit", limit);

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                    purchases.Add(MapPurchase(reader));
            }

            LoadPurchaseItems(conn, purchases);
            return purchases;
        }

        /// <summary>Returns a single purchase with all its items.</summary>
        public StockPurchase? GetById(int purchaseId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT PurchaseId, PurchaseAt, TotalAmount, CreatedByUserId, ImagePath
                FROM StockPurchases WHERE PurchaseId = @id;";
            cmd.Parameters.AddWithValue("@id", purchaseId);

            StockPurchase? purchase = null;
            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                    purchase = MapPurchase(reader);
            }

            if (purchase != null)
                LoadPurchaseItems(conn, new List<StockPurchase> { purchase });

            return purchase;
        }

        /// <summary>Returns today's total stock purchase amount (used for Cash in Drawer).</summary>
        public double GetTodayPurchasesTotal()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            cmd.CommandText = @"
                SELECT COALESCE(SUM(TotalAmount), 0)
                FROM StockPurchases
                WHERE date(PurchaseAt) = @today;";
            cmd.Parameters.AddWithValue("@today", todayStr);
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Returns total stock purchases for a date range.</summary>
        public double GetPurchasesTotalByRange(DateTime from, DateTime to)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(TotalAmount), 0)
                FROM StockPurchases
                WHERE PurchaseAt >= @from AND PurchaseAt < @to;";
            cmd.Parameters.AddWithValue("@from", from.ToDbString());
            cmd.Parameters.AddWithValue("@to",   to.ToDbString());
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        // ────────────────────────────────────────────
        //  Private helpers
        // ────────────────────────────────────────────

        private static StockPurchase MapPurchase(SqliteDataReader r)
        {
            return new StockPurchase
            {
                PurchaseId       = r.GetInt32(r.GetOrdinal("PurchaseId")),
                PurchaseAt       = DateTime.TryParse(r.GetString(r.GetOrdinal("PurchaseAt")), out var dt) ? dt : DateTime.Now,
                TotalAmount      = r.GetDouble(r.GetOrdinal("TotalAmount")),
                CreatedByUserId  = r.IsDBNull(r.GetOrdinal("CreatedByUserId")) ? null : r.GetInt32(r.GetOrdinal("CreatedByUserId")),
                ImagePath        = r.IsDBNull(r.GetOrdinal("ImagePath")) ? null : r.GetString(r.GetOrdinal("ImagePath"))
            };
        }

        private static void LoadPurchaseItems(SqliteConnection conn, List<StockPurchase> purchases)
        {
            if (purchases.Count == 0) return;

            var dict = new System.Collections.Generic.Dictionary<int, StockPurchase>();
            foreach (var p in purchases) dict[p.PurchaseId] = p;

            var ids = string.Join(",", dict.Keys);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT spi.Id, spi.PurchaseId, spi.ItemId, spi.Quantity, spi.CostPrice,
                       i.Description, i.Barcode
                FROM StockPurchaseItems spi
                JOIN Items i ON i.ItemId = spi.ItemId
                WHERE spi.PurchaseId IN ({ids})
                ORDER BY spi.Id ASC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var pid = reader.GetInt32(reader.GetOrdinal("PurchaseId"));
                if (!dict.TryGetValue(pid, out var purchase)) continue;

                purchase.Items.Add(new StockPurchaseItem
                {
                    Id              = reader.GetInt32(reader.GetOrdinal("Id")),
                    PurchaseId      = pid,
                    ItemId          = reader.GetInt32(reader.GetOrdinal("ItemId")),
                    Quantity        = reader.GetDouble(reader.GetOrdinal("Quantity")),
                    CostPrice       = reader.GetDouble(reader.GetOrdinal("CostPrice")),
                    ItemDescription = reader.IsDBNull(reader.GetOrdinal("Description")) ? "" : reader.GetString(reader.GetOrdinal("Description")),
                    Barcode         = reader.IsDBNull(reader.GetOrdinal("Barcode")) ? null : reader.GetString(reader.GetOrdinal("Barcode"))
                });
            }
        }
    }
}
