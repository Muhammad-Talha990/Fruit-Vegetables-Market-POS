using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Data.Repositories
{
    /// <summary>
    /// Data access for the Users table.
    /// Provides operations using raw SQL with parameterized queries.
    /// </summary>
    public class UserRepository
    {
        /// <summary>Returns all users ordered by FullName.</summary>
        public List<User> GetAll()
        {
            var users = new List<User>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Users ORDER BY FullName;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                users.Add(MapUser(reader));

            return users;
        }

        /// <summary>Gets a single user by username.</summary>
        public User? GetByUsername(string username)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Users WHERE Username = @user AND IsActive = 1;";
            cmd.Parameters.AddWithValue("@user", username);

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapUser(reader) : null;
        }

        /// <summary>Gets a single user by ID.</summary>
        public User? GetById(int id)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Users WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapUser(reader) : null;
        }

        // ── Mapper ──
        private static User MapUser(SqliteDataReader reader)
        {
            return new User
            {
                Id           = reader.GetInt32(reader.GetOrdinal("Id")),
                Username     = reader.GetString(reader.GetOrdinal("Username")),
                PasswordHash = reader.GetString(reader.GetOrdinal("PasswordHash")),
                FullName     = reader.GetString(reader.GetOrdinal("FullName")),
                Role         = reader.GetString(reader.GetOrdinal("Role")),
                IsActive     = reader.GetInt32(reader.GetOrdinal("IsActive")) == 1,
                CreatedAt    = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
            };
        }
    }
}
