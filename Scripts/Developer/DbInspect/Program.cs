using System;
using Microsoft.Data.Sqlite;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

class Program
{
    static void Main(string[] args)
    {
        var dbPath = args.Length > 0 ? args[0] : "..\\..\\..\\bin\\Debug\\net8.0-windows\\GroceryPOS.db";
        Console.WriteLine($"Inspecting DB: {dbPath}");
        var cs = $"Data Source={dbPath}";
        using var conn = new SqliteConnection(cs);
        conn.Open();

        if (args.Length > 1 && args[1] == "--fix")
        {
            Console.WriteLine("Running fix: replace Customers_v17 references with Customers...");
            FixCustomersV17(conn);
            return;
        }

        if (args.Length > 1 && args[1] == "--fix2")
        {
            Console.WriteLine("Running targeted fix: replace *_old references with base table names for known tables...");
            FixTablesReferencingOld(conn, new string[] { "BillItems", "BillReturnItems", "BillReturns", "bill_payment" });
            return;
        }
            if (args.Length > 1 && args[1] == "--test")
            {
                Console.WriteLine("Running FK enforcement tests (transactional, no persistent changes)...");
                RunFkTests(conn);
                return;
            }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name, type, sql FROM sqlite_master WHERE type IN ('table','index') ORDER BY type, name;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            var type = reader.GetString(1);
            var sql = reader.IsDBNull(2) ? "" : reader.GetString(2);
            Console.WriteLine($"{type}\t{name}\n  {sql}");
        }
    }

    static void FixCustomersV17(SqliteConnection conn)
    {
        const string legacy = "Customers_v17";
        const string replacement = "Customers";

        var tables = new List<(string name, string sql)>();
        using (var find = conn.CreateCommand())
        {
            find.CommandText = "SELECT name, sql FROM sqlite_master WHERE type='table' AND sql LIKE '%' || @legacy || '%';";
            find.Parameters.AddWithValue("@legacy", legacy);
            using var reader = find.ExecuteReader();
            while (reader.Read()) tables.Add((reader.GetString(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
        }

        foreach (var (name, sql) in tables)
        {
            Console.WriteLine($"Fixing table: {name}");
            using var tx = conn.BeginTransaction();
            try
            {
                using (var disableFk = conn.CreateCommand()) { disableFk.CommandText = "PRAGMA foreign_keys = OFF;"; disableFk.ExecuteNonQuery(); }

                var oldName = name + "_old";
                using (var rename = conn.CreateCommand()) { rename.CommandText = $"ALTER TABLE {name} RENAME TO {oldName};"; rename.ExecuteNonQuery(); }

                var newCreate = sql.Replace(legacy, replacement);
                using (var create = conn.CreateCommand()) { create.CommandText = newCreate + ";"; create.ExecuteNonQuery(); }

                // Copy columns
                var cols = new List<string>();
                using (var colsCmd = conn.CreateCommand())
                {
                    colsCmd.CommandText = $"PRAGMA table_info({oldName});";
                    using var colsR = colsCmd.ExecuteReader();
                    while (colsR.Read()) cols.Add(colsR.GetString(colsR.GetOrdinal("name")));
                }
                var colList = string.Join(",", cols);
                using (var insCmd = conn.CreateCommand()) { insCmd.CommandText = $"INSERT INTO {name} ({colList}) SELECT {colList} FROM {oldName};"; insCmd.ExecuteNonQuery(); }

                // Recreate indexes (safe)
                var idxSqls = new List<string>();
                using (var idxCmd = conn.CreateCommand())
                {
                    idxCmd.CommandText = "SELECT name, sql FROM sqlite_master WHERE type='index' AND tbl_name=@old;";
                    idxCmd.Parameters.AddWithValue("@old", oldName);
                    using var idxR = idxCmd.ExecuteReader();
                    while (idxR.Read()) if (!idxR.IsDBNull(1)) idxSqls.Add(idxR.GetString(1).Replace(oldName, name));
                }
                foreach (var idx in idxSqls)
                {
                    var safeIdx = idx;
                    if (safeIdx.TrimStart().StartsWith("CREATE INDEX ", StringComparison.OrdinalIgnoreCase))
                        safeIdx = safeIdx.Replace("CREATE INDEX ", "CREATE INDEX IF NOT EXISTS ", StringComparison.OrdinalIgnoreCase);
                    using var icmd = conn.CreateCommand(); try { icmd.CommandText = safeIdx + ";"; icmd.ExecuteNonQuery(); } catch { }
                }

                using (var dropCmd = conn.CreateCommand()) { dropCmd.CommandText = $"DROP TABLE {oldName};"; dropCmd.ExecuteNonQuery(); }
                using (var rek = conn.CreateCommand()) { rek.CommandText = "PRAGMA foreign_keys = ON;"; rek.ExecuteNonQuery(); }

                tx.Commit();
                Console.WriteLine($"Recreated '{name}' to reference {replacement} successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to fix legacy FK reference in table '{name}': {ex.Message}");
                tx.Rollback();
            }
        }
    }

    static void FixTablesReferencingOld(SqliteConnection conn, string[] tableNames)
    {
        foreach (var name in tableNames)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=@name;";
            cmd.Parameters.AddWithValue("@name", name);
            var sql = cmd.ExecuteScalar() as string;
            if (string.IsNullOrEmpty(sql) || !sql.Contains("_old")) continue;

            var newSql = sql.Replace("_old", "");
            Console.WriteLine($"Fixing {name} (replacing _old)...");
            using var tx = conn.BeginTransaction();
            try
            {
                using var disable = conn.CreateCommand(); disable.CommandText = "PRAGMA foreign_keys = OFF;"; disable.ExecuteNonQuery();
                var oldName = name + "_old";
                using var rename = conn.CreateCommand(); rename.CommandText = $"ALTER TABLE {name} RENAME TO {oldName};"; rename.ExecuteNonQuery();
                using var create = conn.CreateCommand(); create.CommandText = newSql + ";"; create.ExecuteNonQuery();

                // copy columns
                using var colsCmd = conn.CreateCommand(); colsCmd.CommandText = $"PRAGMA table_info({oldName});";
                using var colsR = colsCmd.ExecuteReader(); var cols = new List<string>(); while (colsR.Read()) cols.Add(colsR.GetString(colsR.GetOrdinal("name")));
                var colList = string.Join(",", cols);
                using var ins = conn.CreateCommand(); ins.CommandText = $"INSERT INTO {name} ({colList}) SELECT {colList} FROM {oldName};"; ins.ExecuteNonQuery();

                // recreate indexes
                using var idxCmd = conn.CreateCommand(); idxCmd.CommandText = "SELECT name, sql FROM sqlite_master WHERE type='index' AND tbl_name=@old;"; idxCmd.Parameters.AddWithValue("@old", oldName);
                using var idxR = idxCmd.ExecuteReader(); var idxSqls = new List<string>(); while (idxR.Read()) if (!idxR.IsDBNull(1)) idxSqls.Add(idxR.GetString(1).Replace(oldName, name));
                foreach (var idx in idxSqls)
                {
                    var safeIdx = idx;
                    if (safeIdx.TrimStart().StartsWith("CREATE INDEX ", StringComparison.OrdinalIgnoreCase))
                        safeIdx = safeIdx.Replace("CREATE INDEX ", "CREATE INDEX IF NOT EXISTS ", StringComparison.OrdinalIgnoreCase);
                    using var icmd = conn.CreateCommand(); try { icmd.CommandText = safeIdx + ";"; icmd.ExecuteNonQuery(); } catch { }
                }

                using var drop = conn.CreateCommand(); drop.CommandText = $"DROP TABLE {oldName};"; drop.ExecuteNonQuery();
                using var rek = conn.CreateCommand(); rek.CommandText = "PRAGMA foreign_keys = ON;"; rek.ExecuteNonQuery();
                tx.Commit();
                Console.WriteLine($"Fixed {name}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to fix {name}: {ex.Message}");
                tx.Rollback();
            }
        }
    }

    static void RunFkTests(SqliteConnection conn)
    {
        try
        {
            // Ensure PRAGMA foreign_keys = ON
            using (var p = conn.CreateCommand()) { p.CommandText = "PRAGMA foreign_keys = ON;"; p.ExecuteNonQuery(); }

            Console.WriteLine("Test 1: Insert valid Customer + Bill + BillItem (within transaction, then rollback)");
            using (var tx = conn.BeginTransaction())
            {
                try
                {
                    // Ensure we have an ItemId
                    long itemId;
                    using (var c = conn.CreateCommand())
                    {
                        c.CommandText = "SELECT ItemId FROM Items LIMIT 1;";
                        var r = c.ExecuteScalar();
                        if (r == null)
                        {
                            using var ic = conn.CreateCommand();
                            ic.CommandText = "INSERT INTO Items (Barcode, Description, CostPrice, SalePrice) VALUES ('TEST', 'Test Item', 0, 1);";
                            ic.ExecuteNonQuery();
                            using var l = conn.CreateCommand(); l.CommandText = "SELECT last_insert_rowid();"; itemId = (long)l.ExecuteScalar();
                        }
                        else itemId = Convert.ToInt64(r);
                    }

                    // Create a temporary customer for test
                    long customerId;
                    using (var c = conn.CreateCommand())
                    {
                        c.CommandText = "INSERT INTO Customers (FullName, Phone) VALUES ('FK Test','01234567890');";
                        c.ExecuteNonQuery();
                        using var l = conn.CreateCommand(); l.CommandText = "SELECT last_insert_rowid();"; customerId = (long)l.ExecuteScalar();
                    }

                    long billId;
                    using (var c = conn.CreateCommand())
                    {
                        c.CommandText = "INSERT INTO Bills (CustomerId, BillPaymentMethod) VALUES (@cid,'Cash');";
                        c.Parameters.AddWithValue("@cid", customerId);
                        c.ExecuteNonQuery();
                        using var l = conn.CreateCommand(); l.CommandText = "SELECT last_insert_rowid();"; billId = (long)l.ExecuteScalar();
                    }

                    using (var c = conn.CreateCommand())
                    {
                        c.CommandText = "INSERT INTO BillItems (BillId, ItemId, Quantity, UnitPrice) VALUES (@b,@i,1,1);";
                        c.Parameters.AddWithValue("@b", billId);
                        c.Parameters.AddWithValue("@i", itemId);
                        c.ExecuteNonQuery();
                    }

                    Console.WriteLine("Valid insert succeeded (FKs OK). Rolling back test transaction.");
                    tx.Rollback();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Valid insert failed: {ex.Message}");
                    tx.Rollback();
                }
            }

            Console.WriteLine("Test 2: Attempt insert with invalid CustomerId (should fail with FK error)");
            using (var tx2 = conn.BeginTransaction())
            {
                try
                {
                    using var c = conn.CreateCommand();
                    c.CommandText = "INSERT INTO Bills (CustomerId, BillPaymentMethod) VALUES (99999999,'Cash');";
                    c.ExecuteNonQuery();
                    tx2.Rollback();
                    Console.WriteLine("Unexpected: invalid FK insert did not fail.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Expected failure occurred: {ex.Message}");
                    tx2.Rollback();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FK tests failed: {ex.Message}");
        }
    }
}
