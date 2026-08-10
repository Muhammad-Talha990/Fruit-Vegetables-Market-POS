using System;
using System.IO;
using Microsoft.Data.Sqlite;
using FruitVegetableMarketPOS.Helpers;

namespace FruitVegetableMarketPOS.Data
{
    /// <summary>
    /// Centralized database connection helper.
    /// Replaces EF Core's AppDbContext with raw Microsoft.Data.Sqlite.
    /// </summary>
    public static class DatabaseHelper
    {
        private const string AppFolderName = "FruitVegetableMarketPOS";
        private const string DbFileName = "FruitVegetableMarketPOS.db";
        private const string LegacyAppFolderName = "GroceryPOS";
        private const string LegacyDbFileName = "GroceryPOS.db";

        private static readonly string DbPath;
        private static readonly string ConnectionString;

        static DatabaseHelper()
        {
            // Use LocalAppData for production deployment to ensure write permissions
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appDataPath, AppFolderName);

            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }

            DbPath = Path.Combine(appFolder, DbFileName);
            MigrateFromLegacyLocationIfNeeded(appDataPath, appFolder);

            ConnectionString = $"Data Source={DbPath}";
        }

        /// <summary>
        /// One-time copy from the old GroceryPOS AppData location so existing shops keep their data.
        /// </summary>
        private static void MigrateFromLegacyLocationIfNeeded(string appDataPath, string appFolder)
        {
            try
            {
                if (File.Exists(DbPath))
                    return;

                var legacyFolder = Path.Combine(appDataPath, LegacyAppFolderName);
                var legacyDb = Path.Combine(legacyFolder, LegacyDbFileName);
                if (!File.Exists(legacyDb))
                    return;

                File.Copy(legacyDb, DbPath, overwrite: false);
                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    var legacySide = legacyDb + suffix;
                    var newSide = DbPath + suffix;
                    if (File.Exists(legacySide) && !File.Exists(newSide))
                        File.Copy(legacySide, newSide, overwrite: false);
                }

                var legacyImages = Path.Combine(legacyFolder, "Images");
                var newImages = Path.Combine(appFolder, "Images");
                if (Directory.Exists(legacyImages) && !Directory.Exists(newImages))
                    CopyDirectory(legacyImages, newImages);

                AppLogger.Info($"Migrated database from legacy path to: {DbPath}");
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Legacy database migration skipped or failed", ex);
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(sourceDir, file);
                var dest = Path.Combine(destDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: false);
            }
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
