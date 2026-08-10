using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Data.Repositories
{
    public class CategoryRepository
    {
        public List<Category> GetAllActive()
        {
            var categories = new List<Category>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT * FROM Categories
                WHERE IsActive = 1
                ORDER BY DisplayOrder, Name;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                categories.Add(MapCategory(reader));

            return categories;
        }

        public List<Category> GetAll()
        {
            var categories = new List<Category>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Categories ORDER BY DisplayOrder, Name;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                categories.Add(MapCategory(reader));

            return categories;
        }

        public Category? GetById(int categoryId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Categories WHERE CategoryId = @id;";
            cmd.Parameters.AddWithValue("@id", categoryId);

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapCategory(reader) : null;
        }

        public int Add(Category category)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Categories (Name, NameUrdu, IconPath, DisplayOrder, IsActive)
                VALUES (@name, @nameUrdu, @icon, @order, @active);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@name", category.Name);
            cmd.Parameters.AddWithValue("@nameUrdu", string.IsNullOrWhiteSpace(category.NameUrdu) ? DBNull.Value : category.NameUrdu);
            cmd.Parameters.AddWithValue("@icon", (object?)category.IconPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@order", category.DisplayOrder);
            cmd.Parameters.AddWithValue("@active", category.IsActive ? 1 : 0);

            category.CategoryId = Convert.ToInt32(cmd.ExecuteScalar());
            return category.CategoryId;
        }

        public void Update(Category category)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Categories SET
                    Name         = @name,
                    NameUrdu     = @nameUrdu,
                    IconPath     = @icon,
                    DisplayOrder = @order,
                    IsActive     = @active
                WHERE CategoryId = @id;";
            cmd.Parameters.AddWithValue("@id", category.CategoryId);
            cmd.Parameters.AddWithValue("@name", category.Name);
            cmd.Parameters.AddWithValue("@nameUrdu", string.IsNullOrWhiteSpace(category.NameUrdu) ? DBNull.Value : category.NameUrdu);
            cmd.Parameters.AddWithValue("@icon", (object?)category.IconPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@order", category.DisplayOrder);
            cmd.Parameters.AddWithValue("@active", category.IsActive ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        private static Category MapCategory(SqliteDataReader reader)
        {
            var iconOrd = reader.GetOrdinal("IconPath");
            var nameUrduOrd = reader.GetOrdinal("NameUrdu");
            return new Category
            {
                CategoryId   = reader.GetInt32(reader.GetOrdinal("CategoryId")),
                Name         = reader.GetString(reader.GetOrdinal("Name")),
                NameUrdu     = reader.IsDBNull(nameUrduOrd) ? null : reader.GetString(nameUrduOrd),
                IconPath     = reader.IsDBNull(iconOrd) ? null : reader.GetString(iconOrd),
                DisplayOrder = reader.GetInt32(reader.GetOrdinal("DisplayOrder")),
                IsActive     = reader.GetInt32(reader.GetOrdinal("IsActive")) != 0
            };
        }
    }
}
