using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Data.Repositories
{
    /// <summary>
    /// Data access for the Items catalog table (no stock / cost / sale price columns).
    /// Daily unit prices live on ItemTypes.
    /// </summary>
    public class ItemRepository
    {
        private const string BaseSelectSql = @"
            SELECT i.*, c.Name as CategoryName
            FROM Items i
            LEFT JOIN Categories c ON i.CategoryId = c.CategoryId";

        // ────────────────────────────────────────────
        //  READ operations
        // ────────────────────────────────────────────

        /// <summary>Returns all items ordered by description.</summary>
        public List<Item> GetAll()
        {
            var items = new List<Item>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"{BaseSelectSql} ORDER BY i.Description;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                items.Add(MapItem(reader));

            return items;
        }

        /// <summary>Returns active items only.</summary>
        public List<Item> GetActiveItems()
        {
            var items = new List<Item>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"{BaseSelectSql} WHERE i.IsActive = 1 ORDER BY i.Description;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                items.Add(MapItem(reader));

            return items;
        }

        /// <summary>Gets a single item by barcode.</summary>
        public Item? GetByBarcode(string barcode)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"{BaseSelectSql} WHERE i.Barcode = @barcode;";
            cmd.Parameters.AddWithValue("@barcode", barcode);

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapItem(reader) : null;
        }

        /// <summary>Gets a single item by its internal primary key ID.</summary>
        public Item? GetById(int id)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"{BaseSelectSql} WHERE i.ItemId = @id;";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapItem(reader) : null;
        }

        /// <summary>Searches items by description, barcode, or category (case-insensitive).</summary>
        public List<Item> Search(string searchTerm)
        {
            var items = new List<Item>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                {BaseSelectSql}
                WHERE i.IsActive = 1
                  AND (i.Description LIKE @term 
                   OR i.NameUrdu     LIKE @term
                   OR i.Barcode     LIKE @term 
                   OR c.Name        LIKE @term)
                ORDER BY i.Description;
            ";
            cmd.Parameters.AddWithValue("@term", $"%{searchTerm}%");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                items.Add(MapItem(reader));

            return items;
        }

        /// <summary>Gets all items in a specific category name.</summary>
        public List<Item> GetByCategory(string categoryName)
        {
            var items = new List<Item>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"{BaseSelectSql} WHERE c.Name = @cat AND i.IsActive = 1 ORDER BY i.Description;";
            cmd.Parameters.AddWithValue("@cat", categoryName);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                items.Add(MapItem(reader));

            return items;
        }

        /// <summary>Returns all distinct category names.</summary>
        public List<string> GetAllCategories()
        {
            var categories = new List<string>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Name FROM Categories WHERE IsActive = 1 ORDER BY DisplayOrder, Name;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                categories.Add(reader.GetString(0));

            return categories;
        }

        /// <summary>Returns total count of items.</summary>
        public int GetCount()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Items WHERE IsActive = 1;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        // ────────────────────────────────────────────
        //  WRITE operations
        // ────────────────────────────────────────────

        /// <summary>Inserts a new item. Throws if barcode already exists.</summary>
        public void Add(Item item)
        {
            var now = DateTimeHelper.CaptureTransactionTime();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Items (Barcode, Description, NameUrdu, CategoryId, IsActive, UpdatedAt)
                VALUES (@barcode, @desc, @nameUrdu,
                       (SELECT CategoryId FROM Categories WHERE Name = @catName),
                       @active, @updatedAt);
            ";
            cmd.Parameters.AddWithValue("@barcode", string.IsNullOrWhiteSpace(item.Barcode) ? DBNull.Value : item.Barcode);
            cmd.Parameters.AddWithValue("@desc", item.Description);
            cmd.Parameters.AddWithValue("@nameUrdu", string.IsNullOrWhiteSpace(item.NameUrdu) ? DBNull.Value : item.NameUrdu);
            cmd.Parameters.AddWithValue("@catName", (object?)item.CategoryName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@active", item.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@updatedAt", now.ToDbString());
            cmd.ExecuteNonQuery();

            using var idCmd = conn.CreateCommand();
            idCmd.CommandText = "SELECT last_insert_rowid();";
            item.Id = Convert.ToInt32(idCmd.ExecuteScalar()!);
            item.UpdatedAt = now;

            AppLogger.Info($"Item added: '{item.Description}' (Barcode: {item.Barcode}, Id: {item.Id})");
        }

        /// <summary>Updates an existing item.</summary>
        public void Update(Item item)
        {
            Update(item, item.Barcode);
        }

        /// <summary>Updates an item, specifically handling barcode changes if originalBarcode is provided.</summary>
        public void Update(Item item, string? originalBarcode)
        {
            var now = DateTimeHelper.CaptureTransactionTime();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Items SET 
                    Barcode           = @barcode,
                    Description       = @desc,
                    NameUrdu          = @nameUrdu,
                    CategoryId        = COALESCE(@categoryId, (SELECT CategoryId FROM Categories WHERE Name = @catName LIMIT 1)),
                    IsActive          = @active,
                    UpdatedAt         = @updatedAt
                WHERE ItemId = @id;
            ";
            cmd.Parameters.AddWithValue("@id", item.Id);
            cmd.Parameters.AddWithValue("@barcode", string.IsNullOrWhiteSpace(item.Barcode) ? DBNull.Value : item.Barcode);
            cmd.Parameters.AddWithValue("@desc", item.Description);
            cmd.Parameters.AddWithValue("@nameUrdu", string.IsNullOrWhiteSpace(item.NameUrdu) ? DBNull.Value : item.NameUrdu);
            cmd.Parameters.AddWithValue("@categoryId", item.CategoryId.HasValue ? item.CategoryId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@catName", (object?)item.CategoryName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@active", item.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@updatedAt", now.ToDbString());
            cmd.ExecuteNonQuery();

            item.UpdatedAt = now;
            AppLogger.Info($"Item updated: Id={item.Id} '{item.Description}' (Barcode: {item.Barcode})");
        }

        /// <summary>Soft-deactivates an item (IsActive = 0).</summary>
        public void SoftDeactivate(int id)
        {
            var now = DateTimeHelper.CaptureTransactionTime();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Items SET IsActive = 0, UpdatedAt = @updatedAt
                WHERE ItemId = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@updatedAt", now.ToDbString());
            cmd.ExecuteNonQuery();
            AppLogger.Info($"Item soft-deactivated: ID {id}");
        }

        /// <summary>Permanently deletes an item by internal ID.</summary>
        public void Delete(int id)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Items WHERE ItemId = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            AppLogger.Info($"Item deleted: ID {id}");
        }

        /// <summary>Permanently deletes an item by barcode.</summary>
        public void Delete(string barcode)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Items WHERE Barcode = @barcode;";
            cmd.Parameters.AddWithValue("@barcode", barcode);
            cmd.ExecuteNonQuery();
            AppLogger.Info($"Item deleted: Barcode {barcode}");
        }

        // ────────────────────────────────────────────
        //  Mapper
        // ────────────────────────────────────────────

        private static Item MapItem(SqliteDataReader reader)
        {
            var barcodeOrd = reader.GetOrdinal("Barcode");
            var nameUrduOrd = reader.GetOrdinal("NameUrdu");
            var updatedOrd = reader.GetOrdinal("UpdatedAt");
            return new Item
            {
                Id                = reader.GetInt32(reader.GetOrdinal("ItemId")),
                Barcode           = reader.IsDBNull(barcodeOrd) ? null : reader.GetString(barcodeOrd),
                Description       = reader.GetString(reader.GetOrdinal("Description")),
                NameUrdu          = reader.IsDBNull(nameUrduOrd) ? null : reader.GetString(nameUrduOrd),
                CategoryId        = reader.IsDBNull(reader.GetOrdinal("CategoryId")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("CategoryId")),
                CategoryName      = reader.IsDBNull(reader.GetOrdinal("CategoryName")) ? null : reader.GetString(reader.GetOrdinal("CategoryName")),
                IsActive          = reader.GetInt32(reader.GetOrdinal("IsActive")) != 0,
                UpdatedAt         = reader.IsDBNull(updatedOrd) ? null : reader.GetDateTime(updatedOrd)
            };
        }
    }
}
