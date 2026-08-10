using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using FruitVegetableMarketPOS.Data.Repositories;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Services
{
    /// <summary>
    /// Thread-safe in-memory cache for Items and Users.
    /// Used to optimize read performance and avoid redundant DB queries.
    /// Items are stored in a Dictionary for fast $O(1)$ barcode lookup.
    /// </summary>
    public class DataCacheService
    {
        private readonly ItemRepository _itemRepo;
        private readonly UserRepository _userRepo;

        // ── In-Memory Collections ──
        private readonly ConcurrentDictionary<int, Item> _itemCache = new();
        private readonly ConcurrentDictionary<string, int> _barcodeIndex = new(StringComparer.OrdinalIgnoreCase);
        private List<User> _userCache = new();

        public DataCacheService(ItemRepository itemRepo, UserRepository userRepo)
        {
            _itemRepo = itemRepo;
            _userRepo = userRepo;
        }

        /// <summary>
        /// Loads all data from SQLite into memory.
        /// Call this once during application startup.
        /// </summary>
        public void LoadAllData()
        {
            try
            {
                AppLogger.Info("DataCacheService: Loading data from DB...");

                // Load Items
                var items = _itemRepo.GetAll();
                _itemCache.Clear();
                _barcodeIndex.Clear();
                foreach (var item in items)
                {
                    _itemCache.TryAdd(item.Id, item);
                    if (!string.IsNullOrWhiteSpace(item.Barcode))
                        _barcodeIndex.TryAdd(item.Barcode, item.Id);
                }

                // Load Users
                _userCache = _userRepo.GetAll();

                AppLogger.Info($"DataCacheService: Loaded {_itemCache.Count} items and {_userCache.Count} users.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("DataCacheService: Failed to load data", ex);
                throw;
            }
        }

        // ────────────────────────────────────────────
        //  ITEM CACHE METHODS
        // ────────────────────────────────────────────

        public Item? GetItemById(int id)
        {
            return _itemCache.TryGetValue(id, out var item) ? item : null;
        }

        public Item? GetItemByBarcode(string barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode)) return null;
            if (_barcodeIndex.TryGetValue(barcode, out var id))
                return _itemCache.TryGetValue(id, out var item) ? item : null;
            return null;
        }

        public List<Item> GetAllItems()
        {
            return _itemCache.Values.OrderBy(i => i.Description).ToList();
        }

        public void UpdateItemInCache(Item item)
        {
            if (_itemCache.TryGetValue(item.Id, out var existing) && !string.IsNullOrWhiteSpace(existing.Barcode))
                _barcodeIndex.TryRemove(existing.Barcode, out _);

            _itemCache[item.Id] = item;
            if (!string.IsNullOrWhiteSpace(item.Barcode))
                _barcodeIndex[item.Barcode] = item.Id;
        }

        public void RemoveItemFromCache(int id)
        {
            if (_itemCache.TryRemove(id, out var removed) && !string.IsNullOrWhiteSpace(removed.Barcode))
                _barcodeIndex.TryRemove(removed.Barcode, out _);
        }

        // ────────────────────────────────────────────
        //  USER CACHE METHODS
        // ────────────────────────────────────────────

        public User? GetUserByUsername(string username)
        {
            return _userCache.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        }

        public List<User> GetAllUsers()
        {
            return _userCache.ToList();
        }

        public void RefreshUserCache()
        {
            _userCache = _userRepo.GetAll();
        }
    }
}
