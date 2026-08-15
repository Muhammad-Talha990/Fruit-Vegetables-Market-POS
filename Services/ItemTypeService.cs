using System.Collections.Generic;
using System.Linq;
using FruitVegetableMarketPOS.Data.Repositories;
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Services
{
    public class ItemTypeService
    {
        private readonly ItemTypeRepository _repo;

        public ItemTypeService(ItemTypeRepository repo)
        {
            _repo = repo;
        }

        public List<ItemType> GetByItemId(int itemId) => _repo.GetByItemId(itemId);

        public List<ItemType> GetActiveByItemId(int itemId) => _repo.GetActiveByItemId(itemId);

        public ItemType? GetById(int typeId) => _repo.GetById(typeId);

        public int Add(ItemType type)
        {
            if (string.IsNullOrWhiteSpace(type.TypeName))
                throw new System.ArgumentException("Type name is required.");
            if (type.Price < 0)
                throw new System.ArgumentException("Price cannot be negative.");
            return _repo.Add(type);
        }

        public void Update(ItemType type) => _repo.Update(type);

        public void SoftDeactivate(int typeId) => _repo.SoftDeactivate(typeId);

        public void SoftDeactivateAllForItem(int itemId) => _repo.SoftDeactivateAllForItem(itemId);

        public static string FormatTypeName(int index) => $"Type {index} / قسم {index}";

        /// <summary>
        /// Replaces active types with Type 1…N / قسم 1…N at the given prices (max 10).
        /// </summary>
        public void ReplaceWithNumberedTypes(int itemId, IReadOnlyList<double> prices)
            => ReplaceWithNumberedTypes(itemId, prices, notes: null);

        /// <summary>
        /// Replaces active types with Type 1…N at the given prices/notes (max 10).
        /// </summary>
        public void ReplaceWithNumberedTypes(int itemId, IReadOnlyList<double> prices, IReadOnlyList<string?>? notes)
        {
            if (prices == null || prices.Count == 0)
                throw new System.ArgumentException("At least one type price is required.");
            if (prices.Count > 10)
                throw new System.ArgumentException("Maximum 10 types allowed.");

            _repo.SoftDeactivateAllForItem(itemId);

            for (int i = 0; i < prices.Count; i++)
            {
                if (prices[i] < 0)
                    throw new System.ArgumentException($"Type {i + 1} price cannot be negative.");

                string? note = null;
                if (notes != null && i < notes.Count && !string.IsNullOrWhiteSpace(notes[i]))
                    note = notes[i]!.Trim();

                _repo.Add(new ItemType
                {
                    ItemId = itemId,
                    TypeName = FormatTypeName(i + 1),
                    Price = prices[i],
                    Note = note,
                    SortOrder = i + 1,
                    IsActive = true
                });
            }
        }

        public ItemType CreateDefaultType(int itemId, double price, string typeName = "Type 1 / قسم 1")
        {
            var type = new ItemType
            {
                ItemId = itemId,
                TypeName = typeName,
                Price = price,
                SortOrder = 1,
                IsActive = true
            };
            _repo.Add(type);
            return type;
        }
    }
}
