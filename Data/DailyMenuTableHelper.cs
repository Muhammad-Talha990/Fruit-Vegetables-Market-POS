using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using FruitVegetableMarketPOS.Helpers;

namespace FruitVegetableMarketPOS.Data
{
    /// <summary>
    /// One SQLite table per business date for the POS daily menu.
    /// Example: 2026-08-11 → DailyMenu_20260811
    /// </summary>
    public static class DailyMenuTableHelper
    {
        private static readonly Regex DateKeyRegex =
            new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

        public static string ToTableName(string businessDate)
        {
            var key = NormalizeBusinessDate(businessDate);
            // Safe: only digits after validation/normalization
            return "DailyMenu_" + key.Replace("-", "", StringComparison.Ordinal);
        }

        public static string NormalizeBusinessDate(string businessDate)
        {
            if (string.IsNullOrWhiteSpace(businessDate))
                throw new ArgumentException("Business date is required.", nameof(businessDate));

            var trimmed = businessDate.Trim();
            if (!DateKeyRegex.IsMatch(trimmed))
                throw new ArgumentException($"Invalid business date '{businessDate}'. Expected yyyy-MM-dd.", nameof(businessDate));

            if (!DateTime.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _))
                throw new ArgumentException($"Invalid calendar date '{businessDate}'.", nameof(businessDate));

            return trimmed;
        }

        public static void EnsureRegistry(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS DailyMenuRegistry (
                    BusinessDate TEXT PRIMARY KEY,
                    TableName    TEXT NOT NULL,
                    CreatedAt    TEXT NOT NULL
                );";
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Creates DailyMenu_yyyyMMdd if missing and registers it.
        /// Idempotent — safe on every Add / Continue / Refresh.
        /// </summary>
        public static string EnsureDayTable(SqliteConnection conn, string businessDate)
        {
            EnsureRegistry(conn);
            var date = NormalizeBusinessDate(businessDate);
            var table = ToTableName(date);

            using (var create = conn.CreateCommand())
            {
                create.CommandText = $@"
                    CREATE TABLE IF NOT EXISTS [{table}] (
                        DailySelectionId INTEGER PRIMARY KEY AUTOINCREMENT,
                        ItemId           INTEGER NOT NULL UNIQUE,
                        IsAvailable      INTEGER NOT NULL DEFAULT 1,
                        Note             TEXT
                    );";
                create.ExecuteNonQuery();
            }

            EnsureNoteColumn(conn, table);

            using (var reg = conn.CreateCommand())
            {
                reg.CommandText = @"
                    INSERT INTO DailyMenuRegistry (BusinessDate, TableName, CreatedAt)
                    VALUES (@date, @table, @created)
                    ON CONFLICT(BusinessDate) DO UPDATE SET TableName = excluded.TableName;";
                reg.Parameters.AddWithValue("@date", date);
                reg.Parameters.AddWithValue("@table", table);
                reg.Parameters.AddWithValue("@created", DateTimeHelper.CaptureTransactionTime().ToDbString());
                reg.ExecuteNonQuery();
            }

            return table;
        }

        /// <summary>Adds Note column to an existing day table if missing (idempotent).</summary>
        public static void EnsureNoteColumn(SqliteConnection conn, string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName)) return;

            using var info = conn.CreateCommand();
            info.CommandText = $"PRAGMA table_info([{tableName}]);";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetString(1).Equals("Note", StringComparison.OrdinalIgnoreCase))
                    return;
            }
            reader.Close();

            using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE [{tableName}] ADD COLUMN Note TEXT;";
            alter.ExecuteNonQuery();
        }

        /// <summary>Ensures every registered DailyMenu_* table has a Note column.</summary>
        public static void EnsureNoteColumnOnAllRegisteredTables(SqliteConnection conn)
        {
            EnsureRegistry(conn);
            foreach (var date in GetAllRegisteredDates(conn))
            {
                try
                {
                    var table = ToTableName(date);
                    if (DayTableExists(conn, date))
                        EnsureNoteColumn(conn, table);
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"EnsureNoteColumn skipped for {date}: {ex.Message}", ex);
                }
            }
        }

        public static bool DayTableExists(SqliteConnection conn, string businessDate)
        {
            try
            {
                var table = ToTableName(NormalizeBusinessDate(businessDate));
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@name LIMIT 1;";
                cmd.Parameters.AddWithValue("@name", table);
                return cmd.ExecuteScalar() != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsRegistered(SqliteConnection conn, string businessDate)
        {
            EnsureRegistry(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM DailyMenuRegistry WHERE BusinessDate = @date LIMIT 1;";
            cmd.Parameters.AddWithValue("@date", NormalizeBusinessDate(businessDate));
            return cmd.ExecuteScalar() != null;
        }

        /// <summary>Most recent registered business date strictly before <paramref name="beforeDate"/>.</summary>
        public static string? GetPreviousRegisteredDate(SqliteConnection conn, string beforeDate)
        {
            EnsureRegistry(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT BusinessDate
                FROM DailyMenuRegistry
                WHERE BusinessDate < @before
                ORDER BY BusinessDate DESC
                LIMIT 1;";
            cmd.Parameters.AddWithValue("@before", NormalizeBusinessDate(beforeDate));
            return cmd.ExecuteScalar()?.ToString();
        }

        public static List<string> GetAllRegisteredDates(SqliteConnection conn)
        {
            EnsureRegistry(conn);
            var list = new List<string>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT BusinessDate FROM DailyMenuRegistry ORDER BY BusinessDate;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(reader.GetString(0));
            return list;
        }
    }
}
