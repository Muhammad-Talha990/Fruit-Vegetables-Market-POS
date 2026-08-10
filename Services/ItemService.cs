using System;
using System.Collections.Generic;
using System.Linq;
using FruitVegetableMarketPOS.Data.Repositories;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Services
{
    /// <summary>
    /// Business logic for item (product) catalog management.
    /// Unit prices are managed on ItemTypes via Billing → Add Today.
    /// </summary>
    public class ItemService
    {
        private readonly ItemRepository _repo;
        private readonly ItemTypeRepository _typeRepo;
        private readonly DataCacheService _cache;

        public ItemService(ItemRepository repo, ItemTypeRepository typeRepo, DataCacheService cache)
        {
            _repo = repo;
            _typeRepo = typeRepo;
            _cache = cache;
        }

        // ────────────────────────────────────────────
        //  READ (optimized with Cache)
        // ────────────────────────────────────────────

        public List<Item> GetAllItems() => _cache.GetAllItems();

        public List<Item> GetActiveItems()
            => _cache.GetAllItems().FindAll(i => i.IsActive);

        public Item? GetItemById(int id) => _cache.GetItemById(id);

        public Item? GetItemByBarcode(string barcode) => _cache.GetItemByBarcode(barcode);

        public List<Item> SearchItems(string searchTerm)
        {
            return _repo.Search(searchTerm);
        }

        public List<Item> GetItemsByCategory(string category) => _repo.GetByCategory(category);

        public List<string> GetAllCategories() => _repo.GetAllCategories();

        public int GetTotalItemCount() => _cache.GetAllItems().Count;

        public Item? GetItemWithTypes(int id)
        {
            var item = _cache.GetItemById(id) ?? _repo.GetById(id);
            if (item == null) return null;
            item.Types = _typeRepo.GetActiveByItemId(id);
            return item;
        }

        // ────────────────────────────────────────────
        //  WRITE (with validation)
        // ────────────────────────────────────────────

        /// <summary>Adds a new item after validation. Creates a default Type 1 at price 0.</summary>
        public void AddItem(Item item)
        {
            ValidateItem(item);
            EnsureUniqueActiveName(item.Description, excludeItemId: null);

            if (!string.IsNullOrWhiteSpace(item.Barcode))
            {
                var existing = _cache.GetItemByBarcode(item.Barcode);
                if (existing != null)
                    throw new InvalidOperationException($"An item with barcode '{item.Barcode}' already exists ({existing.Description}).");
            }

            _repo.Add(item);

            // Placeholder type — real unit price is set on Billing → Add Today.
            _typeRepo.Add(new ItemType
            {
                ItemId = item.Id,
                TypeName = "Type 1",
                Price = 0,
                SortOrder = 1,
                IsActive = true
            });

            _cache.UpdateItemInCache(item);
        }

        /// <summary>Updates an existing item after validation (by ItemId only).</summary>
        public void UpdateItem(Item item, string? originalBarcode = null)
        {
            ValidateItem(item);
            EnsureUniqueActiveName(item.Description, excludeItemId: item.Id);

            if (!string.IsNullOrEmpty(item.Barcode) && !string.IsNullOrEmpty(originalBarcode) && originalBarcode != item.Barcode)
            {
               var existing = _cache.GetItemByBarcode(item.Barcode);
               if (existing != null && existing.Id != item.Id)
                   throw new InvalidOperationException($"An item with barcode '{item.Barcode}' already exists.");
            }

            _repo.Update(item, originalBarcode);
            _cache.UpdateItemInCache(CloneItem(item));
        }

        /// <summary>Soft-deactivates an item when possible; falls back to hard delete.</summary>
        public void DeleteItem(int id)
        {
            try
            {
                _repo.SoftDeactivate(id);
                if (_cache.GetItemById(id) is Item cached)
                {
                    cached.IsActive = false;
                    _cache.UpdateItemInCache(cached);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Soft delete failed for item {id}, attempting hard delete", ex);
                _repo.Delete(id);
                _cache.RemoveItemFromCache(id);
            }
        }

        // ────────────────────────────────────────────
        //  Validation
        // ────────────────────────────────────────────

        private void ValidateItem(Item item)
        {
            if (string.IsNullOrWhiteSpace(item.Description))
                throw new ArgumentException("Item description is required.");
        }

        /// <summary>
        /// Rule: each active product must have a unique English name.
        /// Two turnips → name them differently (e.g. Turnip / White Turnip).
        /// </summary>
        private void EnsureUniqueActiveName(string description, int? excludeItemId)
        {
            var name = description.Trim();
            var clash = _cache.GetAllItems().FirstOrDefault(i =>
                i.IsActive
                && (!excludeItemId.HasValue || i.Id != excludeItemId.Value)
                && string.Equals(i.Description?.Trim(), name, StringComparison.OrdinalIgnoreCase));

            if (clash != null)
            {
                throw new InvalidOperationException(
                    $"An active item named '{clash.Description}' already exists (ID #{clash.PosCode}).\n\n" +
                    "Each product must have a unique English name.\n" +
                    "Example: Turnip and White Turnip — not two items both called Turnip.");
            }
        }

        private static Item CloneItem(Item item) => new()
        {
            Id = item.Id,
            Barcode = item.Barcode,
            Description = item.Description,
            NameUrdu = item.NameUrdu,
            CategoryId = item.CategoryId,
            CategoryName = item.CategoryName,
            IsActive = item.IsActive,
            UpdatedAt = item.UpdatedAt
        };
    }
}
