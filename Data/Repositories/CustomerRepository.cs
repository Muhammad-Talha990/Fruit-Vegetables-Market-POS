using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using GroceryPOS.Helpers;
using GroceryPOS.Models;

namespace GroceryPOS.Data.Repositories
{
    /// <summary>
    /// Data access for the Customers table.
    /// Supports full CRUD, soft-delete, credit querying, and filtered search.
    /// </summary>
    /// <summary>
    /// Data access for the Customers table (Normalized 3NF).
    /// Supports full CRUD, soft-delete, and calculated credit balance.
    /// </summary>
    public class CustomerRepository
    {
        // ────────────────────────────────────────────
        //  WRITE operations
        // ────────────────────────────────────────────

        /// <summary>Inserts a new customer record.</summary>
        public void Save(Customer customer)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Customers (FullName, Phone, SecondaryPhone, Address, Address2, Address3, IsActive)
                VALUES (@fullName, @phone, @secondaryPhone, @address, @address2, @address3, 1);
                SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("@fullName", customer.FullName);
            cmd.Parameters.AddWithValue("@phone",          NormalizePhone(customer.Phone));
            cmd.Parameters.AddWithValue("@secondaryPhone", (object?)customer.SecondaryPhone ?? DBNull.Value);

            cmd.Parameters.AddWithValue("@address",  (object?)customer.Address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@address2", (object?)customer.Address2 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@address3", (object?)customer.Address3 ?? DBNull.Value);

            customer.CustomerId = Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>Updates an existing customer's editable fields.</summary>
        public void Update(Customer customer)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Customers
                SET FullName       = @fullName,
                    Phone          = @phone,
                    SecondaryPhone = @secondaryPhone,
                    Address        = @address,
                    Address2       = @address2,
                    Address3       = @address3
                WHERE CustomerId = @id;";

            cmd.Parameters.AddWithValue("@fullName", customer.FullName);
            cmd.Parameters.AddWithValue("@phone",          NormalizePhone(customer.Phone));
            cmd.Parameters.AddWithValue("@secondaryPhone", (object?)customer.SecondaryPhone ?? DBNull.Value);

            cmd.Parameters.AddWithValue("@address",  (object?)customer.Address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@address2", (object?)customer.Address2 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@address3", (object?)customer.Address3 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id",       customer.CustomerId);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Soft-deletes a customer by setting IsActive = 0.</summary>
        public bool SoftDelete(int customerId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Customers SET IsActive = 0 WHERE CustomerId = @id;";
            cmd.Parameters.AddWithValue("@id", customerId);
            return cmd.ExecuteNonQuery() > 0;
        }

        /// <summary>Reactivates a previously deactivated customer.</summary>
        public bool Reactivate(int customerId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Customers SET IsActive = 1 WHERE CustomerId = @id;";
            cmd.Parameters.AddWithValue("@id", customerId);
            return cmd.ExecuteNonQuery() > 0;
        }

        // ────────────────────────────────────────────
        //  READ operations
        // ────────────────────────────────────────────

        private const string CustomerSelectSql = @"
            SELECT c.*,
                   (SELECT COUNT(*) FROM Bills WHERE CustomerId = c.CustomerId) as BillCount,
                   (SELECT MAX(CreatedAt) FROM Bills WHERE CustomerId = c.CustomerId) as LastVisit,
                   (
                       SELECT COALESCE(SUM(bill_total), 0)
                       FROM (
                           SELECT (SUM(bi.Quantity * bi.UnitPrice) + b.TaxAmount - b.DiscountAmount
                               - COALESCE((SELECT SUM(bri.Quantity * bri.UnitPrice)
                                       FROM BillReturnItems bri
                                       JOIN BillReturns br ON bri.ReturnId = br.ReturnId
                                       WHERE br.BillId = b.BillId), 0)) as bill_total
                           FROM Bills b
                           JOIN BillItems bi ON b.BillId = bi.BillId
                           WHERE b.CustomerId = c.CustomerId AND b.Status != 'Cancelled'
                           GROUP BY b.BillId
                       )
                   ) as TotalAmount,
                   (
                       SELECT COALESCE(SUM(bill_balance), 0)
                       FROM (
                           SELECT b.BillId,
                                  CASE
                                      WHEN (
                                        (
                                          (SELECT COALESCE(SUM(bi.Quantity * bi.UnitPrice), 0) FROM BillItems bi WHERE bi.BillId = b.BillId)
                                          + COALESCE(b.TaxAmount, 0) - COALESCE(b.DiscountAmount, 0)
                                        )
                                        - COALESCE((SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0)
                                                    FROM BillReturnItems bri
                                                    JOIN BillReturns br ON bri.ReturnId = br.ReturnId
                                                    WHERE br.BillId = b.BillId), 0)
                                        - COALESCE(b.InitialPayment, 0)
                                        - COALESCE((SELECT SUM(Amount) FROM bill_payment WHERE BillId = b.BillId AND Type = 'payment'), 0)
                                      ) > 0
                                      THEN (
                                        (
                                          (SELECT COALESCE(SUM(bi.Quantity * bi.UnitPrice), 0) FROM BillItems bi WHERE bi.BillId = b.BillId)
                                          + COALESCE(b.TaxAmount, 0) - COALESCE(b.DiscountAmount, 0)
                                        )
                                        - COALESCE((SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0)
                                                    FROM BillReturnItems bri
                                                    JOIN BillReturns br ON bri.ReturnId = br.ReturnId
                                                    WHERE br.BillId = b.BillId), 0)
                                        - COALESCE(b.InitialPayment, 0)
                                        - COALESCE((SELECT SUM(Amount) FROM bill_payment WHERE BillId = b.BillId AND Type = 'payment'), 0)
                                      )
                                      ELSE 0
                                  END as bill_balance
                           FROM Bills b
                           WHERE b.CustomerId = c.CustomerId AND b.Status != 'Cancelled'
                       )
                   ) as PendingCredit
            FROM Customers c";

        /// <summary>
        /// Returns all customers for the management grid (includes inactive ones).
        /// </summary>
        public List<Customer> GetAll(bool activeOnly = false)
        {
            var customers = new List<Customer>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = CustomerSelectSql + (activeOnly ? " WHERE c.IsActive = 1" : "") + " ORDER BY c.FullName ASC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                customers.Add(MapCustomer(reader));

            return customers;
        }

        /// <summary>
        /// Searches active customers by name or phone (used in billing dropdown).
        /// </summary>
        public List<Customer> Search(string query)
        {
            string normalized = NormalizePhone(query);
            var customers = new List<Customer>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = CustomerSelectSql + " WHERE c.IsActive = 1";

            if (!string.IsNullOrEmpty(normalized))
            {
                cmd.CommandText += " AND (c.FullName LIKE @nameQuery OR c.Phone LIKE @phoneQuery OR c.Phone = @exactPhone)";
                cmd.Parameters.AddWithValue("@phoneQuery", "%" + normalized);
                cmd.Parameters.AddWithValue("@exactPhone", normalized);
            }
            else
            {
                cmd.CommandText += " AND c.FullName LIKE @nameQuery";
            }

            cmd.CommandText += " ORDER BY c.FullName ASC LIMIT 10;";
            cmd.Parameters.AddWithValue("@nameQuery", "%" + query + "%");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                customers.Add(MapCustomer(reader));

            return customers;
        }

        public Customer? GetByPhone(string phone)
        {
            string normalized = NormalizePhone(phone);
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = CustomerSelectSql + " WHERE c.Phone = @phone";
            cmd.Parameters.AddWithValue("@phone", normalized);

            using var reader = cmd.ExecuteReader();
            if (reader.Read()) return MapCustomer(reader);
            return null;
        }

        public Customer? GetById(int id)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = CustomerSelectSql + " WHERE c.CustomerId = @id;";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            if (reader.Read()) return MapCustomer(reader);
            return null;
        }

        /// <summary>Returns total pending credit for a specific customer.</summary>
        public double GetPendingCredit(int customerId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(bill_balance), 0)
                FROM (
                    SELECT b.BillId,
                           CASE
                               WHEN (
                                 (
                                   (SELECT COALESCE(SUM(bi.Quantity * bi.UnitPrice), 0) FROM BillItems bi WHERE bi.BillId = b.BillId)
                                   + COALESCE(b.TaxAmount, 0) - COALESCE(b.DiscountAmount, 0)
                                 )
                                 - COALESCE((SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0)
                                             FROM BillReturnItems bri
                                             JOIN BillReturns br ON bri.ReturnId = br.ReturnId
                                             WHERE br.BillId = b.BillId), 0)
                                 - COALESCE(b.InitialPayment, 0)
                                 - COALESCE((SELECT SUM(Amount) FROM bill_payment WHERE BillId = b.BillId AND Type = 'payment'), 0)
                               ) > 0
                               THEN (
                                 (
                                   (SELECT COALESCE(SUM(bi.Quantity * bi.UnitPrice), 0) FROM BillItems bi WHERE bi.BillId = b.BillId)
                                   + COALESCE(b.TaxAmount, 0) - COALESCE(b.DiscountAmount, 0)
                                 )
                                 - COALESCE((SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0)
                                             FROM BillReturnItems bri
                                             JOIN BillReturns br ON bri.ReturnId = br.ReturnId
                                             WHERE br.BillId = b.BillId), 0)
                                 - COALESCE(b.InitialPayment, 0)
                                 - COALESCE((SELECT SUM(Amount) FROM bill_payment WHERE BillId = b.BillId AND Type = 'payment'), 0)
                               )
                               ELSE 0
                           END as bill_balance
                    FROM Bills b
                    WHERE b.CustomerId = @cid AND b.Status != 'Cancelled'
                )";
            cmd.Parameters.AddWithValue("@cid", customerId);
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        // ────────────────────────────────────────────
        //  Private helpers
        // ────────────────────────────────────────────

        public string NormalizePhone(string phone)
        {
            if (string.IsNullOrEmpty(phone)) return string.Empty;
            var result = new System.Text.StringBuilder();
            foreach (char c in phone)
                if (char.IsDigit(c)) result.Append(c);
            return result.ToString();
        }

        private Customer MapCustomer(SqliteDataReader reader)
        {
            var customer = new Customer
            {
                CustomerId    = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                FullName      = reader.GetString(reader.GetOrdinal("FullName")),
                Phone         = reader.GetString(reader.GetOrdinal("Phone")),
                Address       = reader.IsDBNull(reader.GetOrdinal("Address")) ? null : reader.GetString(reader.GetOrdinal("Address")),
                IsActive      = reader.GetInt32(reader.GetOrdinal("IsActive")) != 0,
                CreatedAt     = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                BillCount     = reader.GetInt32(reader.GetOrdinal("BillCount")),
                TotalAmount   = reader.GetDouble(reader.GetOrdinal("TotalAmount")),
                PendingCredit = reader.GetDouble(reader.GetOrdinal("PendingCredit")),
                LastVisitDate = reader.IsDBNull(reader.GetOrdinal("LastVisit")) ? null : reader.GetDateTime(reader.GetOrdinal("LastVisit"))
            };
            if (reader.HasColumn("SecondaryPhone")) customer.SecondaryPhone = reader.IsDBNull(reader.GetOrdinal("SecondaryPhone")) ? null : reader.GetString(reader.GetOrdinal("SecondaryPhone"));
            if (reader.HasColumn("Address2")) customer.Address2 = reader.IsDBNull(reader.GetOrdinal("Address2")) ? null : reader.GetString(reader.GetOrdinal("Address2"));
            if (reader.HasColumn("Address3")) customer.Address3 = reader.IsDBNull(reader.GetOrdinal("Address3")) ? null : reader.GetString(reader.GetOrdinal("Address3"));
            return customer;
        }
    }

    // Keep extension class in the same namespace (was already here)
    public static class SqliteExtensions
    {
        public static bool HasColumn(this SqliteDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
                if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
