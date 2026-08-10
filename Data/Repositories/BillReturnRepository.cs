using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Data.Repositories
{
    /// <summary>
    /// Data access for the BILL_RETURNS table.
    /// </summary>
    public class BillReturnRepository
    {
        /// <summary>
        /// Inserts a new return record and its items.
        /// </summary>
        public int InsertReturnHeader(int billId, double refundAmount, SqliteConnection conn, SqliteTransaction txn)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = @"
                INSERT INTO BillReturns (BillId, RefundAmount, ReturnedAt)
                VALUES (@billId, @amount, @at);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@billId", billId);
            cmd.Parameters.AddWithValue("@amount", refundAmount);
            cmd.Parameters.AddWithValue("@at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void InsertReturnItem(int returnId, int billItemId, int quantity, double unitPrice, SqliteConnection conn, SqliteTransaction txn)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = @"
                INSERT INTO BillReturnItems (ReturnId, BillItemId, Quantity, UnitPrice)
                VALUES (@retId, @biId, @qty, @price);";
            
            cmd.Parameters.AddWithValue("@retId", returnId);
            cmd.Parameters.AddWithValue("@biId", billItemId);
            cmd.Parameters.AddWithValue("@qty", quantity);
            cmd.Parameters.AddWithValue("@price", unitPrice);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Calculates the total quantity already returned for a specific product in a bill.
        /// </summary>
        public int GetTotalReturnedQuantity(int billId, int itemId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(bri.Quantity), 0) 
                FROM BillReturnItems bri
                JOIN BillReturns br ON bri.ReturnId = br.ReturnId
                JOIN BillItems bi ON bri.BillItemId = bi.BillItemId
                WHERE br.BillId = @billId AND bi.ItemId = @itemId;";
            cmd.Parameters.AddWithValue("@billId", billId);
            cmd.Parameters.AddWithValue("@itemId", itemId);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>
        /// Gets all return records for a specific original bill.
        /// </summary>
        public List<BillReturn> GetByOriginalBillId(int billId)
        {
            var returns = new List<BillReturn>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            // Barcode is optional on Items — never read it without IsDBNull.
            cmd.CommandText = @"
                SELECT br.ReturnId, br.BillId, br.RefundAmount, br.ReturnedAt,
                       bri.Quantity, bri.UnitPrice, i.Barcode, i.Description, bi.ItemId
                FROM BillReturns br
                JOIN BillReturnItems bri ON br.ReturnId = bri.ReturnId
                JOIN BillItems bi ON bri.BillItemId = bi.BillItemId
                JOIN Items i ON bi.ItemId = i.ItemId
                WHERE br.BillId = @billId
                ORDER BY br.ReturnedAt DESC;";
            cmd.Parameters.AddWithValue("@billId", billId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var barcode = reader.IsDBNull(6) ? null : reader.GetString(6);
                var itemId = reader.IsDBNull(8) ? 0 : reader.GetInt32(8);

                returns.Add(new BillReturn
                {
                    Id = reader.GetInt32(0),
                    BillId = reader.GetInt32(1),
                    RefundAmount = reader.IsDBNull(2) ? 0 : reader.GetDouble(2),
                    ReturnDate = reader.IsDBNull(3)
                        ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        : reader.GetString(3),
                    ReturnQuantity = reader.IsDBNull(4)
                        ? 0
                        : Convert.ToInt32(Math.Round(reader.GetDouble(4))),
                    UnitPrice = reader.IsDBNull(5) ? 0 : reader.GetDouble(5),
                    ProductId = !string.IsNullOrWhiteSpace(barcode) ? barcode : itemId.ToString(),
                    ProductDescription = reader.IsDBNull(7) ? string.Empty : reader.GetString(7)
                });
            }
            return returns;
        }
    }
}
