using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using GroceryPOS.Helpers;

namespace GroceryPOS.Data
{
    /// <summary>
    /// Utility class for resetting transactional data in SQLite database for testing purposes.
    /// 
    /// Clears:
    ///   - Bills (sales invoices)
    ///   - BillItems (line items)
    ///   - bill_payment (payment transaction log)
    ///   - BillReturns (return headers)
    ///   - BillReturnItems (return items)
    ///   - InventoryLogs (stock movement audit trail)
    ///   - CustomerLedger (customer accounting journal)
    /// 
    /// Preserves:
    ///   - Items (product catalog)
    ///   - Customers (customer registry)
    ///   - Accounts (payment methods)
    ///   - Users (system users)
    ///   - Categories (product categories)
    ///   - Database schema/structure
    /// </summary>
    public static class DatabaseResetUtility
    {
        /// <summary>
        /// Resets only transactional data while preserving all master data.
        /// Safe operation with proper foreign key handling and referential integrity.
        /// </summary>
        /// <param name="resetAutoIncrement">If true, resets AUTOINCREMENT sequences to 1 (optional)</param>
        /// <returns>Summary of deleted records and master data counts</returns>
        public static DatabaseResetSummary ResetTransactionalData(bool resetAutoIncrement = false)
        {
            var summary = new DatabaseResetSummary();

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    // Get counts BEFORE deletion
                    summary.BillsBeforeDelete = GetTableCount(conn, "Bills");
                    summary.BillItemsBeforeDelete = GetTableCount(conn, "BillItems");
                    summary.BillPaymentBeforeDelete = GetTableCount(conn, "bill_payment");
                    summary.BillReturnsBeforeDelete = GetTableCount(conn, "BillReturns");
                    summary.InventoryLogsBeforeDelete = GetTableCount(conn, "InventoryLogs");
                    summary.CustomerLedgerBeforeDelete = GetTableCount(conn, "CustomerLedger");

                    // Disable foreign key constraints temporarily
                    Execute(conn, "PRAGMA foreign_keys = OFF;");

                    // Delete in correct order (dependencies first)
                    Execute(conn, "DELETE FROM BillReturnItems;");
                    Execute(conn, "DELETE FROM BillReturns;");
                    Execute(conn, "DELETE FROM bill_payment;");
                    Execute(conn, "DELETE FROM BillItems;");
                    Execute(conn, "DELETE FROM Bills;");
                    Execute(conn, "DELETE FROM InventoryLogs;");
                    Execute(conn, "DELETE FROM CustomerLedger;");

                    // Optionally reset AUTOINCREMENT sequences
                    if (resetAutoIncrement)
                    {
                        ResetAutoIncrementSequences(conn);
                    }

                    // Re-enable foreign key enforcement
                    Execute(conn, "PRAGMA foreign_keys = ON;");

                    // Optimize database
                    Execute(conn, "VACUUM;");

                    // Verify master data is intact
                    summary.ItemsPreserved = GetTableCount(conn, "Items");
                    summary.CustomersPreserved = GetTableCount(conn, "Customers");
                    summary.AccountsPreserved = GetTableCount(conn, "Accounts");
                    summary.UsersPreserved = GetTableCount(conn, "Users");
                    summary.CategoriesPreserved = GetTableCount(conn, "Categories");

                    summary.IsSuccess = true;
                    summary.Message = "Transactional data reset successfully. All master data preserved.";
                    AppLogger.Info(summary.Message);
                }
            }
            catch (Exception ex)
            {
                summary.IsSuccess = false;
                summary.Message = $"Database reset failed: {ex.Message}";
                summary.ErrorDetails = ex.ToString();
                AppLogger.Error(summary.Message, ex);
            }

            return summary;
        }

        /// <summary>
        /// Resets AUTOINCREMENT sequences for all transactional tables (sets next ID to 1).
        /// This makes IDs start from 1 again after reset.
        /// </summary>
        private static void ResetAutoIncrementSequences(SqliteConnection conn)
        {
            try
            {
                var tables = new[]
                {
                    "Bills", "BillItems", "bill_payment", "BillReturns",
                    "BillReturnItems", "InventoryLogs", "CustomerLedger"
                };

                foreach (var table in tables)
                {
                    Execute(conn, $"DELETE FROM sqlite_sequence WHERE name = '{table}';");
                    Execute(conn, $"UPDATE sqlite_sequence SET seq = 0 WHERE name = '{table}';");
                }

                AppLogger.Info("AUTOINCREMENT sequences reset for transactional tables.");
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Failed to reset AUTOINCREMENT sequences: " + ex.Message);
                // This is not critical, so we don't throw
            }
        }

        /// <summary>
        /// Gets the row count for a specific table.
        /// </summary>
        private static int GetTableCount(SqliteConnection conn, string tableName)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT COUNT(*) FROM {tableName};";
                var result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : 0;
            }
        }

        /// <summary>
        /// Executes a raw SQL command.
        /// </summary>
        private static void Execute(SqliteConnection conn, string sql)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }
    }

    /// <summary>
    /// Summary of database reset operation including counts and status.
    /// </summary>
    public class DatabaseResetSummary
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? ErrorDetails { get; set; }

        // Counts before deletion
        public int BillsBeforeDelete { get; set; }
        public int BillItemsBeforeDelete { get; set; }
        public int BillPaymentBeforeDelete { get; set; }
        public int BillReturnsBeforeDelete { get; set; }
        public int InventoryLogsBeforeDelete { get; set; }
        public int CustomerLedgerBeforeDelete { get; set; }

        // Master data preserved counts
        public int ItemsPreserved { get; set; }
        public int CustomersPreserved { get; set; }
        public int AccountsPreserved { get; set; }
        public int UsersPreserved { get; set; }
        public int CategoriesPreserved { get; set; }

        public override string ToString()
        {
            if (!IsSuccess)
            {
                return $"FAILED: {Message}\n{ErrorDetails}";
            }

            var summary = $@"
================================================================================
Database Reset Summary
================================================================================
Status: {(IsSuccess ? "✓ SUCCESS" : "✗ FAILED")}

DELETED TRANSACTIONAL DATA:
  Bills:               {BillsBeforeDelete} records
  BillItems:          {BillItemsBeforeDelete} records
  Payments:           {BillPaymentBeforeDelete} records
  Returns:            {BillReturnsBeforeDelete} records
  Inventory Logs:     {InventoryLogsBeforeDelete} records
  Customer Ledger:    {CustomerLedgerBeforeDelete} records

PRESERVED MASTER DATA:
  ✓ Items:            {ItemsPreserved} records
  ✓ Customers:        {CustomersPreserved} records
  ✓ Accounts:         {AccountsPreserved} records
  ✓ Users:            {UsersPreserved} records
  ✓ Categories:       {CategoriesPreserved} records

{Message}
================================================================================
";
            return summary;
        }
    }
}
