using System;
using System.IO;
using Microsoft.Data.Sqlite;
using GroceryPOS.Helpers;

namespace GroceryPOS.Data
{
    /// <summary>
    /// Centralized database connection helper.
    /// Replaces EF Core's AppDbContext with raw Microsoft.Data.Sqlite.
    /// </summary>
    public static class DatabaseHelper
    {
        private static readonly string DbPath;
        private static readonly string ConnectionString;

        static DatabaseHelper()
        {
            // Use the application's base directory (portable mode)
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            
            DbPath = Path.Combine(baseDir, "GroceryPOS.db");
            ConnectionString = $"Data Source={DbPath}";
        }

        /// <summary>
        /// Returns the absolute path to the database file.
        /// </summary>
        public static string GetDatabasePath() => DbPath;

        /// <summary>
        /// Creates and returns a new open SQLite connection.
        /// Caller is responsible for disposing.
        /// Foreign keys are enabled on every connection.
        /// </summary>
        public static SqliteConnection GetConnection()
        {
            var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            // Enable foreign key enforcement and optimization settings
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                PRAGMA foreign_keys = ON;
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = FULL;
            ";
            cmd.ExecuteNonQuery();

            return connection;
        }

        /// <summary>
        /// Creates and returns a new open connection with WAL journal mode
        /// for better concurrent read/write performance.
        /// </summary>
        public static SqliteConnection GetWalConnection()
        {
            var connection = GetConnection();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode = WAL;";
            cmd.ExecuteNonQuery();

            return connection;
        }

        /// <summary>
        /// Performs database maintenance (Vacuum).
        /// Should be called during off-peak times or on application exit.
        /// </summary>
        public static void MaintainDatabase()
        {
            try
            {
                using var conn = GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "VACUUM;";
                cmd.ExecuteNonQuery();
                AppLogger.Info("Database maintenance (VACUUM) completed successfully.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Database maintenance failed", ex);
            }
        }

        /// <summary>
        /// Returns diagnostic information about the current database.
        /// </summary>
        public static string GetDatabaseDiagnostics()
        {
            try
            {
                using var conn = GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM Bills;";
                var count = cmd.ExecuteScalar();
                return $"[DB DIAGNOSTIC] Path: {DbPath} | Total Bills: {count}";
            }
            catch (Exception ex)
            {
                return $"[DB DIAGNOSTIC ERROR] Path: {DbPath} | Error: {ex.Message}";
            }
        }
    }
}
