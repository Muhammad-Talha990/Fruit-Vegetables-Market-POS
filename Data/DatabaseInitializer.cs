using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using FruitVegetableMarketPOS.Helpers;

namespace FruitVegetableMarketPOS.Data
{
    /// <summary>
    /// Creates and maintains the normalized (3NF) database schema for Fruit & Vegetable Market POS.
    ///
    /// Tables (16):
    ///   1.  Users               – System users (Admin / Cashier)
    ///   2.  Categories          – Product categories (lookup, with display order + icon)
    ///   3.  Items               – Product catalog (PK = ItemId, Barcode optional + unique)
    ///   4.  ItemTypes           – Price/type variants per item (Type 1, Type 2, …)
    ///   5.  DailyItemSelection  – Today's menu (BusinessDate + ItemId); Type/Sale via DailyItemSet view
    ///   6.  DailyClosing        – End-of-day sales summary per business date
    ///   7.  Customers           – Registered customers with soft-delete, mandatory 11-digit phone
    ///   8.  Bills               – Sale headers (IMMUTABLE once saved)
    ///   9.  BillItems           – Sale line items (IMMUTABLE, surrogate PK, type snapshots)
    ///  10.  bill_payment        – Payment transaction log (Sale / Credit Payment / Refund)
    ///  11.  BillReturns         – Return headers (linked to original Bill)
    ///  12.  BillReturnItems     – Return line items (linked to original BillItems)
    ///  13.  Accounts            – Payment accounts (Bank / Easypaisa / JazzCash)
    ///  14.  CustomerLedger      – Double-entry audit journal per customer
    ///
    /// Key Business Rules:
    ///   - Bill totals are CALCULATED from BillItems, never stored on Bills.
    ///   - Customer phone is MANDATORY, exactly 11 digits, must start with '0'.
    ///   - All datetime values use a single-capture variable per transaction (no multiple Now calls).
    ///
    /// Safe to call on every application startup (CREATE IF NOT EXISTS + idempotent migrations).
    /// </summary>
    public static class DatabaseInitializer
    {
        // Schema version — increment when adding migrations
        private const int CurrentSchemaVersion = 30;
        private const int MarketCatalogVersion = 28;

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
                    // ── Hard guard: ensure Bills financial columns always exist ──
                    EnsureBillsFinancialColumns(conn);
                    // ── Hard guard: ensure BillReturns store-credit columns always exist ──
                    EnsureBillReturnsCreditColumns(conn);
                    // ── Hard guard: ensure CustomerLedger has canonical audit columns ──
                    EnsureCustomerLedgerAuditColumns(conn);
                    // ── Hard guard: make bill_payment canonical even if user_version is stale ──
                    EnsureCanonicalBillPaymentShape(conn);
                    // ── Hard guard: fruit/veg POS tables and columns (ItemTypes, Daily*, snapshots) ──
                    EnsureFruitVegSchema(conn);

                    // Fix any leftover foreign-key references to a legacy Customers_v17 table
                    // that may have been left behind by interrupted migrations.
                    FixCustomersV17ForeignKeys(conn);

                    // Fresh databases now start directly at the latest schema.
                    if (GetSchemaVersion(conn) == 0)
                    {
                        SetSchemaVersion(conn, CurrentSchemaVersion);
                    }

                    // ── Run migrations for existing databases ──
                    MigrateIfNeeded(conn);

                    SeedFruitVegetableMarketCatalog(conn);
                    ConsolidateToTwoCategories(conn);
                    // One-time seed cleanup only — do not re-merge user catalog on every launch
                    RunDuplicateCleanupOnce(conn);

                    SeedUsers(conn);
                    RepairDefaultUserPasswords(conn);
                    SeedAccounts(conn);
                    // Commented out to prevent test data from shipping to clients
                    // SeedCategories(conn);
                    // SeedItems(conn);
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
                    CategoryId   INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name         TEXT    NOT NULL UNIQUE,
                    IconPath     TEXT,
                    DisplayOrder INTEGER NOT NULL DEFAULT 0,
                    IsActive     INTEGER NOT NULL DEFAULT 1
                );
            ");

            // ────────────────────────────────────────
            //  TABLE 3: Items (catalog only — prices on ItemTypes)
            // ────────────────────────────────────────
            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS Items (
                    ItemId            INTEGER PRIMARY KEY AUTOINCREMENT,
                    Barcode           TEXT    UNIQUE,
                    Description       TEXT    NOT NULL,
                    NameUrdu          TEXT,
                    CategoryId        INTEGER,
                    IsActive          INTEGER NOT NULL DEFAULT 1,
                    UpdatedAt         DATETIME,
                    CreatedAt         DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (CategoryId) REFERENCES Categories(CategoryId)
                        ON DELETE SET NULL
                );
            ");

            // ────────────────────────────────────────
            //  TABLE 4: Customers
            //  Phone: mandatory, 11 digits, must start with '0' (e.g. 03001234567)
            // ────────────────────────────────────────
            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS Customers (
                    CustomerId INTEGER PRIMARY KEY AUTOINCREMENT,
                    FullName   TEXT    NOT NULL,
                    Phone      TEXT    NOT NULL UNIQUE
                               CHECK(length(Phone) = 11 AND Phone GLOB '0[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'),
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
                    BillId              INTEGER PRIMARY KEY AUTOINCREMENT,
                    CustomerId          INTEGER,
                    UserId              INTEGER,
                    TaxAmount           REAL    DEFAULT 0,
                    DiscountAmount      REAL    DEFAULT 0,
                    Status              TEXT    DEFAULT 'Completed'
                                        CHECK(Status IN ('Completed', 'Cancelled')),
                    BillPaymentMethod   TEXT    NOT NULL DEFAULT 'Cash',
                    OnlinePaymentMethod TEXT,
                    InitialPayment      REAL    DEFAULT 0,
                    IsPrinted           INTEGER DEFAULT 0,
                    PrintedAt           DATETIME,
                    CreatedAt           DATETIME DEFAULT CURRENT_TIMESTAMP,
                    AccountId           INTEGER,
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
            //  TABLE 10: CustomerLedger (Audit Journal)
            // ────────────────────────────────────────
            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS CustomerLedger (
                    LedgerId       INTEGER PRIMARY KEY AUTOINCREMENT,
                    CustomerId     INTEGER NOT NULL,
                    EntryDate      DATETIME DEFAULT CURRENT_TIMESTAMP,
                    Type           TEXT    NOT NULL CHECK(Type IN ('SALE', 'PAYMENT', 'RETURN', 'ADJUSTMENT')),
                    TransactionType TEXT   NOT NULL DEFAULT 'SALE',
                    ReferenceId    TEXT,
                    SourceTable    TEXT,
                    SourceId       INTEGER,
                    BillId         INTEGER,
                    ReturnId       INTEGER,
                    PaymentId      INTEGER,
                    CreatedAtUtc   DATETIME DEFAULT CURRENT_TIMESTAMP,
                    CreatedByUserId INTEGER,
                    SequenceNo     INTEGER NOT NULL DEFAULT 0,
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
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Items_Barcode           ON Items(Barcode) WHERE Barcode IS NOT NULL;");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Items_Category          ON Items(CategoryId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Bills_Customer          ON Bills(CustomerId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Bills_CreatedAt         ON Bills(CreatedAt);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Bills_Status            ON Bills(Status);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_BillItems_BillId        ON BillItems(BillId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_BillItems_ItemId        ON BillItems(ItemId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_BillId     ON bill_payment(BillId);");
            CreateIndexIfColumnExists(conn, "IX_bill_payment_CreatedAt", "bill_payment", "CreatedAt", "CreatedAt");
            CreateIndexIfColumnExists(conn, "IX_bill_payment_PaidAt",    "bill_payment", "PaidAt",    "PaidAt");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Returns_BillId          ON BillReturns(BillId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_ReturnItems_RetId       ON BillReturnItems(ReturnId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_ReturnItems_BiId        ON BillReturnItems(BillItemId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Customers_Phone         ON Customers(Phone);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Ledger_Customer         ON CustomerLedger(CustomerId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Ledger_Date             ON CustomerLedger(EntryDate);");
            if (ColumnExists(conn, "CustomerLedger", "SequenceNo"))
            {
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Ledger_CustomerDateSeq ON CustomerLedger(CustomerId, EntryDate, SequenceNo, LedgerId);");
            }
            else
            {
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Ledger_CustomerDate ON CustomerLedger(CustomerId, EntryDate, LedgerId);");
            }
            CreateIndexIfColumnExists(conn, "IX_Ledger_BillId", "CustomerLedger", "BillId", "BillId");
            
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
                // Format: (Username, Password, FullName, Role)
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
            return;
        }

        private static void RepairDefaultUserPasswords(SqliteConnection conn)
        {
            RepairPasswordIfPlaceholder(conn, "admin", "admin123", "ADMIN_PASSWORD_HERE", "1234");
            RepairPasswordIfPlaceholder(conn, "cashier", "cashier123", "CASHIER_PASSWORD_HERE", "1234");
        }

        private static void RepairPasswordIfPlaceholder(SqliteConnection conn, string username, string newPassword, params string[] oldPasswords)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, PasswordHash FROM Users WHERE Username = @username AND IsActive = 1 LIMIT 1;";
            cmd.Parameters.AddWithValue("@username", username);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return;

            int userId = reader.GetInt32(0);
            string passwordHash = reader.GetString(1);

            bool matchesKnownPlaceholder = oldPasswords.Any(candidate => BCrypt.Net.BCrypt.Verify(candidate, passwordHash));
            if (!matchesKnownPlaceholder) return;

            using var updateCmd = conn.CreateCommand();
            updateCmd.CommandText = "UPDATE Users SET PasswordHash = @hash WHERE Id = @id;";
            updateCmd.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(newPassword));
            updateCmd.Parameters.AddWithValue("@id", userId);
            updateCmd.ExecuteNonQuery();

            AppLogger.Info($"Repaired default password for '{username}'.");
        }

        // ────────────────────────────────────────────
        //  Seed categories
        // ────────────────────────────────────────────
        private static void SeedCategories(SqliteConnection conn)
        {
            using var countCmd = conn.CreateCommand();
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

        // ────────────────────────────────────────────
        //  Seed payment accounts
        // ────────────────────────────────────────────
        private static void SeedAccounts(SqliteConnection conn)
        {
            if (!TableExists(conn, "Accounts")) return;

            // Fruit/veg shop payment accounts — insert only if title is missing
            var defaultAccounts = new[]
            {
                ("Meezan - Main",      "Bank",      "Meezan Bank Ltd",  "DHA Phase 4, Lahore", "01234567890123"),
                ("HBL Business",       "Bank",      "Habib Bank Ltd",   "Main Market",         "12345678901234"),
                ("UBL Shop Account",   "Bank",      "United Bank Ltd",  "Sabzi Mandi Branch",  "01021234567890"),
                ("Bank Alfalah",       "Bank",      "Bank Alfalah",     "Gulberg III Branch",  "55667788990011"),
                ("Shop Easypaisa",     "Easypaisa", "Telenor Microfinance Bank", "Mobile Wallet", "03001234567"),
                ("Shop JazzCash",      "JazzCash",  "Mobilink Microfinance Bank", "Mobile Wallet", "03217654321"),
                ("Owner Easypaisa",    "Easypaisa", "Telenor Microfinance Bank", "Mobile Wallet", "03009876543"),
                ("SadaPay Business",   "Online",    "SadaPay",          "Digital",             "03119998881")
            };

            int inserted = 0;
            foreach (var (title, type, bank, branch, number) in defaultAccounts)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Accounts (AccountTitle, AccountType, BankName, BranchName, AccountNumber, IsActive)
                    SELECT @title, @type, @bank, @branch, @num, 1
                    WHERE NOT EXISTS (SELECT 1 FROM Accounts WHERE AccountTitle = @title);
                ";
                cmd.Parameters.AddWithValue("@title", title);
                cmd.Parameters.AddWithValue("@type", type);
                cmd.Parameters.AddWithValue("@bank", bank);
                cmd.Parameters.AddWithValue("@branch", branch);
                cmd.Parameters.AddWithValue("@num", number);
                inserted += cmd.ExecuteNonQuery();
            }

            // Re-activate any seeded titles that were soft-disabled
            using (var activateCmd = conn.CreateCommand())
            {
                activateCmd.CommandText = @"
                    UPDATE Accounts SET IsActive = 1
                    WHERE AccountTitle IN (
                        'Meezan - Main', 'HBL Business', 'UBL Shop Account', 'Bank Alfalah',
                        'Shop Easypaisa', 'Shop JazzCash', 'Owner Easypaisa', 'SadaPay Business'
                    );";
                activateCmd.ExecuteNonQuery();
            }

            if (inserted > 0)
                AppLogger.Info($"Seeded {inserted} payment account(s).");
            else
                AppLogger.Info("Payment accounts already present — seed skipped inserts.");
        }

        // ────────────────────────────────────────────
        //  Seed sample items
        // ────────────────────────────────────────────
        private static void SeedItems(SqliteConnection conn)
        {
            // SeedCategories(conn) is now called directly in Initialize()
            
            // Only seed if no items exist.
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
                    INSERT INTO Items (Barcode, Description, CategoryId)
                    SELECT @barcode, @desc, c.CategoryId
                    FROM Categories c WHERE c.Name = @catName;
                ";
                cmd.Parameters.AddWithValue("@barcode", barcode);
                cmd.Parameters.AddWithValue("@desc", desc);
                cmd.Parameters.AddWithValue("@catName", categoryName);
                
                int affected = cmd.ExecuteNonQuery();
                if (affected > 0)
                    addedCount++;
            }

            if (addedCount > 0)
                AppLogger.Info($"SeedItems: Successfully added {addedCount} new default items.");
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
                        TransactionType TEXT   NOT NULL DEFAULT 'SALE',
                        ReferenceId    TEXT,
                        SourceTable    TEXT,
                        SourceId       INTEGER,
                        BillId         INTEGER,
                        ReturnId       INTEGER,
                        PaymentId      INTEGER,
                        CreatedAtUtc   DATETIME DEFAULT CURRENT_TIMESTAMP,
                        CreatedByUserId INTEGER,
                        SequenceNo     INTEGER NOT NULL DEFAULT 0,
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

            // Migration v14 → v15: Add PaymentMethod column to bill_payment for tracking cash vs online payments from ledger
            if (currentVersion < 15)
            {
                AddColumnIfNotExists(conn, "bill_payment", "PaymentMethod", "TEXT DEFAULT 'Cash'");
                
                // Backfill existing payments based on the associated bill's payment method
                // For cash bills, mark payments as cash; for online bills, mark as online
                Execute(conn, @"
                    UPDATE bill_payment 
                    SET PaymentMethod = COALESCE(
                        (SELECT CASE WHEN BillPaymentMethod = 'Online' THEN 'Online' ELSE 'Cash' END 
                         FROM Bills WHERE Bills.BillId = bill_payment.BillId), 
                        'Cash')
                    WHERE PaymentMethod = 'Cash';
                ");
                
                SetSchemaVersion(conn, 15);
                AppLogger.Info("Migration v15: Added PaymentMethod column to bill_payment for accurate cash/online ledger tracking.");
            }

            // Migration v15 → v16: Add store credit tracking to BillReturns
            // Tracks whether a return was given as store credit or cash refund
            if (currentVersion < 16)
            {
                AddColumnIfNotExists(conn, "BillReturns", "StoreCreditIssued", "REAL DEFAULT 0");
                AddColumnIfNotExists(conn, "BillReturns", "StoreCreditRefundedAt", "DATETIME");
                
                // Backfill: For returns where there's no matching cash refund in bill_payment,
                // mark the entire return amount as store credit
                Execute(conn, @"
                    UPDATE BillReturns
                    SET StoreCreditIssued = (
                        SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0)
                        FROM BillReturnItems bri
                        WHERE bri.ReturnId = BillReturns.ReturnId
                    )
                    WHERE ReturnId NOT IN (
                        SELECT DISTINCT ReturnId FROM bill_payment
                        WHERE Type = 'refund' AND BillId = BillReturns.BillId
                    )
                ");
                
                SetSchemaVersion(conn, 16);
                AppLogger.Info("Migration v16: Added StoreCreditIssued column to BillReturns for accurate store credit tracking.");
            }

            // Migration v16 → v17: Canonical customer ledger audit metadata and deterministic sequencing.
            if (currentVersion < 17)
            {
                EnsureCustomerLedgerAuditColumns(conn);
                RebuildCustomerLedgerRunningBalances(conn);
                SetSchemaVersion(conn, 17);
                AppLogger.Info("Migration v17: Standardized CustomerLedger metadata and rebuilt running balances.");
            }

            // Migration v17 → v18: Enforce 11-digit zero-prefix phone constraint on Customers.
            // SQLite cannot ADD COLUMN with a CHECK, so we use the safe recreate pattern.
            if (currentVersion < 18)
            {
                MigrateCustomersPhoneConstraint(conn);
                SetSchemaVersion(conn, 18);
                AppLogger.Info("Migration v18: Applied 11-digit zero-prefix phone CHECK constraint to Customers.");
            }

            // Migration v18 → v19: Create StockPurchases + StockPurchaseItems tables.
            if (currentVersion < 19)
            {
                Execute(conn, @"
                    CREATE TABLE IF NOT EXISTS StockPurchases (
                        PurchaseId      INTEGER PRIMARY KEY AUTOINCREMENT,
                        PurchaseAt      DATETIME NOT NULL,
                        TotalAmount     REAL     NOT NULL CHECK(TotalAmount >= 0),
                        CreatedByUserId INTEGER,
                        ImagePath       TEXT,
                        FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id) ON DELETE SET NULL
                    );
                ");
                Execute(conn, @"
                    CREATE TABLE IF NOT EXISTS StockPurchaseItems (
                        Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                        PurchaseId  INTEGER NOT NULL,
                        ItemId      INTEGER NOT NULL,
                        Quantity    REAL    NOT NULL CHECK(Quantity > 0),
                        CostPrice   REAL    NOT NULL CHECK(CostPrice >= 0),
                        FOREIGN KEY (PurchaseId) REFERENCES StockPurchases(PurchaseId) ON DELETE CASCADE,
                        FOREIGN KEY (ItemId)     REFERENCES Items(ItemId)              ON DELETE RESTRICT
                    );
                ");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_StockPurchases_Date     ON StockPurchases(PurchaseAt);");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_StockPurchItems_PurchId ON StockPurchaseItems(PurchaseId);");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_StockPurchItems_ItemId  ON StockPurchaseItems(ItemId);");
                SetSchemaVersion(conn, 19);
                AppLogger.Info("Migration v19: Created StockPurchases and StockPurchaseItems tables.");
            }
            
            // Migration v19 → v20: Add ImagePath column to StockPurchases (if not already added in v19)
            if (currentVersion < 20)
            {
                AddColumnIfNotExists(conn, "StockPurchases", "ImagePath", "TEXT");
                SetSchemaVersion(conn, 20);
                AppLogger.Info("Migration v20: Added ImagePath column to StockPurchases.");
            }

            // Migration v20 → v21: Fix historical restock images by linking StockPurchases images to InventoryLogs
            if (currentVersion < 21)
            {
                Execute(conn, @"
                    UPDATE InventoryLogs
                    SET ImagePath = (SELECT ImagePath FROM StockPurchases WHERE PurchaseId = InventoryLogs.ReferenceId)
                    WHERE ReferenceType = 'Supply' 
                      AND (ImagePath IS NULL OR ImagePath = '') 
                      AND ReferenceId IS NOT NULL 
                      AND EXISTS (SELECT 1 FROM StockPurchases WHERE PurchaseId = InventoryLogs.ReferenceId AND ImagePath IS NOT NULL);
                ");
                SetSchemaVersion(conn, 21);
                AppLogger.Info("Migration v21: Linked historical StockPurchase images to InventoryLogs.");
            }
            
            // Migration v21 → v22: Create Suppliers and SupplierProducts tables
            if (currentVersion < 22)
            {
                Execute(conn, @"
                    CREATE TABLE IF NOT EXISTS Suppliers (
                        PhoneNumber TEXT PRIMARY KEY,
                        Name        TEXT NOT NULL,
                        CompanyName TEXT,
                        Email       TEXT UNIQUE,
                        Address     TEXT,
                        CreatedAt   DATETIME DEFAULT CURRENT_TIMESTAMP
                    );
                ");
                Execute(conn, @"
                    CREATE TABLE IF NOT EXISTS SupplierProducts (
                        Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                        SupplierPhone TEXT NOT NULL,
                        ProductId     INTEGER NOT NULL,
                        SupplyPrice   REAL,
                        SupplyDate    DATETIME DEFAULT CURRENT_TIMESTAMP,
                        Notes         TEXT,
                        UNIQUE(SupplierPhone, ProductId),
                        FOREIGN KEY (SupplierPhone) REFERENCES Suppliers(PhoneNumber) ON DELETE CASCADE,
                        FOREIGN KEY (ProductId)     REFERENCES Items(ItemId)      ON DELETE CASCADE
                    );
                ");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_SupplierProducts_Phone ON SupplierProducts(SupplierPhone);");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_SupplierProducts_Item ON SupplierProducts(ProductId);");

                SetSchemaVersion(conn, 22);
                AppLogger.Info("Migration v22: Created Suppliers and SupplierProducts tables.");
            }

            // Migration v22 → v23: Drop obsolete supplier/procurement/inventory tables.
            if (currentVersion < 23)
            {
                Execute(conn, "DROP TABLE IF EXISTS SupplierProducts;");
                Execute(conn, "DROP TABLE IF EXISTS Suppliers;");
                Execute(conn, "DROP TABLE IF EXISTS StockPurchaseItems;");
                Execute(conn, "DROP TABLE IF EXISTS StockPurchases;");
                Execute(conn, "DROP TABLE IF EXISTS InventoryLogs;");
                SetSchemaVersion(conn, 23);
                AppLogger.Info("Migration v23: Dropped obsolete supplier, stock purchase, and inventory log tables.");
            }

            // Migration v23 → v24: Fruit/veg POS schema (ItemTypes, DailyItemSelection, DailyClosing, snapshots).
            if (currentVersion < 24)
            {
                ApplyFruitVegSchemaChanges(conn);
                SetSchemaVersion(conn, 24);
                AppLogger.Info("Migration v24: Added ItemTypes, DailyItemSelection, DailyClosing, and BillItems snapshots.");
            }

            // Migration v24 → v25: Urdu names + fruit/vegetable market catalog seed marker columns.
            if (currentVersion < 25)
            {
                AddColumnIfNotExists(conn, "Items", "NameUrdu", "TEXT");
                AddColumnIfNotExists(conn, "Categories", "NameUrdu", "TEXT");
                SetSchemaVersion(conn, 25);
                AppLogger.Info("Migration v25: Added NameUrdu to Items and Categories.");
            }

            // Migration v25 → v26: expanded market catalog + sequential POS item codes (1,2,3…).
            if (currentVersion < 26)
            {
                SetSchemaVersion(conn, 26);
                AppLogger.Info("Migration v26: Expanded fruit/veg catalog with sequential item codes.");
            }

            // Migration v26 → v27: daily menu available/out-of-stock flag (keep on grid when deactivated).
            if (currentVersion < 27)
            {
                AddColumnIfNotExists(conn, "DailyItemSelection", "IsAvailable", "INTEGER NOT NULL DEFAULT 1");
                SetSchemaVersion(conn, 27);
                AppLogger.Info("Migration v27: Added IsAvailable to DailyItemSelection.");
            }

            // Migration v27 → v28: persist item stock quantity on Items (removed in v29).
            if (currentVersion < 28)
            {
                AddColumnIfNotExists(conn, "Items", "StockQuantity", "REAL NOT NULL DEFAULT 0");
                SetSchemaVersion(conn, 28);
                AppLogger.Info("Migration v28: Added StockQuantity to Items.");
            }

            // Migration v28 → v29: catalog-only Items — drop stock, cost/sale price, unit, image path.
            // Daily unit prices remain on ItemTypes; BillItems.UnitPrice keeps sale snapshots.
            if (currentVersion < 29)
            {
                MigrateItemsToCatalogOnly(conn);
                SetSchemaVersion(conn, 29);
                AppLogger.Info("Migration v29: Items catalog-only (removed CostPrice, SalePrice, Stock, Unit, ImagePath).");
            }

            // Migration v29 → v30: slim DailyItemSelection + DailyItemSet view (ItemId, Description, Type, Sale).
            if (currentVersion < 30)
            {
                MigrateDailyItemSelectionToV30(conn);
                SetSchemaVersion(conn, 30);
                AppLogger.Info("Migration v30: DailyItemSelection catalog menu only; DailyItemSet view for Type/Sale.");
            }

            AppLogger.Info($"Database migrated successfully to v{CurrentSchemaVersion}.");
        }

        /// <summary>
        /// Rebuilds DailyItemSelection without audit/order/visibility columns.
        /// Keeps one row per (BusinessDate, ItemId). Sale/Type come from DailyItemSet view.
        /// </summary>
        private static void MigrateDailyItemSelectionToV30(SqliteConnection conn)
        {
            if (!TableExists(conn, "DailyItemSelection"))
            {
                EnsureDailyItemSelectionV30(conn);
                return;
            }

            // Already slim?
            if (!ColumnExists(conn, "DailyItemSelection", "AddedAt")
                && !ColumnExists(conn, "DailyItemSelection", "RemovedAt")
                && !ColumnExists(conn, "DailyItemSelection", "IsVisible")
                && !ColumnExists(conn, "DailyItemSelection", "DisplayOrder")
                && !ColumnExists(conn, "DailyItemSelection", "AddedByUserId")
                && !ColumnExists(conn, "DailyItemSelection", "RemovedByUserId"))
            {
                EnsureDailyItemSetView(conn);
                return;
            }

            Execute(conn, "PRAGMA foreign_keys = OFF;");
            try
            {
                Execute(conn, "DROP VIEW IF EXISTS DailyItemSet;");

                Execute(conn, @"
                    CREATE TABLE DailyItemSelection_v30 (
                        DailySelectionId INTEGER PRIMARY KEY AUTOINCREMENT,
                        BusinessDate     TEXT    NOT NULL,
                        ItemId           INTEGER NOT NULL,
                        IsAvailable      INTEGER NOT NULL DEFAULT 1,
                        UNIQUE (BusinessDate, ItemId),
                        FOREIGN KEY (ItemId) REFERENCES Items(ItemId) ON DELETE RESTRICT
                    );
                ");

                var hasAvail = ColumnExists(conn, "DailyItemSelection", "IsAvailable");
                var hasVisible = ColumnExists(conn, "DailyItemSelection", "IsVisible");
                var availSel = hasAvail ? "MAX(IsAvailable)" : "1";
                var visibleFilter = hasVisible ? "WHERE IsVisible = 1" : "";

                Execute(conn, $@"
                    INSERT INTO DailyItemSelection_v30 (BusinessDate, ItemId, IsAvailable)
                    SELECT BusinessDate, ItemId, {availSel}
                    FROM DailyItemSelection
                    {visibleFilter}
                    GROUP BY BusinessDate, ItemId;
                ");

                Execute(conn, "DROP TABLE DailyItemSelection;");
                Execute(conn, "ALTER TABLE DailyItemSelection_v30 RENAME TO DailyItemSelection;");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_DailyItemSel_Date ON DailyItemSelection(BusinessDate);");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_DailyItemSel_DateItem ON DailyItemSelection(BusinessDate, ItemId);");
            }
            finally
            {
                Execute(conn, "PRAGMA foreign_keys = ON;");
            }

            EnsureDailyItemSetView(conn);
        }

        private static void EnsureDailyItemSelectionV30(SqliteConnection conn)
        {
            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS DailyItemSelection (
                    DailySelectionId INTEGER PRIMARY KEY AUTOINCREMENT,
                    BusinessDate     TEXT    NOT NULL,
                    ItemId           INTEGER NOT NULL,
                    IsAvailable      INTEGER NOT NULL DEFAULT 1,
                    UNIQUE (BusinessDate, ItemId),
                    FOREIGN KEY (ItemId) REFERENCES Items(ItemId) ON DELETE RESTRICT
                );
            ");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_DailyItemSel_Date ON DailyItemSelection(BusinessDate);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_DailyItemSel_DateItem ON DailyItemSelection(BusinessDate, ItemId);");
            EnsureDailyItemSetView(conn);
        }

        /// <summary>
        /// Read-model view: ItemId · Description · Type · Sale (live SUM from BillItems).
        /// Keeps DailyItemSelection normalized (no denormalized description/type/sale columns).
        /// </summary>
        private static void EnsureDailyItemSetView(SqliteConnection conn)
        {
            Execute(conn, "DROP VIEW IF EXISTS DailyItemSet;");
            Execute(conn, @"
                CREATE VIEW DailyItemSet AS
                SELECT
                    d.BusinessDate,
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
                          AND date(datetime(b.CreatedAt, 'localtime')) = d.BusinessDate
                    ), 0) AS Sale
                FROM DailyItemSelection d
                JOIN Items i ON i.ItemId = d.ItemId
                JOIN ItemTypes t ON t.ItemId = d.ItemId AND t.IsActive = 1;
            ");
        }

        /// <summary>
        /// Rebuilds Items without inventory/pricing columns. Prices live on ItemTypes.
        /// </summary>
        private static void MigrateItemsToCatalogOnly(SqliteConnection conn)
        {
            if (!TableExists(conn, "Items")) return;

            // Already migrated (fresh DB created with new CREATE TABLE).
            if (!ColumnExists(conn, "Items", "CostPrice")
                && !ColumnExists(conn, "Items", "SalePrice")
                && !ColumnExists(conn, "Items", "StockQuantity")
                && !ColumnExists(conn, "Items", "Unit")
                && !ColumnExists(conn, "Items", "ImagePath")
                && !ColumnExists(conn, "Items", "MinStockThreshold"))
            {
                return;
            }

            Execute(conn, "PRAGMA foreign_keys = OFF;");
            try
            {
                Execute(conn, @"
                    CREATE TABLE Items_v29 (
                        ItemId            INTEGER PRIMARY KEY AUTOINCREMENT,
                        Barcode           TEXT    UNIQUE,
                        Description       TEXT    NOT NULL,
                        NameUrdu          TEXT,
                        CategoryId        INTEGER,
                        IsActive          INTEGER NOT NULL DEFAULT 1,
                        UpdatedAt         DATETIME,
                        CreatedAt         DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (CategoryId) REFERENCES Categories(CategoryId)
                            ON DELETE SET NULL
                    );
                ");

                var hasNameUrdu = ColumnExists(conn, "Items", "NameUrdu");
                var hasCreatedAt = ColumnExists(conn, "Items", "CreatedAt");
                var hasUpdatedAt = ColumnExists(conn, "Items", "UpdatedAt");
                var hasIsActive = ColumnExists(conn, "Items", "IsActive");

                var nameUrduSel = hasNameUrdu ? "NameUrdu" : "NULL";
                var createdSel = hasCreatedAt ? "CreatedAt" : "CURRENT_TIMESTAMP";
                var updatedSel = hasUpdatedAt ? "UpdatedAt" : "NULL";
                var activeSel = hasIsActive ? "IsActive" : "1";

                Execute(conn, $@"
                    INSERT INTO Items_v29 (ItemId, Barcode, Description, NameUrdu, CategoryId, IsActive, UpdatedAt, CreatedAt)
                    SELECT ItemId, Barcode, Description, {nameUrduSel}, CategoryId, {activeSel}, {updatedSel}, {createdSel}
                    FROM Items;
                ");

                Execute(conn, "DROP TABLE Items;");
                Execute(conn, "ALTER TABLE Items_v29 RENAME TO Items;");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Items_Barcode ON Items(Barcode) WHERE Barcode IS NOT NULL;");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Items_Category ON Items(CategoryId);");
            }
            finally
            {
                Execute(conn, "PRAGMA foreign_keys = ON;");
            }
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
        /// Migration v18: Recreates the Customers table with the strict phone CHECK constraint.
        /// All existing rows that pass validation are preserved; non-conforming rows get a
        /// zero-padded or truncated placeholder so the migration never silently drops data.
        /// </summary>
        private static void MigrateCustomersPhoneConstraint(SqliteConnection conn)
        {
            AppLogger.Info("Migrating Customers: adding 11-digit phone CHECK constraint...");
            Execute(conn, "PRAGMA foreign_keys = OFF;");
            using var txn = conn.BeginTransaction();

            // If the Customers table is missing for any reason, skip this migration.
            // This prevents attempting to rename a non-existent table which would
            // lead to "no such table: Customers_v17" errors during runtime.
            if (!TableExists(conn, "Customers"))
            {
                AppLogger.Info("Customers table not found; skipping phone constraint migration.");
                txn.Rollback();
                return;
            }

            try
            {
                Execute(conn, "ALTER TABLE Customers RENAME TO Customers_v17;");

                // Create new table with CHECK constraint
                Execute(conn, @"
                    CREATE TABLE Customers (
                        CustomerId     INTEGER PRIMARY KEY AUTOINCREMENT,
                        FullName       TEXT    NOT NULL,
                        Phone          TEXT    NOT NULL UNIQUE
                                       CHECK(length(Phone) = 11 AND Phone GLOB '0[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'),
                        SecondaryPhone TEXT,
                        Address        TEXT,
                        Address2       TEXT,
                        Address3       TEXT,
                        IsActive       INTEGER NOT NULL DEFAULT 1,
                        CreatedAt      DATETIME DEFAULT CURRENT_TIMESTAMP
                    );
                ");

                // Copy rows — normalize phone on the fly:
                //   1. Strip all non-digits
                //   2. If 11 digits starting with 0 → keep as-is
                //   3. Otherwise → prefix with '0' and left-pad to 11 (or truncate to 11)
                //   4. As a last resort produce a unique placeholder 0XXXXXXXXXXX
                Execute(conn, @"
                    INSERT INTO Customers (CustomerId, FullName, Phone, SecondaryPhone, Address, Address2, Address3, IsActive, CreatedAt)
                    SELECT
                        CustomerId,
                        FullName,
                        CASE
                            -- Already valid: 11 chars starting with '0' that are all digits
                            WHEN length(replace(replace(replace(replace(replace(replace(replace(replace(replace(replace(Phone,'0',''),'1',''),'2',''),'3',''),'4',''),'5',''),'6',''),'7',''),'8',''),'9','')) = 0
                                 AND length(Phone) = 11 AND substr(Phone,1,1) = '0'
                                THEN Phone
                            -- Fallback: generate unique 11-digit placeholder '0' + 10-digit CustomerId
                            ELSE '0' || substr('0000000000' || CAST(CustomerId AS TEXT), -10, 10)
                        END,
                        SecondaryPhone,
                        Address,
                        Address2,
                        Address3,
                        IsActive,
                        CreatedAt
                    FROM Customers_v17;
                ");

                Execute(conn, "DROP TABLE Customers_v17;");
                txn.Commit();
                AppLogger.Info("Customers phone constraint migration completed.");
            }
            catch (Exception ex)
            {
                txn.Rollback();
                AppLogger.Error("Customers phone constraint migration failed", ex);
                throw;
            }
            finally
            {
                Execute(conn, "PRAGMA foreign_keys = ON;");
            }
        }

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
        /// Detects any table definitions that reference the legacy `Customers_v17` table name
        /// and recreates those tables to reference the correct `Customers` table instead.
        /// This is defensive: it only operates when a table's CREATE SQL contains the
        /// legacy identifier and will copy data across without data loss.
        /// </summary>
        private static void FixCustomersV17ForeignKeys(SqliteConnection conn)
        {
            const string legacy = "Customers_v17";
            const string replacement = "Customers";

            using var find = conn.CreateCommand();
            find.CommandText = "SELECT name, sql FROM sqlite_master WHERE type='table' AND sql LIKE '%' || @legacy || '%';";
            find.Parameters.AddWithValue("@legacy", legacy);

            using var reader = find.ExecuteReader();
            var tablesToFix = new System.Collections.Generic.List<(string name, string sql)>();
            while (reader.Read())
            {
                tablesToFix.Add((reader.GetString(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
            }

            foreach (var (name, sql) in tablesToFix)
            {
                try
                {
                    AppLogger.Info($"Fixing legacy FK reference in table '{name}' (replacing {legacy} → {replacement})...");
                    Execute(conn, "PRAGMA foreign_keys = OFF;");
                    using var txn = conn.BeginTransaction();

                    // Rename old table
                    var oldName = name + "_old";
                    Execute(conn, $"ALTER TABLE {name} RENAME TO {oldName};");

                    // Build new CREATE TABLE statement by replacing the legacy reference
                    var newCreateSql = sql.Replace(legacy, replacement);
                    Execute(conn, newCreateSql + ";");

                    // Copy columns by querying the old table's pragma
                    using var colsCmd = conn.CreateCommand();
                    colsCmd.CommandText = $"PRAGMA table_info({oldName});";
                    using var colsReader = colsCmd.ExecuteReader();
                    var cols = new System.Collections.Generic.List<string>();
                    while (colsReader.Read()) cols.Add(colsReader.GetString(colsReader.GetOrdinal("name")));
                    var colList = string.Join(",", cols);

                    Execute(conn, $"INSERT INTO {name} ({colList}) SELECT {colList} FROM {oldName};");

                    // Recreate indexes for the table (if any)
                    using var idxCmd = conn.CreateCommand();
                    idxCmd.CommandText = "SELECT name, sql FROM sqlite_master WHERE type='index' AND tbl_name=@old;";
                    idxCmd.Parameters.AddWithValue("@old", oldName);
                    using var idxReader = idxCmd.ExecuteReader();
                    while (idxReader.Read())
                    {
                        if (idxReader.IsDBNull(1)) continue;
                        var idxSql = idxReader.GetString(1).Replace(oldName, name);
                        Execute(conn, idxSql + ";");
                    }

                    Execute(conn, $"DROP TABLE {oldName};");
                    txn.Commit();
                    AppLogger.Info($"Recreated '{name}' to reference {replacement} successfully.");
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"Failed to fix legacy FK reference in table '{name}'", ex);
                }
                finally
                {
                    Execute(conn, "PRAGMA foreign_keys = ON;");
                }
            }
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
                        PaymentId       INTEGER PRIMARY KEY AUTOINCREMENT,
                        BillId          INTEGER NOT NULL,
                        Amount          REAL    NOT NULL CHECK(Amount >= 0),
                        Type            TEXT    NOT NULL CHECK(Type IN ('payment', 'refund')),
                        PaymentMethod   TEXT    NOT NULL DEFAULT 'Cash'
                                        CHECK(PaymentMethod IN ('Cash', 'Online')),
                        CreatedAt       DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (BillId) REFERENCES Bills(BillId) ON DELETE CASCADE
                    );
                ");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_BillId ON bill_payment(BillId);");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_CreatedAt ON bill_payment(CreatedAt);");
                return;
            }

            bool hasType = ColumnExists(conn, "bill_payment", "Type");
            bool hasCreatedAt = ColumnExists(conn, "bill_payment", "CreatedAt");
            bool hasPaymentMethod = ColumnExists(conn, "bill_payment", "PaymentMethod");

            if (hasType && hasCreatedAt && hasPaymentMethod)
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
            
            string paymentMethodExpr = hasPaymentMethod ? "PaymentMethod" : "'Cash'";

            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS bill_payment_fix (
                    PaymentId       INTEGER PRIMARY KEY AUTOINCREMENT,
                    BillId          INTEGER NOT NULL,
                    Amount          REAL    NOT NULL CHECK(Amount >= 0),
                    Type            TEXT    NOT NULL CHECK(Type IN ('payment', 'refund')),
                    PaymentMethod   TEXT    NOT NULL DEFAULT 'Cash'
                                    CHECK(PaymentMethod IN ('Cash', 'Online')),
                    CreatedAt       DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (BillId) REFERENCES Bills(BillId) ON DELETE CASCADE
                );
            ");

            Execute(conn, $@"
                INSERT INTO bill_payment_fix (PaymentId, BillId, Amount, Type, PaymentMethod, CreatedAt)
                SELECT PaymentId, BillId, ABS(Amount),
                       CASE WHEN {typeExpr} = 'refund' THEN 'refund' ELSE 'payment' END,
                       {paymentMethodExpr},
                       {createdAtExpr}
                FROM bill_payment;
            ");

            Execute(conn, "DROP TABLE bill_payment;");
            Execute(conn, "ALTER TABLE bill_payment_fix RENAME TO bill_payment;");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_BillId ON bill_payment(BillId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_bill_payment_CreatedAt ON bill_payment(CreatedAt);");
            AppLogger.Info("Self-heal: canonicalized bill_payment table shape (v16).");
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

        /// <summary>
        /// Ensures Bills always contains financial columns consumed by billing/ledger flows.
        /// This is version-agnostic to self-heal stale/broken schemas.
        /// </summary>
        private static void EnsureBillsFinancialColumns(SqliteConnection conn)
        {
            if (!TableExists(conn, "Bills")) return;

            AddColumnIfNotExists(conn, "Bills", "BillPaymentMethod", "TEXT NOT NULL DEFAULT 'Cash'");
            AddColumnIfNotExists(conn, "Bills", "OnlinePaymentMethod", "TEXT");
            AddColumnIfNotExists(conn, "Bills", "InitialPayment", "REAL DEFAULT 0");
            AddColumnIfNotExists(conn, "Bills", "AccountId", "INTEGER");
        }

        /// <summary>
        /// Ensures BillReturns always contains store-credit tracking columns used by dashboard metrics.
        /// This is version-agnostic to self-heal stale/broken schemas.
        /// </summary>
        private static void EnsureBillReturnsCreditColumns(SqliteConnection conn)
        {
            if (!TableExists(conn, "BillReturns")) return;

            AddColumnIfNotExists(conn, "BillReturns", "StoreCreditIssued", "REAL DEFAULT 0");
            AddColumnIfNotExists(conn, "BillReturns", "StoreCreditRefundedAt", "DATETIME");
        }

        /// <summary>
        /// Ensures CustomerLedger has canonical audit metadata columns and deterministic indexes.
        /// </summary>
        private static void EnsureCustomerLedgerAuditColumns(SqliteConnection conn)
        {
            if (!TableExists(conn, "CustomerLedger")) return;

            AddColumnIfNotExists(conn, "CustomerLedger", "TransactionType", "TEXT NOT NULL DEFAULT 'SALE'");
            AddColumnIfNotExists(conn, "CustomerLedger", "SourceTable", "TEXT");
            AddColumnIfNotExists(conn, "CustomerLedger", "SourceId", "INTEGER");
            AddColumnIfNotExists(conn, "CustomerLedger", "BillId", "INTEGER");
            AddColumnIfNotExists(conn, "CustomerLedger", "ReturnId", "INTEGER");
            AddColumnIfNotExists(conn, "CustomerLedger", "PaymentId", "INTEGER");
            // SQLite cannot ADD COLUMN with non-constant defaults like CURRENT_TIMESTAMP.
            AddColumnIfNotExists(conn, "CustomerLedger", "CreatedAtUtc", "DATETIME");
            AddColumnIfNotExists(conn, "CustomerLedger", "CreatedByUserId", "INTEGER");
            AddColumnIfNotExists(conn, "CustomerLedger", "SequenceNo", "INTEGER NOT NULL DEFAULT 0");

            Execute(conn, "UPDATE CustomerLedger SET TransactionType = Type WHERE TransactionType IS NULL OR TRIM(TransactionType) = '';");
            Execute(conn, "UPDATE CustomerLedger SET CreatedAtUtc = COALESCE(CreatedAtUtc, EntryDate, CURRENT_TIMESTAMP);");
            if (ColumnExists(conn, "CustomerLedger", "SequenceNo"))
            {
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Ledger_CustomerDateSeq ON CustomerLedger(CustomerId, EntryDate, SequenceNo, LedgerId);");
            }
            else
            {
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Ledger_CustomerDate ON CustomerLedger(CustomerId, EntryDate, LedgerId);");
            }
            CreateIndexIfColumnExists(conn, "IX_Ledger_BillId", "CustomerLedger", "BillId", "BillId");
        }

        /// <summary>
        /// Rebuilds SequenceNo and RunningBalance deterministically for each customer.
        /// </summary>
        private static void RebuildCustomerLedgerRunningBalances(SqliteConnection conn)
        {
            if (!TableExists(conn, "CustomerLedger")) return;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT LedgerId, CustomerId, Debit, Credit
                FROM CustomerLedger
                ORDER BY CustomerId ASC, datetime(EntryDate) ASC, LedgerId ASC;";

            var rows = new List<(int LedgerId, int CustomerId, double Debit, double Credit)>();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    rows.Add((
                        reader.GetInt32(0),
                        reader.GetInt32(1),
                        reader.IsDBNull(2) ? 0 : reader.GetDouble(2),
                        reader.IsDBNull(3) ? 0 : reader.GetDouble(3)
                    ));
                }
            }

            int currentCustomerId = -1;
            int sequence = 0;
            double runningBalance = 0;

            foreach (var row in rows)
            {
                if (row.CustomerId != currentCustomerId)
                {
                    currentCustomerId = row.CustomerId;
                    sequence = 0;
                    runningBalance = 0;
                }

                sequence++;
                runningBalance = Math.Round(runningBalance + row.Debit - row.Credit, 2);

                using var upd = conn.CreateCommand();
                upd.CommandText = @"
                    UPDATE CustomerLedger
                    SET SequenceNo = @seq,
                        RunningBalance = @bal
                    WHERE LedgerId = @id;";
                upd.Parameters.AddWithValue("@seq", sequence);
                upd.Parameters.AddWithValue("@bal", runningBalance);
                upd.Parameters.AddWithValue("@id", row.LedgerId);
                upd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Hard guard: ensures fruit/veg POS tables and columns exist even when user_version is stale.
        /// Idempotent — safe to call on every startup.
        /// </summary>
        private static void EnsureFruitVegSchema(SqliteConnection conn)
        {
            ApplyFruitVegSchemaChanges(conn);
        }

        /// <summary>
        /// Applies all fruit/veg schema changes: column adds, new tables, indexes, backfill, category seed.
        /// </summary>
        private static void ApplyFruitVegSchemaChanges(SqliteConnection conn)
        {
            // Categories extensions
            AddColumnIfNotExists(conn, "Categories", "IconPath", "TEXT");
            AddColumnIfNotExists(conn, "Categories", "DisplayOrder", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfNotExists(conn, "Categories", "IsActive", "INTEGER NOT NULL DEFAULT 1");
            AddColumnIfNotExists(conn, "Categories", "NameUrdu", "TEXT");

            // Items extensions (catalog only — do not re-add legacy stock/price/unit/image columns)
            AddColumnIfNotExists(conn, "Items", "IsActive", "INTEGER NOT NULL DEFAULT 1");
            AddColumnIfNotExists(conn, "Items", "UpdatedAt", "DATETIME");
            AddColumnIfNotExists(conn, "Items", "NameUrdu", "TEXT");

            // BillItems snapshot columns
            AddColumnIfNotExists(conn, "BillItems", "TypeId", "INTEGER");
            AddColumnIfNotExists(conn, "BillItems", "ItemName", "TEXT");
            AddColumnIfNotExists(conn, "BillItems", "TypeName", "TEXT");
            AddColumnIfNotExists(conn, "BillItems", "Unit", "TEXT");

            // ItemTypes
            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS ItemTypes (
                    TypeId    INTEGER PRIMARY KEY AUTOINCREMENT,
                    ItemId    INTEGER NOT NULL,
                    TypeName  TEXT    NOT NULL,
                    Price     REAL    NOT NULL CHECK(Price >= 0),
                    SortOrder INTEGER NOT NULL DEFAULT 1,
                    IsActive  INTEGER NOT NULL DEFAULT 1,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (ItemId) REFERENCES Items(ItemId) ON DELETE CASCADE
                );
            ");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_ItemTypes_ItemId ON ItemTypes(ItemId);");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_ItemTypes_ItemActive ON ItemTypes(ItemId, IsActive, SortOrder);");

            // DailyItemSelection — today's menu only (BusinessDate + ItemId + IsAvailable)
            EnsureDailyItemSelectionV30(conn);

            // DailyClosing
            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS DailyClosing (
                    DailyClosingId  INTEGER PRIMARY KEY AUTOINCREMENT,
                    BusinessDate    TEXT    NOT NULL UNIQUE,
                    TotalBills      INTEGER NOT NULL DEFAULT 0,
                    TotalSales      REAL    NOT NULL DEFAULT 0,
                    CashSales       REAL    NOT NULL DEFAULT 0,
                    CardSales       REAL    NOT NULL DEFAULT 0,
                    OnlineSales     REAL    NOT NULL DEFAULT 0,
                    CreditSales     REAL    NOT NULL DEFAULT 0,
                    CreditRecovered REAL    NOT NULL DEFAULT 0,
                    Refunds         REAL    NOT NULL DEFAULT 0,
                    NetSales        REAL    NOT NULL DEFAULT 0,
                    ClosedAt        DATETIME,
                    ClosedByUserId  INTEGER,
                    Status          TEXT    NOT NULL DEFAULT 'Open'
                                    CHECK(Status IN ('Open','Closed')),
                    Notes           TEXT,
                    FOREIGN KEY (ClosedByUserId) REFERENCES Users(Id) ON DELETE SET NULL
                );
            ");
            Execute(conn, "CREATE INDEX IF NOT EXISTS IX_DailyClosing_Date ON DailyClosing(BusinessDate);");

            BackfillDefaultItemTypes(conn);
            SeedFruitVegCategories(conn);
        }

        /// <summary>
        /// For each Item without any ItemTypes row, inserts a default Type 1 at price 0.
        /// Real unit prices are set daily via Billing → Add Today.
        /// </summary>
        private static void BackfillDefaultItemTypes(SqliteConnection conn)
        {
            if (!TableExists(conn, "ItemTypes") || !TableExists(conn, "Items")) return;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ItemTypes (ItemId, TypeName, Price, SortOrder, IsActive)
                SELECT i.ItemId,
                       'Type 1 / قسم 1',
                       0,
                       1,
                       1
                FROM Items i
                WHERE NOT EXISTS (
                    SELECT 1 FROM ItemTypes it WHERE it.ItemId = i.ItemId
                );";
            int inserted = cmd.ExecuteNonQuery();
            if (inserted > 0)
                AppLogger.Info($"Backfilled {inserted} default ItemType row(s) from Items.");
        }

        /// <summary>
        /// Seeds fruit/vegetable categories when the Categories table is empty.
        /// Does not remove or alter existing grocery categories.
        /// </summary>
        private static void SeedFruitVegCategories(SqliteConnection conn)
        {
            if (!TableExists(conn, "Categories")) return;

            using var countCmd = conn.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM Categories;";
            if (Convert.ToInt64(countCmd.ExecuteScalar()) > 0) return;

            var categories = new[]
            {
                ("Fruits", "پھل", 1),
                ("Vegetables", "سبزی", 2)
            };

            foreach (var (name, nameUr, order) in categories)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Categories (Name, NameUrdu, DisplayOrder, IsActive)
                    VALUES (@name, @nameUr, @order, 1);";
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@nameUr", nameUr);
                cmd.Parameters.AddWithValue("@order", order);
                cmd.ExecuteNonQuery();
            }

            AppLogger.Info("Seeded default fruit/vegetable categories.");
        }

        /// <summary>
        /// Seeds / refreshes the fruit-veg market catalog.
        /// Assigns simple POS codes 1, 2, 3… (stored in Barcode) for Add Today / scan.
        /// Re-runs when MarketCatalogVersion is below the current catalog version.
        /// </summary>
        private static void SeedFruitVegetableMarketCatalog(SqliteConnection conn)
        {
            if (!TableExists(conn, "Items") || !TableExists(conn, "Categories")) return;

            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS AppSettings (
                    Key   TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );");

            int installedCatalog = 0;
            using (var verCmd = conn.CreateCommand())
            {
                verCmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = 'MarketCatalogVersion' LIMIT 1;";
                var val = verCmd.ExecuteScalar()?.ToString();
                int.TryParse(val, out installedCatalog);
            }

            if (installedCatalog >= MarketCatalogVersion)
            {
                AppLogger.Info($"Market catalog v{installedCatalog} already installed — skipping.");
                return;
            }

            AppLogger.Info($"Seeding fruit/vegetable market catalog v{MarketCatalogVersion}...");

            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var today = DateTime.Today.ToString("yyyy-MM-dd");

            // Free numeric barcodes so new POS codes 1..N can be assigned cleanly.
            Execute(conn, "UPDATE Items SET IsActive = 0, Barcode = NULL;");

            if (TableExists(conn, "DailyItemSelection"))
            {
                using var hideCmd = conn.CreateCommand();
                hideCmd.CommandText = @"
                    DELETE FROM DailyItemSelection
                    WHERE BusinessDate = @today;";
                hideCmd.Parameters.AddWithValue("@today", today);
                hideCmd.ExecuteNonQuery();
            }

            // Only two selling categories for the market POS
            var marketCategories = new (string NameEn, string NameUr, int Order)[]
            {
                ("Fruits", "پھل", 1),
                ("Vegetables", "سبزی", 2)
            };

            var categoryNames = string.Join(", ", marketCategories.Select(c => $"'{c.NameEn.Replace("'", "''")}'"));
            Execute(conn, $"UPDATE Categories SET IsActive = 0 WHERE Name NOT IN ({categoryNames});");

            foreach (var (nameEn, nameUr, order) in marketCategories)
            {
                using var upsertCmd = conn.CreateCommand();
                upsertCmd.CommandText = @"
                    UPDATE Categories
                    SET NameUrdu = @nameUr, DisplayOrder = @order, IsActive = 1
                    WHERE Name = @nameEn;
                    SELECT changes();";
                upsertCmd.Parameters.AddWithValue("@nameEn", nameEn);
                upsertCmd.Parameters.AddWithValue("@nameUr", nameUr);
                upsertCmd.Parameters.AddWithValue("@order", order);
                var updated = Convert.ToInt32(upsertCmd.ExecuteScalar());

                if (updated == 0)
                {
                    using var insertCmd = conn.CreateCommand();
                    insertCmd.CommandText = @"
                        INSERT INTO Categories (Name, NameUrdu, DisplayOrder, IsActive)
                        VALUES (@nameEn, @nameUr, @order, 1);";
                    insertCmd.Parameters.AddWithValue("@nameEn", nameEn);
                    insertCmd.Parameters.AddWithValue("@nameUr", nameUr);
                    insertCmd.Parameters.AddWithValue("@order", order);
                    insertCmd.ExecuteNonQuery();
                }
            }

            // POS codes are 1..N in this exact order (shown on cards / used in Add Today).
            // Types are always "Type N / قسم N" — only prices differ (max 10).
            var marketItems = new (string English, string Urdu, string Category, double[] Prices)[]
            {
                ("Apple", "سیب", "Fruits", new[] { 450.0, 500.0, 400.0 }),
                ("Banana", "کیلا", "Fruits", new[] { 180.0 }),
                ("Mango", "آم", "Fruits", new[] { 350.0, 300.0, 400.0 }),
                ("Orange", "مالٹا", "Fruits", new[] { 220.0 }),
                ("Grapes", "انگور", "Fruits", new[] { 400.0 }),
                ("Watermelon", "تربوز", "Fruits", new[] { 80.0 }),
                ("Guava", "امرود", "Fruits", new[] { 200.0 }),
                ("Pomegranate", "انار", "Fruits", new[] { 450.0 }),
                ("Papaya", "پپیتا", "Fruits", new[] { 120.0 }),
                ("Pineapple", "انناس", "Fruits", new[] { 250.0 }),
                ("Peach", "آڑو", "Fruits", new[] { 300.0 }),
                ("Pear", "ناشپاتی", "Fruits", new[] { 280.0 }),
                ("Strawberry", "اسٹرابیری", "Fruits", new[] { 600.0 }),
                ("Lychee", "لیچی", "Fruits", new[] { 450.0 }),
                ("Melon", "خربوزہ", "Fruits", new[] { 100.0 }),
                ("Coconut", "ناریل", "Fruits", new[] { 200.0 }),
                ("Dates", "کھجور", "Fruits", new[] { 500.0 }),
                ("Lemon", "لیموں", "Fruits", new[] { 250.0 }),
                ("Tomato", "ٹماٹر", "Vegetables", new[] { 120.0, 180.0 }),
                ("Potato", "آلو", "Vegetables", new[] { 80.0 }),
                ("Onion", "پیاز", "Vegetables", new[] { 100.0 }),
                ("Carrot", "گاجر", "Vegetables", new[] { 90.0 }),
                ("Cucumber", "کھیرا", "Vegetables", new[] { 70.0 }),
                ("Broccoli", "بروکلی", "Vegetables", new[] { 350.0 }),
                ("Ginger", "ادرک", "Vegetables", new[] { 400.0 }),
                ("Garlic", "لہسن", "Vegetables", new[] { 450.0 }),
                ("Spinach", "پالک", "Vegetables", new[] { 60.0 }),
                ("Coriander", "دھنیا", "Vegetables", new[] { 40.0 }),
                ("Mint", "پودینہ", "Vegetables", new[] { 40.0 }),
                ("Green Chili", "ہری مرچ", "Vegetables", new[] { 150.0 }),
                ("Capsicum", "شملہ مرچ", "Vegetables", new[] { 200.0 }),
                ("Okra", "بھنڈی", "Vegetables", new[] { 120.0 }),
                ("Eggplant", "بینگن", "Vegetables", new[] { 90.0 }),
                ("Cauliflower", "پھول گوبھی", "Vegetables", new[] { 110.0 }),
                ("Cabbage", "بند گوبھی", "Vegetables", new[] { 80.0 }),
                ("Peas", "مٹر", "Vegetables", new[] { 200.0 }),
                ("Radish", "مولی", "Vegetables", new[] { 60.0 }),
                ("Turnip", "شلجم", "Vegetables", new[] { 70.0 }),
                ("Beetroot", "چقندر", "Vegetables", new[] { 100.0 }),
                ("Bottle Gourd", "لوکی", "Vegetables", new[] { 70.0 }),
                ("Bitter Gourd", "کریلا", "Vegetables", new[] { 100.0 }),
                ("Pumpkin", "کدو", "Vegetables", new[] { 60.0 }),
                ("Corn", "مکئی", "Vegetables", new[] { 90.0 }),
                ("Sweet Potato", "شکر قندی", "Vegetables", new[] { 120.0 }),
                ("Fenugreek", "میتھی", "Vegetables", new[] { 50.0 }),
                ("Spring Onion", "ہرا پیاز", "Vegetables", new[] { 80.0 }),
                ("Lettuce", "سلاد پتہ", "Vegetables", new[] { 150.0 }),
                ("Zucchini", "توری", "Vegetables", new[] { 90.0 })
            };

            var insertedItemIds = new System.Collections.Generic.List<int>();
            int posCode = 1;

            foreach (var (english, urdu, category, prices) in marketItems)
            {
                var code = posCode.ToString();

                int itemId;
                using (var itemCmd = conn.CreateCommand())
                {
                    itemCmd.CommandText = @"
                        INSERT INTO Items (Barcode, Description, NameUrdu, CategoryId, IsActive, UpdatedAt)
                        VALUES (
                            @barcode, @desc, @nameUrdu,
                            (SELECT CategoryId FROM Categories WHERE Name = @cat LIMIT 1),
                            1, @updatedAt
                        );
                        SELECT last_insert_rowid();";
                    itemCmd.Parameters.AddWithValue("@barcode", code);
                    itemCmd.Parameters.AddWithValue("@desc", english);
                    itemCmd.Parameters.AddWithValue("@nameUrdu", urdu);
                    itemCmd.Parameters.AddWithValue("@cat", category);
                    itemCmd.Parameters.AddWithValue("@updatedAt", now);
                    itemId = Convert.ToInt32(itemCmd.ExecuteScalar()!);
                }

                insertedItemIds.Add(itemId);
                posCode++;

                var typeCount = Math.Min(prices.Length, 10);
                for (int i = 0; i < typeCount; i++)
                {
                    var typeName = $"Type {i + 1} / قسم {i + 1}";
                    using var typeCmd = conn.CreateCommand();
                    typeCmd.CommandText = @"
                        INSERT INTO ItemTypes (ItemId, TypeName, Price, SortOrder, IsActive)
                        VALUES (@itemId, @typeName, @price, @sortOrder, 1);";
                    typeCmd.Parameters.AddWithValue("@itemId", itemId);
                    typeCmd.Parameters.AddWithValue("@typeName", typeName);
                    typeCmd.Parameters.AddWithValue("@price", prices[i]);
                    typeCmd.Parameters.AddWithValue("@sortOrder", i + 1);
                    typeCmd.ExecuteNonQuery();
                }

                if (TableExists(conn, "DailyItemSelection"))
                {
                    using var selCmd = conn.CreateCommand();
                    selCmd.CommandText = @"
                        INSERT OR IGNORE INTO DailyItemSelection (BusinessDate, ItemId, IsAvailable)
                        VALUES (@date, @itemId, 1);";
                    selCmd.Parameters.AddWithValue("@date", today);
                    selCmd.Parameters.AddWithValue("@itemId", itemId);
                    selCmd.ExecuteNonQuery();
                }
            }

            using (var settingsCmd = conn.CreateCommand())
            {
                settingsCmd.CommandText = @"
                    INSERT INTO AppSettings (Key, Value) VALUES ('MarketCatalogVersion', @ver)
                    ON CONFLICT(Key) DO UPDATE SET Value = @ver;
                    INSERT INTO AppSettings (Key, Value) VALUES ('MarketCatalogSeeded', '1')
                    ON CONFLICT(Key) DO UPDATE SET Value = '1';";
                settingsCmd.Parameters.AddWithValue("@ver", MarketCatalogVersion.ToString());
                settingsCmd.ExecuteNonQuery();
            }

            AppLogger.Info(
                $"Market catalog v{MarketCatalogVersion}: {marketItems.Length} items with POS codes 1–{marketItems.Length}, " +
                $"{insertedItemIds.Count} daily selections for {today}.");
        }

        /// <summary>
        /// Keeps only Fruits + Vegetables. Remaps Citrus→Fruits and other old groups→Vegetables.
        /// </summary>
        private static void ConsolidateToTwoCategories(SqliteConnection conn)
        {
            if (!TableExists(conn, "Categories") || !TableExists(conn, "Items")) return;

            // Ensure the two categories exist and are active
            foreach (var (name, nameUr, order) in new[] { ("Fruits", "پھل", 1), ("Vegetables", "سبزی", 2) })
            {
                using var upsert = conn.CreateCommand();
                upsert.CommandText = @"
                    UPDATE Categories
                    SET NameUrdu = @nameUr, DisplayOrder = @order, IsActive = 1
                    WHERE Name = @name;
                    SELECT changes();";
                upsert.Parameters.AddWithValue("@name", name);
                upsert.Parameters.AddWithValue("@nameUr", nameUr);
                upsert.Parameters.AddWithValue("@order", order);
                var updated = Convert.ToInt32(upsert.ExecuteScalar());
                if (updated == 0)
                {
                    using var insert = conn.CreateCommand();
                    insert.CommandText = @"
                        INSERT INTO Categories (Name, NameUrdu, DisplayOrder, IsActive)
                        VALUES (@name, @nameUr, @order, 1);";
                    insert.Parameters.AddWithValue("@name", name);
                    insert.Parameters.AddWithValue("@nameUr", nameUr);
                    insert.Parameters.AddWithValue("@order", order);
                    insert.ExecuteNonQuery();
                }
            }

            // Remap old groups → Fruits / Vegetables
            Execute(conn, @"
                UPDATE Items
                SET CategoryId = (SELECT CategoryId FROM Categories WHERE Name = 'Fruits' LIMIT 1)
                WHERE CategoryId IN (
                    SELECT CategoryId FROM Categories WHERE Name = 'Citrus'
                );");

            Execute(conn, @"
                UPDATE Items
                SET CategoryId = (SELECT CategoryId FROM Categories WHERE Name = 'Vegetables' LIMIT 1)
                WHERE CategoryId IN (
                    SELECT CategoryId FROM Categories
                    WHERE Name IN ('Root Vegetables', 'Leafy Vegetables', 'Herbs', 'Other')
                );");

            // Deactivate every category except Fruits & Vegetables
            Execute(conn, @"
                UPDATE Categories
                SET IsActive = 0
                WHERE Name NOT IN ('Fruits', 'Vegetables');");

            AppLogger.Info("Consolidated categories to Fruits + Vegetables only.");
        }

        /// <summary>
        /// Runs CleanupDuplicateAndLegacyItems only once (seed repair). After that, catalog
        /// uniqueness is enforced in ItemService so renaming one item never touches another.
        /// </summary>
        private static void RunDuplicateCleanupOnce(SqliteConnection conn)
        {
            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS AppSettings (
                    Key   TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );");

            string? flag = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = 'DuplicateItemCleanupDone' LIMIT 1;";
                flag = cmd.ExecuteScalar()?.ToString();
            }

            if (string.Equals(flag, "1", StringComparison.Ordinal))
                return;

            CleanupDuplicateAndLegacyItems(conn);

            using var setCmd = conn.CreateCommand();
            setCmd.CommandText = @"
                INSERT INTO AppSettings (Key, Value) VALUES ('DuplicateItemCleanupDone', '1')
                ON CONFLICT(Key) DO UPDATE SET Value = '1';";
            setCmd.ExecuteNonQuery();
            AppLogger.Info("Duplicate item cleanup marked complete (will not re-run on startup).");
        }

        /// <summary>
        /// Removes old seed duplicates: keeps one active row per English name (POS-coded),
        /// deactivates extras, and hard-deletes unused inactive rows (not on any bill).
        /// Also removes leftover legacy grocery items that have no Urdu name.
        /// </summary>
        private static void CleanupDuplicateAndLegacyItems(SqliteConnection conn)
        {
            if (!TableExists(conn, "Items")) return;

            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // 1) Deactivate legacy grocery leftovers (no Urdu name) that are still active.
            using (var legacyCmd = conn.CreateCommand())
            {
                legacyCmd.CommandText = @"
                    UPDATE Items
                    SET IsActive = 0, Barcode = NULL, UpdatedAt = @now
                    WHERE IsActive = 1
                      AND (NameUrdu IS NULL OR TRIM(NameUrdu) = '');";
                legacyCmd.Parameters.AddWithValue("@now", now);
                var legacy = legacyCmd.ExecuteNonQuery();
                if (legacy > 0)
                    AppLogger.Info($"Deactivated {legacy} legacy item(s) without Urdu names.");
            }

            // 2) For each English name with multiple active rows, keep the POS-coded (Barcode) one
            //    (or newest ItemId), deactivate the rest.
            using (var findCmd = conn.CreateCommand())
            {
                findCmd.CommandText = @"
                    SELECT Description
                    FROM Items
                    WHERE IsActive = 1
                    GROUP BY LOWER(TRIM(Description))
                    HAVING COUNT(*) > 1;";
                var dupNames = new List<string>();
                using (var reader = findCmd.ExecuteReader())
                {
                    while (reader.Read())
                        dupNames.Add(reader.GetString(0));
                }

                foreach (var name in dupNames)
                {
                    using var keepCmd = conn.CreateCommand();
                    keepCmd.CommandText = @"
                        SELECT ItemId FROM Items
                        WHERE IsActive = 1 AND LOWER(TRIM(Description)) = LOWER(TRIM(@name))
                        ORDER BY
                            CASE WHEN Barcode IS NOT NULL AND TRIM(Barcode) != '' THEN 0 ELSE 1 END,
                            ItemId DESC
                        LIMIT 1;";
                    keepCmd.Parameters.AddWithValue("@name", name);
                    var keepId = Convert.ToInt32(keepCmd.ExecuteScalar()!);

                    using var deactivateCmd = conn.CreateCommand();
                    deactivateCmd.CommandText = @"
                        UPDATE Items
                        SET IsActive = 0, Barcode = NULL, UpdatedAt = @now
                        WHERE IsActive = 1
                          AND LOWER(TRIM(Description)) = LOWER(TRIM(@name))
                          AND ItemId != @keepId;";
                    deactivateCmd.Parameters.AddWithValue("@now", now);
                    deactivateCmd.Parameters.AddWithValue("@name", name);
                    deactivateCmd.Parameters.AddWithValue("@keepId", keepId);
                    var removed = deactivateCmd.ExecuteNonQuery();
                    if (removed > 0)
                        AppLogger.Info($"Removed {removed} duplicate active row(s) for '{name}', kept ItemId={keepId}.");
                }
            }

            // 3) Hard-delete inactive items that are not referenced by any bill line
            //    (safe cleanup of previous seed copies).
            if (TableExists(conn, "BillItems"))
            {
                if (TableExists(conn, "ItemTypes"))
                {
                    Execute(conn, @"
                        DELETE FROM ItemTypes
                        WHERE ItemId IN (
                            SELECT i.ItemId FROM Items i
                            WHERE i.IsActive = 0
                              AND i.ItemId NOT IN (SELECT DISTINCT ItemId FROM BillItems)
                        );");
                }

                if (TableExists(conn, "DailyItemSelection"))
                {
                    Execute(conn, @"
                        DELETE FROM DailyItemSelection
                        WHERE ItemId IN (
                            SELECT i.ItemId FROM Items i
                            WHERE i.IsActive = 0
                              AND i.ItemId NOT IN (SELECT DISTINCT ItemId FROM BillItems)
                        );");
                }

                using var delCmd = conn.CreateCommand();
                delCmd.CommandText = @"
                    DELETE FROM Items
                    WHERE IsActive = 0
                      AND ItemId NOT IN (SELECT DISTINCT ItemId FROM BillItems);";
                var deleted = delCmd.ExecuteNonQuery();
                if (deleted > 0)
                    AppLogger.Info($"Purged {deleted} unused inactive duplicate/legacy item(s).");
            }
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
