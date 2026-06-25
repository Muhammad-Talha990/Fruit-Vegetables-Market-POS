using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GroceryPOS.Data.Repositories;
using GroceryPOS.Models;
using GroceryPOS.Helpers;

namespace GroceryPOS.Services
{
    public class StockService : IStockService
    {
        private readonly ItemRepository _itemRepo;
        private readonly DataCacheService _cache;
        private readonly IImageStorageService _imageService;
        private readonly UniqueIdGenerator _idGenerator;
        private readonly StockPurchaseRepository _purchaseRepo;

        public event Action? StockChanged;

        public StockService(ItemRepository itemRepo, DataCacheService cache, IImageStorageService imageService, UniqueIdGenerator idGenerator, StockPurchaseRepository purchaseRepo)
        {
            _itemRepo     = itemRepo;
            _cache        = cache;
            _imageService = imageService;
            _idGenerator  = idGenerator;
            _purchaseRepo = purchaseRepo;
        }

        public void NotifyChanged() => StockChanged?.Invoke();

        // ─────────────────────────────────────────────
        //  CART-BASED STOCK PURCHASE  (new primary path)
        // ─────────────────────────────────────────────

        /// <summary>
        /// Atomically saves a multi-item stock purchase.
        /// DateTime is captured ONCE at the start of this call and propagated
        /// to every InventoryLog row so all share an identical timestamp.
        /// Cash in Drawer is implicitly reduced via GetTodayCashInDrawer().
        /// </summary>
        public async Task<StockPurchase> RegisterPurchaseAsync(StockPurchase purchase, string? tempImagePath = null)
        {
            if (purchase == null || purchase.Items == null || purchase.Items.Count == 0)
                throw new ArgumentException("Purchase cart must contain at least one item.");

            // 1. Process Image (Rename and save)
            // Note: Since StockPurchaseRepository.SavePurchaseWithTransaction generates the ID, 
            // we have a chicken-and-egg problem if we want the ID in the filename.
            // We'll use a unique temp name or save after. 
            // Better: use a GUID or just save after.
            // Actually, we can use the UniqueIdGenerator if we want a "Bill ID" style name.
            
            return await Task.Run(async () =>
            {
                // We'll save the image AFTER we get the ID from the DB to keep filenames consistent (e.g., SP_00123.jpg)
                var saved = _purchaseRepo.SavePurchaseWithTransaction(purchase);

                if (!string.IsNullOrEmpty(tempImagePath))
                {
                    try
                    {
                        // Generate a pseudo-bill ID for naming the file: "SP-" + PurchaseId
                        string billId = $"SP-{saved.PurchaseId:D5}";
                        string permanentPath = await _imageService.SaveBillImageAsync(tempImagePath, billId);
                        
                        // Update the record with the image path
                        saved.ImagePath = permanentPath;
                        
                        // We need to update the DB record now that we have the path
                        // Update both the StockPurchases master and the InventoryLogs (for history visibility)
                        using var conn = Data.DatabaseHelper.GetConnection();
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = @"
                            UPDATE StockPurchases SET ImagePath = @img WHERE PurchaseId = @id;
                            UPDATE InventoryLogs  SET ImagePath = @img WHERE ReferenceId = @id AND ReferenceType = 'Supply';";
                        cmd.Parameters.AddWithValue("@img", permanentPath);
                        cmd.Parameters.AddWithValue("@id",  saved.PurchaseId);
                        cmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error($"Failed to save bill image for Purchase {saved.PurchaseId}", ex);
                        // We don't fail the whole transaction if just the image fails, but we log it.
                    }
                }

                // Update cache for each item
                foreach (var item in saved.Items)
                    _cache.UpdateStockInCache(item.ItemId, item.Quantity);

                StockChanged?.Invoke();
                return saved;
            });
        }
        // ─────────────────────────────────────────────
        //  Legacy single-item stock operations
        // ─────────────────────────────────────────────

        public void DeductStock(string barcode, double quantity)
        {
            var item = _itemRepo.GetByBarcode(barcode);
            if (item == null)
                throw new InvalidOperationException($"Product with barcode {barcode} not found.");

            if (item.StockQuantity < quantity)
                throw new InvalidOperationException($"Insufficient stock for '{item.Description}'. Available: {item.StockQuantity}, Requested: {quantity}");

            _itemRepo.UpdateStock(barcode, -quantity);
            _cache.UpdateStockInCache(item.Id, -quantity);
            StockChanged?.Invoke();
        }

        public void AddStock(string barcode, double quantity)
        {
            if (quantity <= 0)
                throw new ArgumentException("Quantity to add must be positive.");

            var item = _itemRepo.GetByBarcode(barcode);
            if (item == null)
                throw new InvalidOperationException($"Product with barcode {barcode} not found.");

            _itemRepo.UpdateStock(barcode, quantity);
            _cache.UpdateStockInCache(item.Id, quantity);
            StockChanged?.Invoke();
        }

        public async Task<bool> RegisterSupplyAsync(Stock entry, string? tempImagePath)
        {
            if (entry.Quantity <= 0)
                throw new ArgumentException("Supply quantity must be positive.");

            // 1. Generate Internal ID
            entry.BillId = _idGenerator.GenerateSupplierBillId();

            // 2. Process Image (Rename and save)
            string? permanentImagePath = null;
            if (!string.IsNullOrEmpty(tempImagePath))
            {
                permanentImagePath = await _imageService.SaveBillImageAsync(tempImagePath, entry.BillId);
                entry.ImagePath = permanentImagePath;
            }

            // 3. Transactional Operations
            using var conn = Data.DatabaseHelper.GetConnection();
            using var transaction = conn.BeginTransaction();
            try
            {
                // Capture timestamp ONCE for the entire operation
                DateTime txnTime = DateTimeHelper.CaptureTransactionTime();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO InventoryLogs (ItemId, QuantityChange, ChangeType, ReferenceType, LogDate, ImagePath)
                        VALUES ((SELECT ItemId FROM Items WHERE Barcode = @pid), @qty, 'Purchase', 'Supply', @dt, @img);
                    ";
                    cmd.Parameters.AddWithValue("@pid", entry.ProductId);
                    cmd.Parameters.AddWithValue("@qty", entry.Quantity);
                    cmd.Parameters.AddWithValue("@dt",  txnTime.ToDbString());  // single-capture timestamp
                    cmd.Parameters.AddWithValue("@img", (object?)permanentImagePath ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();

                // 4. Update Cache & UI
                var addedItem = _itemRepo.GetByBarcode(entry.ProductId);
                if (addedItem != null)
                    _cache.UpdateStockInCache(addedItem.Id, entry.Quantity);
                StockChanged?.Invoke();

                AppLogger.Info($"Supply registered: {entry.Quantity} units for {entry.ProductId}. Bill ID: {entry.BillId}");
                return true;
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                // Cleanup image if saved but transaction failed
                if (!string.IsNullOrEmpty(permanentImagePath))
                    _imageService.DeleteBillImage(entry.BillId);

                AppLogger.Error($"Failed to register supply for {entry.ProductId}", ex);
                throw;
            }
        }

        public async Task<List<Stock>> GetSupplyHistoryAsync(string productId)
        {
            var history = new List<Stock>();
            using var conn = Data.DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT l.*, i.Description AS ProductName, i.Barcode as product_id
                FROM InventoryLogs l 
                LEFT JOIN Items i ON l.ItemId = i.ItemId
                WHERE i.Barcode = @pid AND l.ReferenceType = 'Supply'
                ORDER BY l.LogDate DESC;";
            cmd.Parameters.AddWithValue("@pid", productId);

            using var reader = await cmd.ExecuteReaderAsync();
            var imgOrd = reader.GetOrdinal("ImagePath");
            while (reader.Read())
            {
                history.Add(new Stock
                {
                    Id = reader.GetInt32(reader.GetOrdinal("LogId")),
                    ProductId = reader.GetString(reader.GetOrdinal("product_id")),
                    Quantity = (int)reader.GetDouble(reader.GetOrdinal("QuantityChange")),
                    SystemDate = DateTime.TryParse(reader.GetString(reader.GetOrdinal("LogDate")), out var dt) ? dt : DateTime.Now,
                    ProductName = reader.IsDBNull(reader.GetOrdinal("ProductName")) ? "" : reader.GetString(reader.GetOrdinal("ProductName")),
                    ImagePath = reader.IsDBNull(imgOrd) ? null : reader.GetString(imgOrd)
                });
            }
            return history;
        }

        public async Task<bool> UpdateSupplyAsync(Stock entry, string? tempImagePath)
        {
            using var conn = Data.DatabaseHelper.GetConnection();
            
            // 1. Fetch existing entry to calculate difference
            Stock? existing = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT l.*, i.Barcode as product_id
                    FROM InventoryLogs l
                    JOIN Items i ON l.ItemId = i.ItemId
                    WHERE l.LogId = @id;";
                cmd.Parameters.AddWithValue("@id", entry.Id);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (reader.Read())
                    {
                        var eImgOrd = reader.GetOrdinal("ImagePath");
                        existing = new Stock
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("LogId")),
                            ProductId = reader.GetString(reader.GetOrdinal("product_id")),
                            Quantity = (int)reader.GetDouble(reader.GetOrdinal("QuantityChange")),
                            ImagePath = reader.IsDBNull(eImgOrd) ? null : reader.GetString(eImgOrd)
                        };
                    }
                }
            }

            if (existing == null) throw new InvalidOperationException("Supply record not found.");

            // 2. Process Image (if new image provided)
            string? permanentImagePath = existing.ImagePath;
            if (!string.IsNullOrEmpty(tempImagePath))
            {
                // Delete old image if it exists
                if (!string.IsNullOrEmpty(existing.BillId))
                {
                    _imageService.DeleteBillImage(existing.BillId);
                }
                
                // Save new image
                permanentImagePath = await _imageService.SaveBillImageAsync(tempImagePath, existing.BillId);
                entry.ImagePath = permanentImagePath;
            }

            double qtyDifference = entry.Quantity - existing.Quantity;

            using var transaction = conn.BeginTransaction();
            try
            {
                // 3. Update InventoryLogs entry
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        UPDATE InventoryLogs 
                        SET QuantityChange = @qty, ImagePath = @img 
                        WHERE LogId = @id;
                    ";
                    cmd.Parameters.AddWithValue("@qty", (double)entry.Quantity);
                    cmd.Parameters.AddWithValue("@img", (object?)permanentImagePath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@id", entry.Id);
                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();

                // 5. Update Cache & UI
                if (qtyDifference != 0)
                {
                    var updatedItem = _itemRepo.GetByBarcode(entry.ProductId);
                    if (updatedItem != null)
                        _cache.UpdateStockInCache(updatedItem.Id, qtyDifference);
                }
                StockChanged?.Invoke();

                AppLogger.Info($"Supply updated: ID {entry.Id}, New Qty: {entry.Quantity} (Diff: {qtyDifference}). Bill ID: {existing.BillId}");
                return true;
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                AppLogger.Error($"Failed to update supply {entry.Id}", ex);
                throw;
            }
        }

        public async Task<List<Stock>> GetAllRecentSuppliesAsync(int limit = 50)
        {
            var history = new List<Stock>();
            using var conn = Data.DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT l.*, i.Description AS ProductName, i.Barcode as product_id
                FROM InventoryLogs l 
                LEFT JOIN Items i ON l.ItemId = i.ItemId
                WHERE l.ReferenceType = 'Supply'
                ORDER BY l.LogDate DESC 
                LIMIT @limit;";
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = await cmd.ExecuteReaderAsync();
            var imgOrd = reader.GetOrdinal("ImagePath");
            while (reader.Read())
            {
                history.Add(new Stock
                {
                    Id = reader.GetInt32(reader.GetOrdinal("LogId")),
                    ProductId = reader.GetString(reader.GetOrdinal("product_id")),
                    Quantity = (int)reader.GetDouble(reader.GetOrdinal("QuantityChange")),
                    SystemDate = DateTime.TryParse(reader.GetString(reader.GetOrdinal("LogDate")), out var dt) ? dt : DateTime.Now,
                    ProductName = reader.IsDBNull(reader.GetOrdinal("ProductName")) ? "" : reader.GetString(reader.GetOrdinal("ProductName")),
                    ImagePath = reader.IsDBNull(imgOrd) ? null : reader.GetString(imgOrd)
                });
            }
            return history;
        }

        public async Task<bool> DeleteSupplyAsync(int stockId)
        {
            using var conn = Data.DatabaseHelper.GetConnection();
            
            // 1. Fetch entry details first for cleanup
            Stock? entry = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT l.*, i.Barcode as product_id
                    FROM InventoryLogs l
                    JOIN Items i ON l.ItemId = i.ItemId
                    WHERE l.LogId = @id;";
                cmd.Parameters.AddWithValue("@id", stockId);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (reader.Read())
                    {
                        entry = new Stock
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("LogId")),
                            ProductId = reader.GetString(reader.GetOrdinal("product_id")),
                            Quantity = (int)reader.GetDouble(reader.GetOrdinal("QuantityChange"))
                        };
                    }
                }
            }

            if (entry == null) return false;

            using var transaction = conn.BeginTransaction();
            try
            {
                // 2. Delete from InventoryLogs
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "DELETE FROM InventoryLogs WHERE LogId = @id;";
                    cmd.Parameters.AddWithValue("@id", stockId);
                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();

                // 4. Cleanup physical image
                if (!string.IsNullOrEmpty(entry.BillId))
                {
                    _imageService.DeleteBillImage(entry.BillId);
                }

                // 5. Update Cache & UI
                var deletedItem = _itemRepo.GetByBarcode(entry.ProductId);
                if (deletedItem != null)
                    _cache.UpdateStockInCache(deletedItem.Id, -entry.Quantity);
                StockChanged?.Invoke();

                AppLogger.Info($"Supply deleted: {entry.Quantity} units for {entry.ProductId}. Bill ID: {entry.BillId}");
                return true;
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                AppLogger.Error($"Failed to delete supply {stockId}", ex);
                throw;
            }
        }

        public bool IsStockAvailable(string barcode, double requiredQuantity, out double availableQuantity)
        {
            var item = _itemRepo.GetByBarcode(barcode);
            if (item == null)
            {
                availableQuantity = 0;
                return false;
            }

            availableQuantity = item.StockQuantity;
            return item.StockQuantity >= requiredQuantity;
        }

        public bool IsStockAvailable(int itemId, double requiredQuantity, out double availableQuantity)
        {
            var item = _itemRepo.GetById(itemId);
            if (item == null)
            {
                availableQuantity = 0;
                return false;
            }

            availableQuantity = item.StockQuantity;
            return item.StockQuantity >= requiredQuantity;
        }

        public List<Item> GetLowStockItems()
        {
            return _itemRepo.GetAll()
                .Where(i => i.StockQuantity <= i.MinStockThreshold)
                .OrderBy(i => i.StockQuantity)
                .ToList();
        }

        public int GetLowStockCount()
        {
            return _itemRepo.GetAll()
                .Count(i => i.StockQuantity <= i.MinStockThreshold);
        }
    }
}
