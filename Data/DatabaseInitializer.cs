using System;
using Microsoft.Data.Sqlite;
using GroceryPOS.Helpers;

namespace GroceryPOS.Data
{
    /// <summary>
    /// Creates and maintains the normalized (3NF) database schema for GroceryPOS.
    /// 
    /// Tables (10):
    ///   1. Users          – System users (Admin / Cashier)
    ///   2. Categories      – Product categories (lookup)
    ///   3. Items           – Product catalog (PK = ItemId, Barcode optional + unique)
    ///   4. Customers       – Registered customers with soft-delete
    ///   5. Bills           – Sale headers (IMMUTABLE once saved)
    ///   6. BillItems       – Sale line items (IMMUTABLE, surrogate PK)
    ///   7. bill_payment    – Payment transaction log (Sale / Credit Payment / Refund)
    ///   8. BillReturns     – Return headers (linked to original Bill)
    ///   9. BillReturnItems – Return line items (linked to original BillItems)
    ///  10. InventoryLogs   – Stock movement audit trail
    ///
    /// Normalization:
    ///   - Stock is CALCULATED from SUM(InventoryLogs.QuantityChange), never stored on Items.
    ///   - Bill totals are CALCULATED from BillItems, never stored on Bills.
    ///   - All derived values live only in application models, not in the database.
    ///
    /// Safe to call on every application startup (CREATE IF NOT EXISTS + idempotent migrations).
    /// </summary>
    public static class DatabaseInitializer
    {
        // Schema version — increment when adding migrations
        private const int CurrentSchemaVersion = 14;

        /// <summary>
        /// Ensures all tables, indexes, and seed data exist.
        /// Safe to call on every application startup.
        /// </summary>
        public static void Initialize()
        {
            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    // ── Enable WAL and Foreign Keys ──
                    Execute(conn, "PRAGMA journal_mode = WAL;");
                    Execute(conn, "PRAGMA foreign_keys = ON;");

                    // ── Ensure Base Schema exists before migrations ──
                    EnsureBaseSchema(conn);
                    // ── Hard guard: ensure Bills print columns always exist ──
                    EnsureBillsPrintColumns(conn);
                    // ── Hard guard: make bill_payment canonical even if user_version is stale ──
                    EnsureCanonicalBillPaymentShape(conn);

                    // Fresh databases now start directly at the latest schema.
                    if (GetSchemaVersion(conn) == 0)
                    {
                        SetSchemaVersion(conn, CurrentSchemaVersion);
                    }

                    // ── Run migrations for existing databases ──
                    MigrateIfNeeded(conn);

                    SeedUsers(conn);
                    SeedItems(conn);
                }

                AppLogger.Info("Database initialized successfully. All tables and indexes created.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Database initialization failed", ex);
                throw;
            }
        }

        /// <summary>
        /// Creates the base normalized schema (10 tables) if they do not exist.
        /// This must be called BEFORE MigrateIfNeeded so migrations can depend on table existence.
        /// </summary>
        private static void EnsureBaseSchema(SqliteConnection conn)
        {
            // ────────────────────────────────────────
            //  TABLE 1: Users
            // ────────────────────────────────────────
            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS Users (
                    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username     TEXT    NOT NULL UNIQUE,
                    PasswordHash TEXT    NOT NULL,
                    FullName     TEXT    NOT NULL,
                    Role         TEXT    NOT NULL DEFAULT 'Cashier'
                                 CHECK(Role IN ('Admin', 'Cashier')),
                    IsActive     INTEGER NOT NULL DEFAULT 1,
                    CreatedAt    DATETIME DEFAULT CURRENT_TIMESTAMP
                );
            ");

            // ────────────────────────────────────────
            //  TABLE 2: Categories
            // ────────────────────────────────────────
            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS Categories (
                    CategoryId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name       TEXT    NOT NULL UNIQUE
                );
            ");

            // ────────────────────────────────────────
            //  TABLE 3: Items
            // ────────────────────────────────────────
            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS Items (
                    ItemId            INTEGER PRIMARY KEY AUTOINCREMENT,
                    Barcode           TEXT    UNIQUE,
                    Description       TEXT    NOT NULL,
                    CostPrice         REAL    NOT NULL CHECK(CostPrice >= 0),
                    SalePrice         REAL    NOT NULL CHECK(SalePrice >= 0),
                    CategoryId        INTEGER,
                    MinStockThreshold REAL    NOT NULL DEFAULT 10,
                    CreatedAt         DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (CategoryId) REFERENCES Categories(CategoryId)
                        ON DELETE SET NULL
                );
            ");

            // ────────────────────────────────────────
            //  TABLE 4: Customers
            // ────────────────────────────────────────
            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS Customers (
                    CustomerId INTEGER PRIMARY KEY AUTOINCREMENT,
                    FullName   TEXT    NOT NULL,
                    Phone      TEXT    UNIQUE NOT NULL,
                    SecondaryPhone TEXT,
                    Address    TEXT,
                    Address2   TEXT,
                    Address3   TEXT,
                    IsActive   INTEGER NOT NULL DEFAULT 1,
                    CreatedAt  DATETIME DEFAULT CURRENT_TIMESTAMP
                );
            ");

            // ────────────────────────────────────────
            //  TABLE 11: Accounts (Payment Accounts)
            // ────────────────────────────────────────
            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS Accounts (
                    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    AccountTitle TEXT    NOT NULL,
                    AccountType  TEXT    NOT NULL,
                    BankName     TEXT,
                    BranchName   TEXT,
                    AccountNumber TEXT,
                    IsActive     INTEGER NOT NULL DEFAULT 1
                );
            ");

            // ────────────────────────────────────────
            //  TABLE 5: Bills
            // ────────────────────────────────────────
            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS Bills (
                    BillId         INTEGER PRIMARY KEY AUTOINCREMENT,
                    CustomerId     INTEGER,
                    UserId         INTEGER,
                    TaxAmount      REAL    DEFAULT 0,
                    DiscountAmount REAL    DEFAULT 0,
                    Status         TEXT    DEFAULT 'Completed'
                                   CHECK(Status IN ('Completed', 'Cancelled')),
                    BillPaymentMethod TEXT NOT NULL DEFAULT 'Cash',
                    IsPrinted      INTEGER DEFAULT 0,
                    PrintedAt      DATETIME,
                    CreatedAt      DATETIME DEFAULT CURRENT_TIMESTAMP,
                    AccountId      INTEGER,
                    FOREIGN KEY (CustomerId) REFERENCES Customers(CustomerId)
                        ON DELETE RESTRICT,
                    FOREIGN KEY (UserId) REFERENCES Users(Id)
                        ON DELETE SET NULL,
                    FOREIGN KEY (AccountId) REFERENCES Accounts(Id)
                        ON DELETE SET NULL
                );
            ");

            // ────────────────────────────────────────
            //  TABLE 6: BillItems
            // ────────────────────────────────────────
            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS BillItems (
                    BillItemId     INTEGER PRIMARY KEY AUTOINCREMENT,
                    BillId         INTEGER NOT NULL,
                    ItemId         INTEGER NOT NULL,
                    Quantity       REAL    NOT NULL CHECK(Quantity > 0),
                    UnitPrice      REAL    NOT NULL CHECK(UnitPrice >= 0),
                    DiscountAmount REAL    DEFAULT 0,
                    FOREIGN KEY (BillId) REFERENCES Bills(BillId)
                        ON DELETE CASCADE,
                    FOREIGN KEY (ItemId) REFERENCES Items(ItemId)
                        ON DELETE RESTRICT
                );
            ");

            // ────────────────────────────────────────
            //  TABLE 7: bill_payment
            // ────────────────────────────────────────
            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS bill_payment (
                    PaymentId       INTEGER PRIMARY KEY AUTOINCREMENT,
                    BillId          INTEGER NOT NULL,
                    Amount          REAL    NOT NULL CHECK(Amount >= 0),
                    Type            TEXT    NOT NULL CHECK(Type IN ('payment', 'refund')),
                    CreatedAt       DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (BillId) REFERENCES Bills(BillId)
                        ON DELETE CASCADE
                );
            ");

            // ────────────────────────────────────────
            //  TABLE 8: BillReturns
            // ────────────────────────────────────────
            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS BillReturns (
                    ReturnId    INTEGER PRIMARY KEY AUTOINCREMENT,
                    BillId      INTEGER NOT NULL,
                    UserId      INTEGER,
                    RefundAmount REAL   NOT NULL,
                    ReturnedAt  DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (BillId) REFERENCES Bills(BillId)
                        ON DELETE CASCADE,
                    FOREIGN KEY (UserId) REFERENCES Users(Id)
                        ON DELETE SET NULL
                );
            ");

            // ────────────────────────────────────────
            //  TABLE 9: BillReturnItems
            // ────────────────────────────────────────
            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS BillReturnItems (
                    ReturnItemId INTEGER PRIMARY KEY AUTOINCREMENT,
                    ReturnId     INTEGER NOT NULL,
                    BillItemId   INTEGER NOT NULL,
                    Quantity     REAL    NOT NULL CHECK(Quantity > 0),
                    UnitPrice    REAL    NOT NULL CHECK(UnitPrice >= 0),
                    FOREIGN KEY (ReturnId) REFERENCES BillReturns(ReturnId)
                        ON DELETE CASCADE,
                    FOREIGN KEY (BillItemId) REFERENCES BillItems(BillItemId)
                        ON DELETE RESTRICT
                );
            ");

            // ────────────────────────────────────────
            //  TABLE 10: InventoryLogs
            // ────────────────────────────────────────
            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS InventoryLogs (
                    LogId          INTEGER PRIMARY KEY AUTOINCREMENT,
                    ItemId         INTEGER NOT NULL,
                    QuantityChange REAL    NOT NULL,
                    ChangeType     TEXT    NOT NULL
                                   CHECK(ChangeType IN ('Sale', 'Return', 'Purchase', 'Adjustment')),
                    ReferenceId    INTEGER,
                    ReferenceType  TEXT    CHECK(ReferenceType IN ('Bill', 'Return', 'Supply') OR ReferenceType IS NULL),
                    LogDate        DATETIME DEFAULT CURRENT_TIMESTAMP,
                    ImagePath      TEXT,
                    FOREIGN KEY (ItemId) REFERENCES Items(ItemId)
                        ON DELETE CASCADE
                );
            ");

            // ────────────────────────────────────────
            //  TABLE 12: CustomerLedger (Audit Journal)
            // ────────────────────────────────────────
            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS CustomerLedger (
                    LedgerId       INTEGER PRIMARY KEY AUTOINCREMENT,
                    CustomerId     INTEGER NOT NULL,
                    EntryDate      DATETIME DEFAULT CURRENT_TIMESTAMP,
                    Type           TEXT    NOT NULL CHECK(Type IN ('SALE', 'PAYMENT', 'RETURN', 'ADJUSTMENT')),
                    ReferenceId    TEXT,
                    Description    TEXT,
                    Debit          REAL    DEFAULT 0,
                    Credit         REAL    DEFAULT 0,
                    RunningBalance REAL    NOT NULL,
                    FOREIGN KEY (CustomerId) REFERENCES Customers(CustomerId)
                        ON DELETE CASCADE
                );
            ");

            // ────────────────────────────────────────
            //  INDEXES
            // ────────────────────────────────────────
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Items_Barcode      ON Items(Barcode) WHERE Barcode IS NOT NULL;");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Items_Category     ON Items(CategoryId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Bills_Customer     ON Bills(CustomerId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Bills_CreatedAt    ON Bills(CreatedAt);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Bills_Status       ON Bills(Status);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_BillItems_BillId   ON BillItems(BillId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_BillItems_ItemId   ON BillItems(ItemId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_BillId ON bill_payment(BillId);");
            CreateIndexIfColumnExists(conn, "IX_bill_payment_CreatedAt", "bill_payment", "CreatedAt", "CreatedAt");
            CreateIndexIfColumnExists(conn, "IX_bill_payment_PaidAt", "bill_payment", "PaidAt", "PaidAt");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Returns_BillId     ON BillReturns(BillId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_ReturnItems_RetId  ON BillReturnItems(ReturnId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_ReturnItems_BiId   ON BillReturnItems(BillItemId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Inventory_ItemId   ON InventoryLogs(ItemId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Inventory_LogDate  ON InventoryLogs(LogDate);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Customers_Phone    ON Customers(Phone);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Ledger_Customer    ON CustomerLedger(CustomerId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Ledger_Date        ON CustomerLedger(EntryDate);");
        }

        // ────────────────────────────────────────────
        //  Seed default users
        // ────────────────────────────────────────────
        private static void SeedUsers(SqliteConnection conn)
        {
            // Only seed if no users exist
            using var countCmd = conn.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM Users;";
            var count = Convert.ToInt64(countCmd.ExecuteScalar());
            if (count > 0) return;

            var users = new[]
            {
                ("admin",   "admin123",   "System Administrator", "Admin"),
                ("cashier", "cashier123", "Default Cashier",      "Cashier")
            };

            foreach (var (username, password, fullName, role) in users)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Users (Username, PasswordHash, FullName, Role, IsActive)
                    VALUES (@username, @hash, @fullName, @role, 1);
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(password));
                cmd.Parameters.AddWithValue("@fullName", fullName);
                cmd.Parameters.AddWithValue("@role", role);
                cmd.ExecuteNonQuery();
            }

            AppLogger.Info("Default users seeded (admin, cashier).");
        }

        // ────────────────────────────────────────────
        //  Seed sample items
        // ────────────────────────────────────────────
        private static void SeedItems(SqliteConnection conn)
        {
            // 1. Seed Categories first
            using (var countCmd = conn.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM Categories;";
                if (Convert.ToInt64(countCmd.ExecuteScalar()) == 0)
                {
                    var categories = new[] { "Dairy", "Beverages", "Snacks", "Grocery", "Bakery", "Cleaning", "Personal Care", "Frozen Food", "Pantry & Spices", "Household", "Baby Care", "Stationery", "Other" };
                    foreach (var cat in categories)
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "INSERT INTO Categories (Name) VALUES (@name);";
                        cmd.Parameters.AddWithValue("@name", cat);
                        cmd.ExecuteNonQuery();
                    }
                    AppLogger.Info("Categories seeded.");
                }
            }

            // 2. Only seed if no items exist.
            using (var countCmd = conn.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM Items;";
                var count = Convert.ToInt64(countCmd.ExecuteScalar());
                if (count > 0) return;
            }
            
            var items = new[]
            {
                // Dairy
                ("8961000100018", "Olper's Milk 1L",      240.0, 270.0, "Dairy"),
                ("8961000100025", "Nestle Milk Pack 1L",   230.0, 260.0, "Dairy"),
                ("8961000100032", "Adam's Cheese 200g",    450.0, 520.0, "Dairy"),
                
                // Beverages
                ("5449001000996", "Coca Cola 1.5L",        130.0, 160.0, "Beverages"),
                ("0012000001536", "Pepsi 1.5L",            125.0, 155.0, "Beverages"),
                ("8961014101111", "Nestle Pure Life 1.5L",  60.0,  80.0,  "Beverages"),
                ("8961014101112", "Red Bull 250ml",        250.0, 320.0, "Beverages"),

                // Snacks
                ("8964001510017", "Lays Classic Chips",     50.0,  70.0, "Snacks"),
                ("8964001510018", "Kurkure Chutney Chaska", 40.0,  60.0, "Snacks"),
                ("8964001510019", "Cheetos Cheese",         40.0,  60.0, "Snacks"),

                // Grocery
                ("8964001810028", "Supreme Atta 10kg",     850.0, 950.0,  "Grocery"),
                ("8961000200015", "Dalda Cooking Oil 5L", 2200.0, 2550.0, "Grocery"),
                ("8964001311014", "Tapal Danedar 950g",   1100.0, 1250.0, "Grocery"),
                ("8961000300014", "National Salt 800g",    40.0,  55.0,   "Grocery"),
                ("8961000300015", "Mehran Basmati Rice 5kg", 1800.0, 2100.0, "Grocery"),

                // Bakery
                ("8961000400011", "Dawn Bread Large",      140.0, 170.0, "Bakery"),
                ("8961000400012", "Candi Biscuits Half-Roll", 30.0, 45.0, "Bakery"),
                ("8961000400013", "Orio Biscuits Pk 12",   180.0, 220.0, "Bakery"),

                // Cleaning
                ("8961000500011", "Surf Excel 1kg",        450.0, 520.0, "Cleaning"),
                ("8961000500012", "Lemon Max Liquid 500ml", 180.0, 220.0, "Cleaning"),
                ("8961000500013", "Harpic Blue 500ml",     280.0, 340.0, "Cleaning"),

                // Personal Care
                ("8961000600011", "Lux Soap Soap 140g",    120.0, 150.0, "Personal Care"),
                ("8961000600012", "Sunsilk Shampoo 180ml", 350.0, 420.0, "Personal Care"),
                ("8961000600013", "Colgate Toothpaste 100g", 180.0, 240.0, "Personal Care"),

                // Frozen Food
                ("8961000700011", "K&Ns Nuggets 1kg",      1200.0, 1450.0, "Frozen Food"),
                ("8961000700012", "Menu Shami Kabab 12pk", 550.0, 680.0, "Frozen Food"),

                // Other
                ("1000000000001", "Red Apples 1kg",        220.0, 280.0, "Other"),
                ("1000000000002", "Bananas Dozen",           140.0, 180.0, "Other"),
                ("1000000000003", "Tomatoes 1kg",            110.0, 150.0, "Other"),
                ("2000000000001", "Chicken Whole 1kg",     450.0, 550.0, "Other"),
                ("2000000000002", "Mutton Mix 1kg",        1600.0, 1950.0, "Other"),

                // Pantry & Spices
                ("3000000000001", "National Chili Powder 200g", 180.0, 230.0, "Pantry & Spices"),
                ("3000000000002", "National Turmeric 100g",      120.0, 160.0, "Pantry & Spices"),
                ("3000000000003", "Shan Ginger Garlic Paste",    320.0, 380.0, "Pantry & Spices"),

                // Household
                ("4000000000001", "Scotch-Brite Sponge 3pk",     140.0, 180.0, "Household"),
                ("4000000000002", "Large Garbage Bags 10pk",     220.0, 280.0, "Household"),

                // Baby Care
                ("5000000000001", "Pampers Baby Wipes 64ct",     450.0, 550.0, "Baby Care"),
                ("5000000000002", "Cerelac Wheat 250g",          380.0, 480.0, "Baby Care"),

                // Stationery
                ("6000000000001", "A4 Paper Ream 500 Sheets",    1400.0, 1650.0, "Stationery"),
                ("6000000000002", "Dollar Ballpoint Blue 10pk",  250.0, 320.0, "Stationery"),
            };

            int addedCount = 0;
            foreach (var (barcode, desc, cost, sale, categoryName) in items)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Items (Barcode, Description, CostPrice, SalePrice, CategoryId)
                    SELECT @barcode, @desc, @cost, @sale, c.CategoryId
                    FROM Categories c WHERE c.Name = @catName;
                ";
                cmd.Parameters.AddWithValue("@barcode", barcode);
                cmd.Parameters.AddWithValue("@desc", desc);
                cmd.Parameters.AddWithValue("@cost", cost);
                cmd.Parameters.AddWithValue("@sale", sale);
                cmd.Parameters.AddWithValue("@catName", categoryName);
                
                int affected = cmd.ExecuteNonQuery();
                if (affected > 0)
                {
                    addedCount++;
                    // Also seed initial inventory log
                    long lastId = 0;
                    using (var idCmd = conn.CreateCommand())
                    {
                        idCmd.CommandText = "SELECT last_insert_rowid();";
                        lastId = (long)idCmd.ExecuteScalar()!;
                    }
                    
                    using (var logCmd = conn.CreateCommand())
                    {
                        logCmd.CommandText = "INSERT INTO InventoryLogs (ItemId, QuantityChange, ChangeType) VALUES (@itemId, 100, 'Purchase');";
                        logCmd.Parameters.AddWithValue("@itemId", lastId);
                        logCmd.ExecuteNonQuery();
                    }
                }
            }

            if (addedCount > 0)
                AppLogger.Info($"SeedItems: Successfully added {addedCount} new default items and inventory logs.");
        }

        // ════════════════════════════════════════════
        //  MIGRATION FRAMEWORK
        // ════════════════════════════════════════════

        private static int GetSchemaVersion(SqliteConnection conn)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA user_version;";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch { return 0; }
        }

        private static void SetSchemaVersion(SqliteConnection conn, int version)
        {
            Execute(conn, $"PRAGMA user_version = {version};");
        }

        /// <summary>
        /// Runs all pending migrations in order.
        /// Each migration is idempotent and safe to re-run.
        /// </summary>
        private static void MigrateIfNeeded(SqliteConnection conn)
        {
            int currentVersion = GetSchemaVersion(conn);
            if (currentVersion >= CurrentSchemaVersion) return;

            AppLogger.Info($"Database migration needed: v{currentVersion} → v{CurrentSchemaVersion}");

            // Migration v0 → v1: Migrate from legacy schema (old table names)
            if (currentVersion < 1)
            {
                MigrateFromLegacySchema(conn);
                SetSchemaVersion(conn, 1);
            }

            // Migration v1 → v2: Reconcile schema mismatches
            if (currentVersion < 2)
            {
                MigrateSchemaV2(conn);
                SetSchemaVersion(conn, 2);
            }

            // Migration v2 → v3: Add Address2, Address3 columns to Customers
            if (currentVersion < 3)
            {
                AddColumnIfNotExists(conn, "Customers", "Address2", "TEXT");
                AddColumnIfNotExists(conn, "Customers", "Address3", "TEXT");
                SetSchemaVersion(conn, 3);
                AppLogger.Info("Migration v3: Added Address2, Address3 to Customers.");
            }

            // Migration v3 → v4: Add ImagePath column to InventoryLogs
            if (currentVersion < 4)
            {
                AddColumnIfNotExists(conn, "InventoryLogs", "ImagePath", "TEXT");
                SetSchemaVersion(conn, 4);
                AppLogger.Info("Migration v4: Added ImagePath to InventoryLogs.");
            }

            // Migration v4 → v5: Add 'Return Offset' to payment transaction types.
            if (currentVersion < 5)
            {
                string paymentSourceV5 = TableExists(conn, "bill_payment") ? "bill_payment" : "Payments";
                Execute(conn, $@"
                    CREATE TABLE IF NOT EXISTS bill_payment_new (
                        PaymentId       INTEGER PRIMARY KEY AUTOINCREMENT,
                        BillId          INTEGER NOT NULL,
                        Amount          REAL    NOT NULL,
                        PaymentMethod   TEXT    NOT NULL DEFAULT 'Cash'
                                        CHECK(PaymentMethod IN ('Cash', 'Card', 'Credit', 'Online')),
                        TransactionType TEXT    NOT NULL DEFAULT 'Sale'
                                        CHECK(TransactionType IN ('Sale', 'Credit Payment', 'Refund', 'Return Offset')),
                        Note            TEXT,
                        PaidAt          DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (BillId) REFERENCES Bills(BillId)
                            ON DELETE CASCADE
                    );
                    INSERT INTO bill_payment_new SELECT * FROM {paymentSourceV5};
                    DROP TABLE {paymentSourceV5};
                    ALTER TABLE bill_payment_new RENAME TO bill_payment;
                ");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_BillId ON bill_payment(BillId);");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_PaidAt ON bill_payment(PaidAt);");
                // Reclassify any existing return-offset payments that were saved as 'Credit Payment'
                Execute(conn, @"
                    UPDATE bill_payment SET TransactionType = 'Return Offset'
                    WHERE TransactionType = 'Credit Payment'
                      AND BillId IN (SELECT DISTINCT BillId FROM BillReturns);
                ");
                SetSchemaVersion(conn, 5);
                AppLogger.Info("Migration v5: Added 'Return Offset' to bill_payment.TransactionType.");
            }

            // Migration v5 → v6: Add BillPaymentMethod column to Bills + 'Online' to payment method constraint.
            if (currentVersion < 6)
            {
                AddColumnIfNotExists(conn, "Bills", "BillPaymentMethod", "TEXT NOT NULL DEFAULT 'Cash'");
                // Backfill BillPaymentMethod from the first Payment record for each bill
                Execute(conn, @"
                    UPDATE Bills SET BillPaymentMethod = COALESCE(
                        (SELECT p.PaymentMethod FROM bill_payment p 
                         WHERE p.BillId = Bills.BillId AND p.TransactionType = 'Sale'
                         ORDER BY p.PaidAt ASC LIMIT 1), 'Cash');
                ");
                // Recreate payment table to add 'Online' to PaymentMethod CHECK
                Execute(conn, @"
                    CREATE TABLE IF NOT EXISTS bill_payment_v6 (
                        PaymentId       INTEGER PRIMARY KEY AUTOINCREMENT,
                        BillId          INTEGER NOT NULL,
                        Amount          REAL    NOT NULL,
                        PaymentMethod   TEXT    NOT NULL DEFAULT 'Cash'
                                        CHECK(PaymentMethod IN ('Cash', 'Card', 'Credit', 'Online')),
                        TransactionType TEXT    NOT NULL DEFAULT 'Sale'
                                        CHECK(TransactionType IN ('Sale', 'Credit Payment', 'Refund', 'Return Offset')),
                        Note            TEXT,
                        PaidAt          DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (BillId) REFERENCES Bills(BillId)
                            ON DELETE CASCADE
                    );
                    INSERT INTO bill_payment_v6 SELECT * FROM bill_payment;
                    DROP TABLE bill_payment;
                    ALTER TABLE bill_payment_v6 RENAME TO bill_payment;
                ");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_BillId ON bill_payment(BillId);");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_PaidAt ON bill_payment(PaidAt);");
                SetSchemaVersion(conn, 6);
                AppLogger.Info("Migration v6: Added BillPaymentMethod to Bills, 'Online' to bill_payment.PaymentMethod.");
            }

            // Migration v6 → v7: Add OnlinePaymentMethod column to Bills
            if (currentVersion < 7)
            {
                AddColumnIfNotExists(conn, "Bills", "OnlinePaymentMethod", "TEXT");
                SetSchemaVersion(conn, 7);
                AppLogger.Info("Migration v7: Added OnlinePaymentMethod to Bills (nullable, for Easypaisa/JazzCash/Bank Transfer).");
            }

            // Migration v7 → v8: Add Accounts table + AccountId to Bills
            if (currentVersion < 8)
            {
                Execute(conn, @"
                    CREATE TABLE IF NOT EXISTS Accounts (
                        Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                        AccountTitle TEXT    NOT NULL,
                        AccountType  TEXT    NOT NULL,
                        BankName     TEXT,
                        BranchName   TEXT,
                        AccountNumber TEXT,
                        IsActive     INTEGER NOT NULL DEFAULT 1
                    );
                ");
                AddColumnIfNotExists(conn, "Bills", "AccountId", "INTEGER");

                SetSchemaVersion(conn, 8);
                AppLogger.Info("Migration v8: Created Accounts table and added AccountId to Bills.");
            }

            // Migration v8 → v9: Populate detailed accounts (Bank branch names, and additional wallets)
            if (currentVersion < 9)
            {
                // Detailed seed data for Accounts
                var defaultAccounts = new[]
                {
                    ("Meezan - Main",   "Bank",      "Meezan Bank Ltd", "DHA Phase 4, Lahore", "0123-456789-01"),
                    ("Bank Alfalah",    "Bank",      "Bank Alfalah",    "Gulberg III Branch",  "5566-778899-02"),
                    ("Shop Easypaisa",  "Easypaisa", "Telenor Bank",    "Mobile Wallet",       "03001234567"),
                    ("Shop JazzCash",   "JazzCash",  "Mobilink Bank",   "Mobile Wallet",       "03217654321"),
                    ("HBL Business",    "Bank",      "Habib Bank Ltd",  "Main Market",         "1122-334455-03"),
                    ("SadaPay",         "Online",    "SadaPay",         "Digital",             "03119998881")
                };

                foreach (var (title, type, bank, branch, number) in defaultAccounts)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO Accounts (AccountTitle, AccountType, BankName, BranchName, AccountNumber)
                        SELECT @title, @type, @bank, @branch, @num
                        WHERE NOT EXISTS (SELECT 1 FROM Accounts WHERE AccountTitle = @title);
                    ";
                    cmd.Parameters.AddWithValue("@title", title);
                    cmd.Parameters.AddWithValue("@type", type);
                    cmd.Parameters.AddWithValue("@bank", bank);
                    cmd.Parameters.AddWithValue("@branch", branch);
                    cmd.Parameters.AddWithValue("@num", number);
                    cmd.ExecuteNonQuery();
                }

                SetSchemaVersion(conn, 9);
                AppLogger.Info("Migration v9: Seeded detailed account entries including branch names.");
            }

            // Migration v10 → v11: Create CustomerLedger table
            if (currentVersion < 11)
            {
                Execute(conn, @"
                    CREATE TABLE IF NOT EXISTS CustomerLedger (
                        LedgerId       INTEGER PRIMARY KEY AUTOINCREMENT,
                        CustomerId     INTEGER NOT NULL,
                        EntryDate      DATETIME DEFAULT CURRENT_TIMESTAMP,
                        Type           TEXT    NOT NULL CHECK(Type IN ('SALE', 'PAYMENT', 'RETURN', 'ADJUSTMENT')),
                        ReferenceId    TEXT,
                        Description    TEXT,
                        Debit          REAL    DEFAULT 0,
                        Credit         REAL    DEFAULT 0,
                        RunningBalance REAL    NOT NULL,
                        FOREIGN KEY (CustomerId) REFERENCES Customers(CustomerId)
                            ON DELETE CASCADE
                    );
                ");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Ledger_Customer ON CustomerLedger(CustomerId);");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Ledger_Date     ON CustomerLedger(EntryDate);");

                // --- BACKFILL HISTORICAL DATA ---
                AppLogger.Info("Migration v11: Backfilling CustomerLedger from existing history...");
                
                // 1. Backfill SALES (from Bills)
                Execute(conn, @"
                    INSERT INTO CustomerLedger (CustomerId, Type, ReferenceId, Description, Debit, Credit, RunningBalance, EntryDate)
                    SELECT CustomerId, 'SALE', printf('%05d', BillId), 'Historical Invoice #' || printf('%05d', BillId), 
                           (SELECT SUM(Quantity * UnitPrice) FROM BillItems WHERE BillId = Bills.BillId), 0, 0, CreatedAt
                    FROM Bills 
                    WHERE CustomerId IS NOT NULL AND Status != 'Cancelled';");

                // 2. Backfill PAYMENTS (from bill_payment)
                Execute(conn, @"
                    INSERT INTO CustomerLedger (CustomerId, Type, ReferenceId, Description, Debit, Credit, RunningBalance, EntryDate)
                    SELECT b.CustomerId, 'PAYMENT', printf('%05d', b.BillId), p.TransactionType || ' (Ref: #' || printf('%05d', b.BillId) || ')', 
                           0, p.Amount, 0, p.PaidAt
                    FROM bill_payment p
                    JOIN Bills b ON p.BillId = b.BillId
                    WHERE b.CustomerId IS NOT NULL AND p.TransactionType != 'Refund';");

                // 3. Backfill RETURNS (from BillReturns)
                Execute(conn, @"
                    INSERT INTO CustomerLedger (CustomerId, Type, ReferenceId, Description, Debit, Credit, RunningBalance, EntryDate)
                    SELECT b.CustomerId, 'RETURN', printf('%05d', r.ReturnId), 'Items Returned (Ref: #' || printf('%05d', b.BillId) || ')', 
                           0, (SELECT SUM(Quantity * UnitPrice) FROM BillReturnItems WHERE ReturnId = r.ReturnId), 0, r.ReturnedAt
                    FROM BillReturns r
                    JOIN Bills b ON r.BillId = b.BillId
                    WHERE b.CustomerId IS NOT NULL;");

                // 4. Recalculate Running Balances (Simple approach: we'll let the UI handle on-the-fly if needed, but better to fix here)
                // Actually, doing a full recursive update in SQLite is complex. 
                // Given the Repository calculates it correctly for new entries, I'll provide a 'Recalculate' logic in the Repository if needed.
                // For now, these entries will have '0' balance which the ViewModel currently calculates as sums.

                SetSchemaVersion(conn, 11);
                AppLogger.Info("Migration v11: Created CustomerLedger and backfilled history.");
            }

            // Migration v11 → v12: Add InitialPayment column to Bills
            if (currentVersion < 12)
            {
                string paymentsSource = TableExists(conn, "bill_payment") ? "bill_payment" : "Payments";
                AddColumnIfNotExists(conn, "Bills", "InitialPayment", "REAL DEFAULT 0");
                // Ensure no NULLs
                Execute(conn, "UPDATE Bills SET InitialPayment = 0 WHERE InitialPayment IS NULL;");
                // Backfill InitialPayment from the first Sale-type Payment for each existing bill
                Execute(conn, $@"
                    UPDATE Bills SET InitialPayment = COALESCE(
                        (SELECT p.Amount FROM {paymentsSource} p 
                         WHERE p.BillId = Bills.BillId AND p.TransactionType = 'Sale'
                         ORDER BY p.PaidAt ASC LIMIT 1), 0
                    );
                ");
                SetSchemaVersion(conn, 12);
                AppLogger.Info("Migration v12: Added InitialPayment column to Bills and backfilled from existing Sale payments.");
            }

            // Migration v12 → v13: Canonicalize payment table to bill_payment and remove legacy Payments.
            if (currentVersion < 13)
            {
                if (TableExists(conn, "Payments") && !TableExists(conn, "bill_payment"))
                {
                    Execute(conn, "ALTER TABLE Payments RENAME TO bill_payment;");
                }
                else if (TableExists(conn, "Payments") && TableExists(conn, "bill_payment"))
                {
                    // Merge only rows not already present by PaymentId to avoid duplicate data.
                    string legacyPaymentsTable = "Payments";
                    Execute(conn, $@"
                        INSERT INTO bill_payment (PaymentId, BillId, Amount, PaymentMethod, TransactionType, Note, PaidAt)
                        SELECT p.PaymentId, p.BillId, p.Amount, p.PaymentMethod, p.TransactionType, p.Note, p.PaidAt
                        FROM {legacyPaymentsTable} p
                        LEFT JOIN bill_payment bp ON bp.PaymentId = p.PaymentId
                        WHERE bp.PaymentId IS NULL;
                    ");
                    Execute(conn, "DROP TABLE Payments;");
                }

                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_BillId ON bill_payment(BillId);");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_PaidAt ON bill_payment(PaidAt);");

                SetSchemaVersion(conn, 13);
                AppLogger.Info("Migration v13: Canonicalized payment table to bill_payment and removed legacy Payments.");
            }

            // Migration v13 → v14: Enforce canonical bill_payment shape (PaymentId, BillId, Amount, Type, CreatedAt).
            if (currentVersion < 14)
            {
                Execute(conn, @"
                    CREATE TABLE IF NOT EXISTS bill_payment_v14 (
                        PaymentId INTEGER PRIMARY KEY AUTOINCREMENT,
                        BillId    INTEGER NOT NULL,
                        Amount    REAL    NOT NULL CHECK(Amount >= 0),
                        Type      TEXT    NOT NULL CHECK(Type IN ('payment', 'refund')),
                        CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (BillId) REFERENCES Bills(BillId) ON DELETE CASCADE
                    );
                ");

                if (TableExists(conn, "bill_payment"))
                {
                    bool hasType = ColumnExists(conn, "bill_payment", "Type");
                    bool hasCreatedAt = ColumnExists(conn, "bill_payment", "CreatedAt");
                    bool hasTransactionType = ColumnExists(conn, "bill_payment", "TransactionType");
                    bool hasPaidAt = ColumnExists(conn, "bill_payment", "PaidAt");

                    string typeExpr = hasType
                        ? "LOWER(Type)"
                        : hasTransactionType
                            ? "CASE WHEN TransactionType = 'Refund' THEN 'refund' ELSE 'payment' END"
                            : "'payment'";

                    string createdAtExpr = hasCreatedAt
                        ? "CreatedAt"
                        : hasPaidAt
                            ? "PaidAt"
                            : "CURRENT_TIMESTAMP";

                    Execute(conn, $@"
                        INSERT INTO bill_payment_v14 (PaymentId, BillId, Amount, Type, CreatedAt)
                        SELECT PaymentId, BillId, ABS(Amount),
                               CASE WHEN {typeExpr} = 'refund' THEN 'refund' ELSE 'payment' END,
                               {createdAtExpr}
                        FROM bill_payment;
                    ");
                    Execute(conn, "DROP TABLE bill_payment;");
                }

                Execute(conn, "ALTER TABLE bill_payment_v14 RENAME TO bill_payment;");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_BillId ON bill_payment(BillId);");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_CreatedAt ON bill_payment(CreatedAt);");

                SetSchemaVersion(conn, 14);
                AppLogger.Info("Migration v14: Standardized bill_payment to canonical accounting schema.");
            }

            // --- Real-time Date Fix: Localize UTC timestamps stored previously ---
            // This 'repairs' entries created after the user's local 7 PM but stored as UTC (previous day date).
            // We shift everything created in the last 24 hours by +5 hours if it looks like UTC.
            Execute(conn, @"
                UPDATE Bills SET CreatedAt = datetime(CreatedAt, '+5 hours') 
                WHERE CreatedAt > datetime('now', '-24 hours') AND CreatedAt LIKE '%Z' OR CreatedAt NOT LIKE '%:%:%';
                
                UPDATE bill_payment SET CreatedAt = datetime(CreatedAt, '+5 hours') 
                WHERE CreatedAt > datetime('now', '-24 hours');
                
                UPDATE InventoryLogs SET LogDate = datetime(LogDate, '+5 hours') 
                WHERE LogDate > datetime('now', '-24 hours');
                
                UPDATE BillReturns SET ReturnedAt = datetime(ReturnedAt, '+5 hours') 
                WHERE ReturnedAt > datetime('now', '-24 hours');
            ");

            AppLogger.Info($"Database migrated successfully to v{CurrentSchemaVersion}.");
        }

        /// <summary>
        /// Migration v0 → v1: Handles transition from legacy table names
        /// (Bill, BillDescription, BILL_RETURNS, Item, stock) to the new normalized schema.
        /// </summary>
        private static void MigrateFromLegacySchema(SqliteConnection conn)
        {
            // Check if legacy tables exist
            bool hasLegacyBill = TableExists(conn, "Bill");
            bool hasLegacyItem = TableExists(conn, "Item");
            bool hasLegacyBillDesc = TableExists(conn, "BillDescription");
            bool hasLegacyReturns = TableExists(conn, "BILL_RETURNS");
            bool hasLegacyStock = TableExists(conn, "stock");

            if (!hasLegacyBill && !hasLegacyItem) return; // Not a legacy database

            AppLogger.Info("Migrating from legacy schema (Bill/Item/BillDescription/BILL_RETURNS)...");
            Execute(conn, "PRAGMA foreign_keys = OFF;");

            using var txn = conn.BeginTransaction();
            try
            {
                // Migrate Item → Items (if Items is empty)
                if (hasLegacyItem && IsTableEmpty(conn, "Items"))
                {
                    Execute(conn, @"
                        CREATE TABLE IF NOT EXISTS Items (
                            ItemId INTEGER PRIMARY KEY AUTOINCREMENT,
                            Barcode TEXT UNIQUE,
                            Description TEXT NOT NULL,
                            CostPrice REAL NOT NULL DEFAULT 0,
                            SalePrice REAL NOT NULL DEFAULT 0,
                            CategoryId INTEGER,
                            MinStockThreshold REAL NOT NULL DEFAULT 10,
                            CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                        );
                    ");
                    Execute(conn, @"
                        INSERT OR IGNORE INTO Items (Barcode, Description, SalePrice, CostPrice)
                        SELECT itemId, itemDescription, COALESCE(salePrice, 0), COALESCE(costPrice, 0) FROM Item;
                    ");
                }

                // Migrate Bill → Bills
                if (hasLegacyBill && IsTableEmpty(conn, "Bills"))
                {
                    Execute(conn, @"
                        CREATE TABLE IF NOT EXISTS Bills (
                            BillId INTEGER PRIMARY KEY AUTOINCREMENT,
                            CustomerId INTEGER,
                            UserId INTEGER,
                            TaxAmount REAL DEFAULT 0,
                            DiscountAmount REAL DEFAULT 0,
                            Status TEXT DEFAULT 'Completed',
                            IsPrinted INTEGER DEFAULT 0,
                            PrintedAt DATETIME,
                            CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                        );
                    ");
                    Execute(conn, @"
                        INSERT OR IGNORE INTO Bills (BillId, TaxAmount, DiscountAmount, CreatedAt)
                        SELECT bill_id, 0, 0, bill_date FROM Bill;
                    ");
                }

                // Migrate BillDescription → BillItems
                if (hasLegacyBillDesc && IsTableEmpty(conn, "BillItems"))
                {
                    Execute(conn, @"
                        CREATE TABLE IF NOT EXISTS BillItems (
                            BillItemId INTEGER PRIMARY KEY AUTOINCREMENT,
                            BillId INTEGER NOT NULL,
                            ItemId INTEGER NOT NULL,
                            Quantity REAL NOT NULL DEFAULT 1,
                            UnitPrice REAL NOT NULL DEFAULT 0,
                            DiscountAmount REAL DEFAULT 0
                        );
                    ");
                    Execute(conn, @"
                        INSERT OR IGNORE INTO BillItems (BillId, ItemId, Quantity, UnitPrice)
                        SELECT bd.Bill_id, COALESCE(i.ItemId, 0), bd.Quantity, bd.UnitPrice
                        FROM BillDescription bd
                        LEFT JOIN Items i ON bd.ItemId = i.Barcode;
                    ");
                }

                // Migrate BILL_RETURNS → BillReturns
                if (hasLegacyReturns && IsTableEmpty(conn, "BillReturns"))
                {
                    Execute(conn, @"
                        CREATE TABLE IF NOT EXISTS BillReturns (
                            ReturnId INTEGER PRIMARY KEY AUTOINCREMENT,
                            BillId INTEGER NOT NULL,
                            UserId INTEGER,
                            RefundAmount REAL NOT NULL DEFAULT 0,
                            ReturnedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                        );
                    ");
                    Execute(conn, @"
                        CREATE TABLE IF NOT EXISTS BillReturnItems (
                            ReturnItemId INTEGER PRIMARY KEY AUTOINCREMENT,
                            ReturnId INTEGER NOT NULL,
                            BillItemId INTEGER NOT NULL DEFAULT 0,
                            Quantity REAL NOT NULL DEFAULT 1,
                            UnitPrice REAL NOT NULL DEFAULT 0
                        );
                    ");
                    // Migrate legacy returns into headers
                    Execute(conn, @"
                        INSERT OR IGNORE INTO BillReturns (BillId, RefundAmount, ReturnedAt)
                        SELECT bill_id, 0, return_date FROM BILL_RETURNS GROUP BY bill_id, return_bill_id;
                    ");
                }

                // Migrate stock → InventoryLogs
                if (hasLegacyStock && IsTableEmpty(conn, "InventoryLogs"))
                {
                    Execute(conn, @"
                        CREATE TABLE IF NOT EXISTS InventoryLogs (
                            LogId INTEGER PRIMARY KEY AUTOINCREMENT,
                            ItemId INTEGER NOT NULL,
                            QuantityChange REAL NOT NULL,
                            ChangeType TEXT NOT NULL DEFAULT 'Purchase',
                            ReferenceId INTEGER,
                            ReferenceType TEXT,
                            LogDate DATETIME DEFAULT CURRENT_TIMESTAMP
                        );
                    ");
                    Execute(conn, @"
                        INSERT OR IGNORE INTO InventoryLogs (ItemId, QuantityChange, ChangeType, LogDate)
                        SELECT COALESCE(i.ItemId, 0), s.quantity, 'Purchase', s.system_date
                        FROM stock s
                        LEFT JOIN Items i ON s.product_id = i.Barcode
                        WHERE i.ItemId IS NOT NULL;
                    ");
                }

                txn.Commit();
                AppLogger.Info("Legacy schema migration completed.");
            }
            catch (Exception ex)
            {
                txn.Rollback();
                AppLogger.Error("Legacy schema migration failed", ex);
            }
            finally
            {
                Execute(conn, "PRAGMA foreign_keys = ON;");
            }
        }

        /// <summary>
        /// Migration v1 → v2: Reconcile schema mismatches between code and DB.
        /// Handles: BillItems PK, Payments columns, Bills print columns, BillReturnItems table.
        /// </summary>
        private static void MigrateSchemaV2(SqliteConnection conn)
        {
            AppLogger.Info("Running schema v2 migration (reconcile mismatches)...");

            // Add missing columns to Bills (safe — ALTER TABLE ADD COLUMN is idempotent-safe with IF NOT EXISTS check)
            AddColumnIfNotExists(conn, "Bills", "IsPrinted", "INTEGER DEFAULT 0");
            AddColumnIfNotExists(conn, "Bills", "PrintedAt", "DATETIME");

            // Add ReferenceId/ReferenceType/ImagePath to InventoryLogs if missing
            AddColumnIfNotExists(conn, "InventoryLogs", "ReferenceId", "INTEGER");
            AddColumnIfNotExists(conn, "InventoryLogs", "ReferenceType", "TEXT");
            AddColumnIfNotExists(conn, "InventoryLogs", "ImagePath", "TEXT");

            // Add UserId to BillReturns if missing
            AddColumnIfNotExists(conn, "BillReturns", "UserId", "INTEGER");

            // Migrate BillItems if it uses composite PK (no BillItemId column)
            if (TableExists(conn, "BillItems") && !ColumnExists(conn, "BillItems", "BillItemId"))
            {
                MigrateBillItemsToSurrogatePK(conn);
            }

            // Migrate BillItems if it has ItemDiscount instead of DiscountAmount
            if (TableExists(conn, "BillItems") && ColumnExists(conn, "BillItems", "ItemDiscount") && !ColumnExists(conn, "BillItems", "DiscountAmount"))
            {
                MigrateBillItemsDiscountColumn(conn);
            }

            // Migrate Payments if it has Method instead of PaymentMethod
            if (TableExists(conn, "Payments") && ColumnExists(conn, "Payments", "Method") && !ColumnExists(conn, "Payments", "PaymentMethod"))
            {
                MigratePaymentsColumns(conn);
            }

            // Migrate BillReturns if it has flat ItemId/Quantity instead of header-only
            if (TableExists(conn, "BillReturns") && ColumnExists(conn, "BillReturns", "ItemId"))
            {
                MigrateBillReturnsToHeaderDetail(conn);
            }
        }

        // ────────────────────────────────────────────
        //  Sub-Migrations
        // ────────────────────────────────────────────

        /// <summary>
        /// Migrates BillItems from composite PK (BillId, ItemId)
        /// to surrogate PK (BillItemId AUTOINCREMENT).
        /// </summary>
        private static void MigrateBillItemsToSurrogatePK(SqliteConnection conn)
        {
            AppLogger.Info("Migrating BillItems: composite PK → surrogate BillItemId...");
            Execute(conn, "PRAGMA foreign_keys = OFF;");
            using var txn = conn.BeginTransaction();
            try
            {
                Execute(conn, "ALTER TABLE BillItems RENAME TO BillItems_old;");
                Execute(conn, @"
                    CREATE TABLE BillItems (
                        BillItemId     INTEGER PRIMARY KEY AUTOINCREMENT,
                        BillId         INTEGER NOT NULL,
                        ItemId         INTEGER NOT NULL,
                        Quantity       REAL    NOT NULL CHECK(Quantity > 0),
                        UnitPrice      REAL    NOT NULL CHECK(UnitPrice >= 0),
                        DiscountAmount REAL    DEFAULT 0,
                        FOREIGN KEY (BillId) REFERENCES Bills(BillId) ON DELETE CASCADE,
                        FOREIGN KEY (ItemId) REFERENCES Items(ItemId) ON DELETE RESTRICT
                    );
                ");
                string discountCol = ColumnExists(conn, "BillItems_old", "ItemDiscount") ? "ItemDiscount" :
                                     ColumnExists(conn, "BillItems_old", "DiscountAmount") ? "DiscountAmount" : "0";
                Execute(conn, $@"
                    INSERT INTO BillItems (BillId, ItemId, Quantity, UnitPrice, DiscountAmount)
                    SELECT BillId, ItemId, Quantity, UnitPrice, COALESCE({discountCol}, 0)
                    FROM BillItems_old;
                ");
                Execute(conn, "DROP TABLE BillItems_old;");
                txn.Commit();
                AppLogger.Info("BillItems migration completed.");
            }
            catch (Exception ex)
            {
                txn.Rollback();
                AppLogger.Error("BillItems migration failed", ex);
            }
            finally
            {
                Execute(conn, "PRAGMA foreign_keys = ON;");
            }
        }

        /// <summary>
        /// Renames BillItems.ItemDiscount → DiscountAmount.
        /// </summary>
        private static void MigrateBillItemsDiscountColumn(SqliteConnection conn)
        {
            AppLogger.Info("Migrating BillItems: ItemDiscount → DiscountAmount...");
            Execute(conn, "PRAGMA foreign_keys = OFF;");
            using var txn = conn.BeginTransaction();
            try
            {
                Execute(conn, "ALTER TABLE BillItems RENAME TO BillItems_old;");
                Execute(conn, @"
                    CREATE TABLE BillItems (
                        BillItemId     INTEGER PRIMARY KEY AUTOINCREMENT,
                        BillId         INTEGER NOT NULL,
                        ItemId         INTEGER NOT NULL,
                        Quantity       REAL    NOT NULL CHECK(Quantity > 0),
                        UnitPrice      REAL    NOT NULL CHECK(UnitPrice >= 0),
                        DiscountAmount REAL    DEFAULT 0,
                        FOREIGN KEY (BillId) REFERENCES Bills(BillId) ON DELETE CASCADE,
                        FOREIGN KEY (ItemId) REFERENCES Items(ItemId) ON DELETE RESTRICT
                    );
                ");
                Execute(conn, @"
                    INSERT INTO BillItems (BillItemId, BillId, ItemId, Quantity, UnitPrice, DiscountAmount)
                    SELECT BillItemId, BillId, ItemId, Quantity, UnitPrice, COALESCE(ItemDiscount, 0)
                    FROM BillItems_old;
                ");
                Execute(conn, "DROP TABLE BillItems_old;");
                txn.Commit();
            }
            catch (Exception ex)
            {
                txn.Rollback();
                AppLogger.Error("BillItems discount column migration failed", ex);
            }
            finally
            {
                Execute(conn, "PRAGMA foreign_keys = ON;");
            }
        }

        /// <summary>
        /// Migrates legacy Payments table to canonical bill_payment shape.
        /// </summary>
        private static void MigratePaymentsColumns(SqliteConnection conn)
        {
            AppLogger.Info("Migrating legacy Payments: Method → PaymentMethod + TransactionType...");
            Execute(conn, "PRAGMA foreign_keys = OFF;");
            using var txn = conn.BeginTransaction();
            try
            {
                Execute(conn, "ALTER TABLE Payments RENAME TO LegacyPayOld;");
                Execute(conn, @"
                    CREATE TABLE bill_payment (
                        PaymentId       INTEGER PRIMARY KEY AUTOINCREMENT,
                        BillId          INTEGER NOT NULL,
                        Amount          REAL    NOT NULL,
                        PaymentMethod   TEXT    NOT NULL DEFAULT 'Cash'
                                        CHECK(PaymentMethod IN ('Cash', 'Card', 'Credit')),
                        TransactionType TEXT    NOT NULL DEFAULT 'Sale'
                                        CHECK(TransactionType IN ('Sale', 'Credit Payment', 'Refund')),
                        Note            TEXT,
                        PaidAt          DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (BillId) REFERENCES Bills(BillId) ON DELETE CASCADE
                    );
                ");
                Execute(conn, @"
                    INSERT INTO bill_payment (PaymentId, BillId, Amount, PaymentMethod, TransactionType, Note, PaidAt)
                    SELECT PaymentId, BillId, Amount, COALESCE(Method, 'Cash'), 'Sale', Note, PaidAt
                    FROM LegacyPayOld;
                ");
                Execute(conn, "DROP TABLE LegacyPayOld;");
                txn.Commit();
                AppLogger.Info("Legacy payments migration completed to bill_payment.");
            }
            catch (Exception ex)
            {
                txn.Rollback();
                AppLogger.Error("Legacy payments migration failed", ex);
            }
            finally
            {
                Execute(conn, "PRAGMA foreign_keys = ON;");
            }
        }

        /// <summary>
        /// Migrates BillReturns from flat (ItemId, Quantity per row) to
        /// header/detail (BillReturns + BillReturnItems).
        /// </summary>
        private static void MigrateBillReturnsToHeaderDetail(SqliteConnection conn)
        {
            AppLogger.Info("Migrating BillReturns: flat → header/detail pattern...");
            Execute(conn, "PRAGMA foreign_keys = OFF;");
            using var txn = conn.BeginTransaction();
            try
            {
                // Save old data
                Execute(conn, "ALTER TABLE BillReturns RENAME TO BillReturns_old;");

                // Create new header table
                Execute(conn, @"
                    CREATE TABLE BillReturns (
                        ReturnId    INTEGER PRIMARY KEY AUTOINCREMENT,
                        BillId      INTEGER NOT NULL,
                        UserId      INTEGER,
                        RefundAmount REAL   NOT NULL DEFAULT 0,
                        ReturnedAt  DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (BillId) REFERENCES Bills(BillId) ON DELETE CASCADE,
                        FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE SET NULL
                    );
                ");

                // Create detail table if not exists
                Execute(conn, @"
                    CREATE TABLE IF NOT EXISTS BillReturnItems (
                        ReturnItemId INTEGER PRIMARY KEY AUTOINCREMENT,
                        ReturnId     INTEGER NOT NULL,
                        BillItemId   INTEGER NOT NULL,
                        Quantity     REAL    NOT NULL CHECK(Quantity > 0),
                        UnitPrice    REAL    NOT NULL CHECK(UnitPrice >= 0),
                        FOREIGN KEY (ReturnId) REFERENCES BillReturns(ReturnId) ON DELETE CASCADE,
                        FOREIGN KEY (BillItemId) REFERENCES BillItems(BillItemId) ON DELETE RESTRICT
                    );
                ");

                // Migrate: group old rows into headers
                Execute(conn, @"
                    INSERT INTO BillReturns (BillId, RefundAmount, ReturnedAt)
                    SELECT BillId, SUM(RefundAmount), MAX(ReturnedAt)
                    FROM BillReturns_old
                    GROUP BY BillId, ReturnedAt;
                ");

                // Migrate detail items (best effort — link to BillItems by ItemId)
                Execute(conn, @"
                    INSERT OR IGNORE INTO BillReturnItems (ReturnId, BillItemId, Quantity, UnitPrice)
                    SELECT br.ReturnId, COALESCE(bi.BillItemId, 0), old.Quantity,
                           COALESCE((SELECT UnitPrice FROM BillItems WHERE BillId = old.BillId AND ItemId = old.ItemId LIMIT 1), 0)
                    FROM BillReturns_old old
                    JOIN BillReturns br ON br.BillId = old.BillId
                    LEFT JOIN BillItems bi ON bi.BillId = old.BillId AND bi.ItemId = old.ItemId;
                ");

                Execute(conn, "DROP TABLE BillReturns_old;");
                txn.Commit();
                AppLogger.Info("BillReturns migration completed.");
            }
            catch (Exception ex)
            {
                txn.Rollback();
                AppLogger.Error("BillReturns migration failed", ex);
            }
            finally
            {
                Execute(conn, "PRAGMA foreign_keys = ON;");
            }
        }

        // ────────────────────────────────────────────
        //  Schema Introspection Helpers
        // ────────────────────────────────────────────

        private static bool IsTableEmpty(SqliteConnection conn, string tableName)
        {
            if (!TableExists(conn, tableName)) return true;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {tableName};";
            return Convert.ToInt64(cmd.ExecuteScalar()) == 0;
        }

        private static bool TableExists(SqliteConnection conn, string tableName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
            cmd.Parameters.AddWithValue("@name", tableName);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        private static bool ColumnExists(SqliteConnection conn, string tableName, string columnName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({tableName});";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetString(reader.GetOrdinal("name")).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void AddColumnIfNotExists(SqliteConnection conn, string tableName, string columnName, string columnDefinition)
        {
            if (!TableExists(conn, tableName)) return;
            if (!ColumnExists(conn, tableName, columnName))
            {
                Execute(conn, $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};");
                AppLogger.Info($"Added column '{columnName}' to table '{tableName}'.");
            }
        }

        private static void CreateIndexIfColumnExists(SqliteConnection conn, string indexName, string tableName, string columnName, string indexColumns)
        {
            if (!TableExists(conn, tableName)) return;
            if (!ColumnExists(conn, tableName, columnName)) return;
            Execute(conn, $"CREATE INDEX IF NOT EXISTS {indexName} ON {tableName}({indexColumns});");
        }

        /// <summary>
        /// Ensures bill_payment always has the canonical accounting shape:
        /// (PaymentId, BillId, Amount, Type, CreatedAt).
        /// This is intentionally version-agnostic to self-heal stale user_version values.
        /// </summary>
        private static void EnsureCanonicalBillPaymentShape(SqliteConnection conn)
        {
            if (!TableExists(conn, "bill_payment"))
            {
                Execute(conn, @"
                    CREATE TABLE IF NOT EXISTS bill_payment (
                        PaymentId INTEGER PRIMARY KEY AUTOINCREMENT,
                        BillId    INTEGER NOT NULL,
                        Amount    REAL    NOT NULL CHECK(Amount >= 0),
                        Type      TEXT    NOT NULL CHECK(Type IN ('payment', 'refund')),
                        CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (BillId) REFERENCES Bills(BillId) ON DELETE CASCADE
                    );
                ");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_BillId ON bill_payment(BillId);");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_CreatedAt ON bill_payment(CreatedAt);");
                return;
            }

            bool hasType = ColumnExists(conn, "bill_payment", "Type");
            bool hasCreatedAt = ColumnExists(conn, "bill_payment", "CreatedAt");
            if (hasType && hasCreatedAt)
            {
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_BillId ON bill_payment(BillId);");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_CreatedAt ON bill_payment(CreatedAt);");
                return;
            }

            bool hasTransactionType = ColumnExists(conn, "bill_payment", "TransactionType");
            bool hasPaidAt = ColumnExists(conn, "bill_payment", "PaidAt");

            string typeExpr = hasType
                ? "LOWER(Type)"
                : hasTransactionType
                    ? "CASE WHEN TransactionType = 'Refund' THEN 'refund' ELSE 'payment' END"
                    : "'payment'";

            string createdAtExpr = hasCreatedAt
                ? "CreatedAt"
                : hasPaidAt
                    ? "PaidAt"
                    : "CURRENT_TIMESTAMP";

            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS bill_payment_fix (
                    PaymentId INTEGER PRIMARY KEY AUTOINCREMENT,
                    BillId    INTEGER NOT NULL,
                    Amount    REAL    NOT NULL CHECK(Amount >= 0),
                    Type      TEXT    NOT NULL CHECK(Type IN ('payment', 'refund')),
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (BillId) REFERENCES Bills(BillId) ON DELETE CASCADE
                );
            ");

            Execute(conn, $@"
                INSERT INTO bill_payment_fix (PaymentId, BillId, Amount, Type, CreatedAt)
                SELECT PaymentId, BillId, ABS(Amount),
                       CASE WHEN {typeExpr} = 'refund' THEN 'refund' ELSE 'payment' END,
                       {createdAtExpr}
                FROM bill_payment;
            ");

            Execute(conn, "DROP TABLE bill_payment;");
            Execute(conn, "ALTER TABLE bill_payment_fix RENAME TO bill_payment;");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_BillId ON bill_payment(BillId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_CreatedAt ON bill_payment(CreatedAt);");
            AppLogger.Info("Self-heal: canonicalized bill_payment table shape.");
        }

        /// <summary>
        /// Ensures Bills always contains print tracking columns used by billing flow.
        /// This is version-agnostic to self-heal stale/broken schemas.
        /// </summary>
        private static void EnsureBillsPrintColumns(SqliteConnection conn)
        {
            if (!TableExists(conn, "Bills")) return;

            AddColumnIfNotExists(conn, "Bills", "IsPrinted", "INTEGER DEFAULT 0");
            AddColumnIfNotExists(conn, "Bills", "PrintedAt", "DATETIME");
        }

        // ────────────────────────────────────────────
        //  Helper: execute a non-query SQL statement
        // ────────────────────────────────────────────
        private static void Execute(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }
}
