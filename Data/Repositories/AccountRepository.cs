using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Data.Repositories
{
    /// <summary>
    /// Data access for the Accounts table.
    /// Supports retrieving active payment accounts and basic management.
    /// </summary>
    public class AccountRepository
    {
        public List<Account> GetActiveAccounts()
        {
            var accounts = new List<Account>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Accounts WHERE IsActive = 1 ORDER BY AccountTitle ASC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                accounts.Add(MapAccount(reader));
            }
            return accounts;
        }

        public Account? GetById(int id)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Accounts WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            if (reader.Read()) return MapAccount(reader);
            return null;
        }

        private Account MapAccount(SqliteDataReader reader)
        {
            return new Account
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                AccountTitle = reader.GetString(reader.GetOrdinal("AccountTitle")),
                AccountType = reader.GetString(reader.GetOrdinal("AccountType")),
                BankName = reader.IsDBNull(reader.GetOrdinal("BankName")) ? null : reader.GetString(reader.GetOrdinal("BankName")),
                BranchName = reader.IsDBNull(reader.GetOrdinal("BranchName")) ? null : reader.GetString(reader.GetOrdinal("BranchName")),
                AccountNumber = reader.IsDBNull(reader.GetOrdinal("AccountNumber")) ? null : reader.GetString(reader.GetOrdinal("AccountNumber")),
                IsActive = reader.GetInt32(reader.GetOrdinal("IsActive")) != 0
            };
        }
    }
}
