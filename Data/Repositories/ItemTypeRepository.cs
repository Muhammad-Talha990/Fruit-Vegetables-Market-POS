using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Data.Repositories
{
    public class ItemTypeRepository
    {
        public List<ItemType> GetByItemId(int itemId)
        {
            var types = new List<ItemType>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT * FROM ItemTypes
                WHERE ItemId = @itemId
                ORDER BY SortOrder, TypeId;";
            cmd.Parameters.AddWithValue("@itemId", itemId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                types.Add(MapItemType(reader));

            return types;
        }

        public List<ItemType> GetActiveByItemId(int itemId)
        {
            var types = new List<ItemType>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT * FROM ItemTypes
                WHERE ItemId = @itemId AND IsActive = 1
                ORDER BY SortOrder, TypeId;";
            cmd.Parameters.AddWithValue("@itemId", itemId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                types.Add(MapItemType(reader));

            return types;
        }

        public ItemType? GetById(int typeId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM ItemTypes WHERE TypeId = @id;";
            cmd.Parameters.AddWithValue("@id", typeId);

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapItemType(reader) : null;
        }

        public int Add(ItemType type)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ItemTypes (ItemId, TypeName, Price, Note, SortOrder, IsActive, CreatedAt)
                VALUES (@itemId, @name, @price, @note, @sort, @active, @created);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@itemId", type.ItemId);
            cmd.Parameters.AddWithValue("@name", type.TypeName);
            cmd.Parameters.AddWithValue("@price", type.Price);
            cmd.Parameters.AddWithValue("@note",
                string.IsNullOrWhiteSpace(type.Note) ? DBNull.Value : type.Note.Trim());
            cmd.Parameters.AddWithValue("@sort", type.SortOrder);
            cmd.Parameters.AddWithValue("@active", type.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@created", (type.CreatedAt ?? DateTimeHelper.CaptureTransactionTime()).ToDbString());

            type.TypeId = Convert.ToInt32(cmd.ExecuteScalar());
            return type.TypeId;
        }

        public void Update(ItemType type)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE ItemTypes SET
                    TypeName  = @name,
                    Price     = @price,
                    Note      = @note,
                    SortOrder = @sort,
                    IsActive  = @active
                WHERE TypeId = @id;";
            cmd.Parameters.AddWithValue("@id", type.TypeId);
            cmd.Parameters.AddWithValue("@name", type.TypeName);
            cmd.Parameters.AddWithValue("@price", type.Price);
            cmd.Parameters.AddWithValue("@note",
                string.IsNullOrWhiteSpace(type.Note) ? DBNull.Value : type.Note.Trim());
            cmd.Parameters.AddWithValue("@sort", type.SortOrder);
            cmd.Parameters.AddWithValue("@active", type.IsActive ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        public void SoftDeactivate(int typeId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE ItemTypes SET IsActive = 0 WHERE TypeId = @id;";
            cmd.Parameters.AddWithValue("@id", typeId);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Deactivates all types for an item (used before replacing with Type 1…N).</summary>
        public void SoftDeactivateAllForItem(int itemId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE ItemTypes SET IsActive = 0 WHERE ItemId = @itemId;";
            cmd.Parameters.AddWithValue("@itemId", itemId);
            cmd.ExecuteNonQuery();
        }

        private static ItemType MapItemType(SqliteDataReader reader)
        {
            var createdOrd = reader.GetOrdinal("CreatedAt");
            var noteOrd = -1;
            try { noteOrd = reader.GetOrdinal("Note"); } catch { /* older schema */ }

            return new ItemType
            {
                TypeId    = reader.GetInt32(reader.GetOrdinal("TypeId")),
                ItemId    = reader.GetInt32(reader.GetOrdinal("ItemId")),
                TypeName  = reader.GetString(reader.GetOrdinal("TypeName")),
                Price     = reader.GetDouble(reader.GetOrdinal("Price")),
                Note      = noteOrd >= 0 && !reader.IsDBNull(noteOrd) ? reader.GetString(noteOrd) : null,
                SortOrder = reader.GetInt32(reader.GetOrdinal("SortOrder")),
                IsActive  = reader.GetInt32(reader.GetOrdinal("IsActive")) != 0,
                CreatedAt = reader.IsDBNull(createdOrd) ? null : reader.GetDateTime(createdOrd)
            };
        }
    }
}
