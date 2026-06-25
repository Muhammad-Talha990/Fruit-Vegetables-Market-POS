-- ============================================================================
-- GroceryPOS Professional Database Reset Script (SQLite)
-- Version: 2.0 (Enhanced with AUTOINCREMENT reset and verification)
-- ============================================================================
-- Purpose: 
--   Reset database to "fresh installation" state by clearing all transactional
--   data while preserving master data (products, customers, users, accounts).
--
-- What Gets DELETED (Transactional Data):
--   ✗ Bills (sales invoices)
--   ✗ BillItems (line items from bills)
--   ✗ bill_payment (payment transactions)
--   ✗ BillReturns (return headers)
--   ✗ BillReturnItems (returned items)
--   ✗ InventoryLogs (stock movement audit trail)
--   ✗ CustomerLedger (customer accounting entries)
--
-- What's PRESERVED (Master Data):
--   ✓ Items (product catalog - prices, stock thresholds)
--   ✓ Categories (product categories)
--   ✓ Customers (customer registry)
--   ✓ Users (system users)
--   ✓ Accounts (payment methods/bank accounts)
--
-- Safety Features:
--   • Maintains referential integrity throughout
--   • Resets AUTOINCREMENT sequences
--   • Verifies success with SELECT statements
--   • Vacuum optimizes storage
-- ============================================================================

-- Step 0: Enable Safety Features
-- ============================================================================
PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = ON;

-- Step 1: Verify System is Ready (informational)
-- ============================================================================
-- SELECT 'PRE-RESET SYSTEM STATE' as Info;
-- SELECT COUNT(*) as BillCount FROM Bills;
-- SELECT COUNT(*) as BillItemCount FROM BillItems;
-- SELECT COUNT(*) as ReturnCount FROM BillReturns;
-- SELECT COUNT(*) as PaymentCount FROM bill_payment;
-- SELECT COUNT(*) as InventoryLogCount FROM InventoryLogs;

-- Step 2: Begin Transaction
-- ============================================================================
BEGIN IMMEDIATE TRANSACTION;

-- Step 3: Disable Foreign Keys Temporarily (for safe deletion order)
-- ============================================================================
PRAGMA foreign_keys = OFF;

-- Step 4: DELETE TRANSACTIONAL DATA IN DEPENDENCY ORDER
-- ============================================================================
-- Order is critical: delete detail records first, then headers

-- Delete return item details (most dependent)
DELETE FROM BillReturnItems
WHERE ReturnId IN (
    SELECT ReturnId FROM BillReturns
    WHERE BillId IS NOT NULL
);

-- Delete return headers
DELETE FROM BillReturns;

-- Delete payment transaction log (depends on Bills)
DELETE FROM bill_payment;

-- Delete bill line items (depends on Bills)
DELETE FROM BillItems;

-- Delete bill headers (main transactional records)
DELETE FROM Bills;

-- Delete inventory movement history (audit trail)
DELETE FROM InventoryLogs;

-- Delete customer ledger entries (accounting history)
DELETE FROM CustomerLedger;

-- Step 5: RESET AUTOINCREMENT SEQUENCES
-- ============================================================================
-- This ensures new records start from ID 1
-- SQLite uses sqlite_sequence table to track AUTOINCREMENT values

DELETE FROM sqlite_sequence 
WHERE name IN (
    'Bills',
    'BillItems',
    'BillReturns',
    'BillReturnItems',
    'bill_payment',
    'InventoryLogs',
    'CustomerLedger'
);

-- Verify AUTOINCREMENT table was updated
-- SELECT 'AUTOINCREMENT Reset Status' as Info;
-- SELECT COALESCE(name, 'NOT FOUND') as SequenceName, COALESCE(seq, 0) as NextId 
-- FROM sqlite_sequence 
-- WHERE name IN ('Bills', 'BillItems', 'BillReturns', 'BillReturnItems', 'bill_payment', 'InventoryLogs', 'CustomerLedger');

-- Step 6: RE-ENABLE FOREIGN KEY ENFORCEMENT
-- ============================================================================
PRAGMA foreign_keys = ON;

-- Step 7: COMMIT TRANSACTION
-- ============================================================================
COMMIT;

-- Step 8: CLEANUP AND OPTIMIZATION
-- ============================================================================

-- Reclaim disk space (important after bulk deletes)
VACUUM;

-- Reindex to maintain performance
REINDEX;

-- Rebuild statistics for query optimizer
ANALYZE;

-- Step 9: POST-RESET VERIFICATION
-- ============================================================================
-- Verify all transactional tables are empty
SELECT 
    'POST-RESET VERIFICATION' as Status,
    'Transactional Data' as Category;

SELECT 'Bills' as TableName, COUNT(*) as RecordCount FROM Bills
UNION ALL
SELECT 'BillItems', COUNT(*) FROM BillItems
UNION ALL
SELECT 'bill_payment', COUNT(*) FROM bill_payment
UNION ALL
SELECT 'BillReturns', COUNT(*) FROM BillReturns
UNION ALL
SELECT 'BillReturnItems', COUNT(*) FROM BillReturnItems
UNION ALL
SELECT 'InventoryLogs', COUNT(*) FROM InventoryLogs
UNION ALL
SELECT 'CustomerLedger', COUNT(*) FROM CustomerLedger;

-- Verify master data is intact
SELECT 'MASTER DATA STATUS' as Status;

SELECT 'Items' as TableName, COUNT(*) as RecordCount FROM Items
UNION ALL
SELECT 'Categories', COUNT(*) FROM Categories
UNION ALL
SELECT 'Customers', COUNT(*) FROM Customers
UNION ALL
SELECT 'Users', COUNT(*) FROM Users
UNION ALL
SELECT 'Accounts', COUNT(*) FROM Accounts;

-- Verify AUTOINCREMENT sequences are reset
SELECT 
    'AUTOINCREMENT SEQUENCES RESET' as Status,
    COALESCE(name, 'EMPTY') as SequenceName,
    COALESCE(seq, 0) as NextId
FROM sqlite_sequence;

-- ============================================================================
-- SUCCESS SUMMARY
-- ============================================================================
-- All transactional data has been cleared and reset to fresh state:
--
--   ✓ Bills and BillItems - DELETED
--   ✓ Payments (bill_payment) - DELETED  
--   ✓ Returns (BillReturns & BillReturnItems) - DELETED
--   ✓ InventoryLogs (stock audit trail) - DELETED
--   ✓ CustomerLedger (accounting entries) - DELETED
--   ✓ AUTOINCREMENT sequences - RESET (next ID = 1)
--   ✓ Referential Integrity - MAINTAINED
--   ✓ Database Performance - OPTIMIZED (VACUUM, ANALYZE)
--
-- Preserved:
--   ✓ Items (Product catalog - {ItemCount} products)
--   ✓ Categories (Product categories)
--   ✓ Customers (Customer registry)
--   ✓ Users (System users)
--   ✓ Accounts (Payment methods)
--   ✓ Database Schema (All tables and indexes intact)
--
-- SYSTEM IS NOW IN FRESH INSTALLATION STATE
-- Ready for testing with existing master data intact.
--
-- Next Steps:
--   1. Build and run: dotnet build && dotnet run --project GroceryPOS.csproj
--   2. Verify dashboard shows zero transactions
--   3. Test billing, returns, and reporting functionality
-- ============================================================================
