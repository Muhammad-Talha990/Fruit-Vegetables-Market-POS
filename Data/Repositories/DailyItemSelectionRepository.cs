using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Data.Repositories
{
    /// <summary>
    /// Daily menu storage: one SQLite table per business date (DailyMenu_yyyyMMdd)
    /// plus DailyMenuRegistry for previous-day / report lookup.
    /// </summary>
    public class DailyItemSelectionRepository
    {
        public void EnsureDayTable(string businessDate)
        {
            using var conn = DatabaseHelper.GetConnection();
            DailyMenuTableHelper.EnsureDayTable(conn, businessDate);
        }

        public List<DailyItemSelection> GetVisibleForDate(string businessDate)
        {
            var rows = new List<DailyItemSelection>();
            using var conn = DatabaseHelper.GetConnection();
            if (!DailyMenuTableHelper.DayTableExists(conn, businessDate))
                return rows;

            var table = DailyMenuTableHelper.ToTableName(businessDate);
            using var cmd = conn.CreateCommand();
            DailyMenuTableHelper.EnsureNoteColumn(conn, table);
            cmd.CommandText = $@"
                SELECT d.DailySelectionId, @date AS BusinessDate, d.ItemId, d.IsAvailable, d.Note,
                       i.Description AS ItemDescription, i.NameUrdu AS ItemNameUrdu
                FROM [{table}] d
                JOIN Items i ON i.ItemId = d.ItemId
                WHERE i.IsActive = 1
                ORDER BY d.DailySelectionId;";
            cmd.Parameters.AddWithValue("@date", DailyMenuTableHelper.NormalizeBusinessDate(businessDate));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                rows.Add(MapSelection(reader));

            return rows;
        }

        /// <summary>
        /// ItemId · Description · Type · Sale for a business date.
        /// Sale = SUM(BillItems.Quantity) for that item/type on that date.
        /// </summary>
        public List<DailyItemSetRow> GetDailyItemSetForDate(string businessDate)
        {
            var rows = new List<DailyItemSetRow>();
            using var conn = DatabaseHelper.GetConnection();
            if (!DailyMenuTableHelper.DayTableExists(conn, businessDate))
                return rows;

            var date = DailyMenuTableHelper.NormalizeBusinessDate(businessDate);
            var table = DailyMenuTableHelper.ToTableName(date);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT
                    @date AS BusinessDate,
                    d.ItemId,
                    i.Description AS ItemDescription,
                    t.TypeName AS Type,
                    COALESCE((
                        SELECT SUM(bi.Quantity)
                        FROM BillItems bi
                        INNER JOIN Bills b ON b.BillId = bi.BillId
                        WHERE bi.ItemId = d.ItemId
                          AND (
                                bi.TypeId = t.TypeId
                                OR (bi.TypeId IS NULL AND t.SortOrder = 1)
                              )
                          AND IFNULL(b.Status, '') != 'Cancelled'
                          AND date(datetime(b.CreatedAt, 'localtime')) = @date
                    ), 0) AS Sale
                FROM [{table}] d
                JOIN Items i ON i.ItemId = d.ItemId
                JOIN ItemTypes t ON t.ItemId = d.ItemId AND t.IsActive = 1
                WHERE i.IsActive = 1
                ORDER BY i.Description, t.SortOrder, t.TypeId;";
            cmd.Parameters.AddWithValue("@date", date);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new DailyItemSetRow
                {
                    BusinessDate = reader.GetString(0),
                    ItemId = reader.GetInt32(1),
                    ItemDescription = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Type = reader.IsDBNull(3) ? "Type 1" : reader.GetString(3),
                    Sale = reader.IsDBNull(4) ? 0 : reader.GetDouble(4)
                });
            }

            return rows;
        }

        /// <summary>Most recent business date before <paramref name="beforeDate"/> that has a registered menu table.</summary>
        public string? GetPreviousMenuDate(string beforeDate)
        {
            using var conn = DatabaseHelper.GetConnection();
            return DailyMenuTableHelper.GetPreviousRegisteredDate(conn, beforeDate);
        }

        public void ClearForDate(string businessDate)
        {
            using var conn = DatabaseHelper.GetConnection();
            var table = DailyMenuTableHelper.EnsureDayTable(conn, businessDate);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM [{table}];";
            cmd.ExecuteNonQuery();
        }

        public string? GetAppSetting(string key)
        {
            using var conn = DatabaseHelper.GetConnection();
            EnsureAppSettings(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = @key LIMIT 1;";
            cmd.Parameters.AddWithValue("@key", key);
            return cmd.ExecuteScalar()?.ToString();
        }

        public void SetAppSetting(string key, string value)
        {
            using var conn = DatabaseHelper.GetConnection();
            EnsureAppSettings(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO AppSettings (Key, Value) VALUES (@key, @val)
                ON CONFLICT(Key) DO UPDATE SET Value = @val;";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@val", value);
            cmd.ExecuteNonQuery();
        }

        private static void EnsureAppSettings(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS AppSettings (
                    Key   TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );";
            cmd.ExecuteNonQuery();
        }

        public int AddItem(string businessDate, int itemId, int? userId = null, string? note = null)
        {
            if (HasActiveRow(businessDate, itemId))
                throw new InvalidOperationException("This item is already on today's selection.");

            using var conn = DatabaseHelper.GetConnection();
            var table = DailyMenuTableHelper.EnsureDayTable(conn, businessDate);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                INSERT INTO [{table}] (ItemId, IsAvailable, Note)
                VALUES (@itemId, 1, @note);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@itemId", itemId);
            cmd.Parameters.AddWithValue("@note",
                string.IsNullOrWhiteSpace(note) ? DBNull.Value : note.Trim());

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void SetNote(string businessDate, int itemId, string? note)
        {
            using var conn = DatabaseHelper.GetConnection();
            if (!DailyMenuTableHelper.DayTableExists(conn, businessDate))
                return;

            var table = DailyMenuTableHelper.ToTableName(businessDate);
            DailyMenuTableHelper.EnsureNoteColumn(conn, table);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                UPDATE [{table}]
                SET Note = @note
                WHERE ItemId = @itemId;";
            cmd.Parameters.AddWithValue("@itemId", itemId);
            cmd.Parameters.AddWithValue("@note",
                string.IsNullOrWhiteSpace(note) ? DBNull.Value : note.Trim());
            cmd.ExecuteNonQuery();
        }

        public void RemoveItem(string businessDate, int dailySelectionId)
        {
            using var conn = DatabaseHelper.GetConnection();
            if (!DailyMenuTableHelper.DayTableExists(conn, businessDate))
                return;

            var table = DailyMenuTableHelper.ToTableName(businessDate);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM [{table}] WHERE DailySelectionId = @id;";
            cmd.Parameters.AddWithValue("@id", dailySelectionId);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Deactivate / reactivate for that day without removing from the selling list.</summary>
        public void SetAvailable(string businessDate, int dailySelectionId, bool isAvailable)
        {
            using var conn = DatabaseHelper.GetConnection();
            if (!DailyMenuTableHelper.DayTableExists(conn, businessDate))
                return;

            var table = DailyMenuTableHelper.ToTableName(businessDate);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                UPDATE [{table}]
                SET IsAvailable = @avail
                WHERE DailySelectionId = @id;";
            cmd.Parameters.AddWithValue("@id", dailySelectionId);
            cmd.Parameters.AddWithValue("@avail", isAvailable ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        public bool HasActiveRow(string businessDate, int itemId)
        {
            using var conn = DatabaseHelper.GetConnection();
            if (!DailyMenuTableHelper.DayTableExists(conn, businessDate))
                return false;

            var table = DailyMenuTableHelper.ToTableName(businessDate);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM [{table}] WHERE ItemId = @itemId;";
            cmd.Parameters.AddWithValue("@itemId", itemId);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        private static DailyItemSelection MapSelection(SqliteDataReader reader)
        {
            var descOrd = reader.GetOrdinal("ItemDescription");
            var urduOrd = reader.GetOrdinal("ItemNameUrdu");
            var availOrd = reader.GetOrdinal("IsAvailable");
            var noteOrd = -1;
            try { noteOrd = reader.GetOrdinal("Note"); } catch { /* older readers */ }

            return new DailyItemSelection
            {
                DailySelectionId = reader.GetInt32(reader.GetOrdinal("DailySelectionId")),
                BusinessDate     = reader.GetString(reader.GetOrdinal("BusinessDate")),
                ItemId           = reader.GetInt32(reader.GetOrdinal("ItemId")),
                IsAvailable      = reader.IsDBNull(availOrd) || reader.GetInt32(availOrd) != 0,
                Note             = noteOrd >= 0 && !reader.IsDBNull(noteOrd) ? reader.GetString(noteOrd) : null,
                ItemDescription  = reader.IsDBNull(descOrd) ? null : reader.GetString(descOrd),
                ItemNameUrdu     = reader.IsDBNull(urduOrd) ? null : reader.GetString(urduOrd)
            };
        }
    }
}
