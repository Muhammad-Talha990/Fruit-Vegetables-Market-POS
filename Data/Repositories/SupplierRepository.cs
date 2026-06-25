using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using GroceryPOS.Helpers;
using GroceryPOS.Models;

namespace GroceryPOS.Data.Repositories
{
    public class SupplierRepository
    {
        public List<Supplier> GetAll()
        {
            var list = new List<Supplier>();
            using (var conn = DatabaseHelper.GetConnection())
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM Suppliers ORDER BY Name ASC;";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(MapSupplier(reader));
                    }
                }
            }
            return list;
        }

        public Supplier? GetByPhone(string phone)
        {
            using (var conn = DatabaseHelper.GetConnection())
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM Suppliers WHERE PhoneNumber = @phone;";
                cmd.Parameters.AddWithValue("@phone", phone);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read()) return MapSupplier(reader);
                }
            }
            return null;
        }

        public bool Add(Supplier supplier)
        {
            using (var conn = DatabaseHelper.GetConnection())
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Suppliers (PhoneNumber, Name, CompanyName, Email, Address, CreatedAt)
                    VALUES (@phone, @name, @company, @email, @address, @at);";
                AddParameters(cmd, supplier);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        public bool Update(Supplier supplier)
        {
            using (var conn = DatabaseHelper.GetConnection())
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    UPDATE Suppliers 
                    SET Name = @name, CompanyName = @company, Email = @email, Address = @address
                    WHERE PhoneNumber = @phone;";
                AddParameters(cmd, supplier);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        public bool Delete(string phone)
        {
            using (var conn = DatabaseHelper.GetConnection())
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM Suppliers WHERE PhoneNumber = @phone;";
                cmd.Parameters.AddWithValue("@phone", phone);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        // --- Supplier-Product Mappings ---

        public List<SupplierProduct> GetProductsBySupplier(string supplierPhone)
        {
            var list = new List<SupplierProduct>();
            using (var conn = DatabaseHelper.GetConnection())
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT sp.*, i.Description as ProductName
                    FROM SupplierProducts sp
                    JOIN Items i ON sp.ProductId = i.ItemId
                    WHERE sp.SupplierPhone = @phone
                    ORDER BY i.Description ASC;";
                cmd.Parameters.AddWithValue("@phone", supplierPhone);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new SupplierProduct
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("Id")),
                            SupplierPhone = reader.GetString(reader.GetOrdinal("SupplierPhone")),
                            ProductId = reader.GetInt32(reader.GetOrdinal("ProductId")),
                            SupplyPrice = reader.IsDBNull(reader.GetOrdinal("SupplyPrice")) ? (double?)null : reader.GetDouble(reader.GetOrdinal("SupplyPrice")),
                            SupplyDate = reader.GetDateTime(reader.GetOrdinal("SupplyDate")),
                            Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
                            ProductName = reader.GetString(reader.GetOrdinal("ProductName"))
                        });
                    }
                }
            }
            return list;
        }

        public List<Supplier> GetSuppliersByProduct(int productId)
        {
            var list = new List<Supplier>();
            using (var conn = DatabaseHelper.GetConnection())
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT s.*
                    FROM Suppliers s
                    JOIN SupplierProducts sp ON s.PhoneNumber = sp.SupplierPhone
                    WHERE sp.ProductId = @pid
                    ORDER BY s.Name ASC;";
                cmd.Parameters.AddWithValue("@pid", productId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(MapSupplier(reader));
                    }
                }
            }
            return list;
        }

        public bool AssignProduct(SupplierProduct mapping)
        {
            using (var conn = DatabaseHelper.GetConnection())
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT OR REPLACE INTO SupplierProducts (SupplierPhone, ProductId, SupplyPrice, SupplyDate, Notes)
                    VALUES (@phone, @pid, @price, @date, @notes);";
                cmd.Parameters.AddWithValue("@phone", mapping.SupplierPhone);
                cmd.Parameters.AddWithValue("@pid", mapping.ProductId);
                cmd.Parameters.AddWithValue("@price", (object?)mapping.SupplyPrice ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@date", mapping.SupplyDate);
                cmd.Parameters.AddWithValue("@notes", (object?)mapping.Notes ?? DBNull.Value);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        public bool UnassignProduct(string supplierPhone, int productId)
        {
            using (var conn = DatabaseHelper.GetConnection())
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM SupplierProducts WHERE SupplierPhone = @phone AND ProductId = @pid;";
                cmd.Parameters.AddWithValue("@phone", supplierPhone);
                cmd.Parameters.AddWithValue("@pid", productId);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        private Supplier MapSupplier(SqliteDataReader reader)
        {
            return new Supplier
            {
                PhoneNumber = reader.GetString(reader.GetOrdinal("PhoneNumber")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                CompanyName = reader.IsDBNull(reader.GetOrdinal("CompanyName")) ? null : reader.GetString(reader.GetOrdinal("CompanyName")),
                Email = reader.IsDBNull(reader.GetOrdinal("Email")) ? null : reader.GetString(reader.GetOrdinal("Email")),
                Address = reader.IsDBNull(reader.GetOrdinal("Address")) ? null : reader.GetString(reader.GetOrdinal("Address")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
            };
        }

        private void AddParameters(SqliteCommand cmd, Supplier s)
        {
            cmd.Parameters.AddWithValue("@phone", s.PhoneNumber);
            cmd.Parameters.AddWithValue("@name", s.Name);
            cmd.Parameters.AddWithValue("@company", (object?)s.CompanyName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@email", (object?)s.Email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@address", (object?)s.Address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@at", s.CreatedAt);
        }
    }
}
