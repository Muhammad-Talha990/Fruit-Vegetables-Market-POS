-- ============================================================================
-- GroceryPOS Transactional Data Reset Script (SQLite)
-- ============================================================================
-- Purpose: Clear transactional/operational history while preserving:
--   ✓ Products (Items table)
--   ✓ Customers table
--   ✓ Payment Accounts (Accounts table)
--   ✓ System Users (Users table)
--   ✓ Categories (Categories table)
--   ✗ Database schema/structure
--
-- This script maintains referential integrity throughout deletion process.
-- ============================================================================

-- Step 1: Enable foreign key enforcement
PRAGMA foreign_keys = ON;

-- Step 2: Disable foreign key constraints temporarily for safe deletion
PRAGMA foreign_keys = OFF;

-- ============================================================================
-- DELETE TRANSACTIONAL DATA IN CORRECT ORDER
-- ============================================================================

-- Clear return item details first (depends on BillItems and BillReturns)
DELETE FROM BillReturnItems;

-- Clear return headers (depends on Bills)
DELETE FROM BillReturns;

-- Clear payment transaction log (depends on Bills)
DELETE FROM bill_payment;

-- Clear bill line items (depends on Bills - CASCADE will handle, but explicit delete is safer)
DELETE FROM BillItems;

-- Clear bill headers (main transactional records)
DELETE FROM Bills;

-- Clear inventory movement history (audit trail for transactions)
DELETE FROM InventoryLogs;

-- Clear customer ledger entries (accounting history tied to transactions)
DELETE FROM CustomerLedger;

-- ============================================================================
-- VERIFY MASTER DATA IS INTACT (Disabled - Uncomment for verification)
-- ============================================================================
-- SELECT '=== Verification ===' as Status;
-- SELECT COUNT(*) as ItemCount FROM Items;
-- SELECT COUNT(*) as CustomerCount FROM Customers;
-- SELECT COUNT(*) as AccountCount FROM Accounts;
-- SELECT COUNT(*) as CategoryCount FROM Categories;
-- SELECT COUNT(*) as UserCount FROM Users;

-- ============================================================================
-- RE-ENABLE FOREIGN KEY ENFORCEMENT
-- ============================================================================
PRAGMA foreign_keys = ON;

-- ============================================================================
-- CLEANUP & OPTIMIZATION
-- ============================================================================

-- Vacuum the database to reclaim space
VACUUM;

-- Reset AUTOINCREMENT sequences (optional - uncomment if needed)
-- DELETE FROM sqlite_sequence WHERE name IN ('Bills', 'BillItems', 'bill_payment', 'BillReturns', 'BillReturnItems', 'InventoryLogs', 'CustomerLedger');
-- UPDATE sqlite_sequence SET seq = 0 WHERE name IN ('Bills', 'BillItems', 'bill_payment', 'BillReturns', 'BillReturnItems', 'InventoryLogs', 'CustomerLedger');

-- ============================================================================
-- SUCCESS SUMMARY
-- ============================================================================
-- All transactional data has been cleared:
--   ✓ Bills (and cascaded BillItems)
--   ✓ bill_payment records
--   ✓ BillReturns & BillReturnItems
--   ✓ InventoryLogs (stock movement history)
--   ✓ CustomerLedger (transaction history)
--
-- Preserved master data:
--   ✓ Items (Product catalog with prices and stock thresholds)
--   ✓ Customers (Customer registry)
--   ✓ Accounts (Payment methods/bank accounts)
--   ✓ Users (System users - Admin/Cashier)
--   ✓ Categories (Product categories)
--
-- System is now in "clean slate" state for testing with existing master data.
-- ============================================================================
