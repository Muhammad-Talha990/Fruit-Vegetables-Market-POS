# GroceryPOS — Database Design Document

> **Version:** 2.0 (Normalized 3NF)
> **Engine:** SQLite 3 (via Microsoft.Data.Sqlite)
> **Application:** C# WPF Desktop POS System

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Entity Relationship Diagram (ERD)](#2-entity-relationship-diagram-erd)
3. [Normalized Schema (3NF)](#3-normalized-schema-3nf)
4. [SQLite CREATE TABLE Statements](#4-sqlite-create-table-statements)
5. [Foreign Key Relationships](#5-foreign-key-relationships)
6. [Indexes](#6-indexes)
7. [Sample Data](#7-sample-data)
8. [SQL Queries — Bill Calculation](#8-sql-queries--bill-calculation)
9. [SQL Queries — Payment Tracking](#9-sql-queries--payment-tracking)
10. [SQL Queries — Return Handling](#10-sql-queries--return-handling)
11. [Example: Sale Transaction](#11-example-sale-transaction)
12. [Example: Return Transaction](#12-example-return-transaction)
13. [Example: Payment Transaction](#13-example-payment-transaction)
14. [Inventory Update Logic](#14-inventory-update-logic)
15. [Migration Strategy](#15-migration-strategy)
16. [Application Logic Guidance](#16-application-logic-guidance)
17. [Normalization Analysis](#17-normalization-analysis)
18. [Receipt Printing Logic](#18-receipt-printing-logic)

---

## 1. Executive Summary

### Problems Found in the Original Schema

| # | Issue | Severity | Fix Applied |
|---|-------|----------|-------------|
| 1 | `BillItems` used composite PK `(BillId, ItemId)` — code expected `BillItemId AUTOINCREMENT` | **Critical** | Surrogate PK added |
| 2 | `Payments.Method` column — code expected `PaymentMethod` + `TransactionType` | **Critical** | Columns renamed, TransactionType added |
| 3 | `BillReturnItems` table referenced in code but **never created** | **Critical** | Table created |
| 4 | `Bills` missing `IsPrinted`, `PrintedAt`, `PrintAttempts` columns — code tried to UPDATE them | **High** | Columns added |
| 5 | `BillReturns` had flat `ItemId`/`Quantity` per row — code expected header/detail pattern | **High** | Migrated to BillReturns + BillReturnItems |
| 6 | `StockService` executed `UPDATE Items SET StockQuantity` — column doesn't exist (stock is calculated from InventoryLogs) | **High** | UPDATE removed; stock is purely calculated |
| 7 | SQL queries used `i.Id` but Items column is `ItemId` | **Medium** | All references fixed |
| 8 | `InventoryLogs` column referenced as `CreatedAt` in some queries but schema used `LogDate` | **Medium** | All references fixed |
| 9 | Returns created new bills — violates business requirement | **Design** | Returns now use BillReturns table only |
| 10 | No audit trail for who processed returns | **Design** | `UserId` added to BillReturns |

### Design Principles

- **Immutable Bills**: Once saved, bill headers and line items are never modified
- **Transaction Ledger**: All payment/refund actions are logged in the Payments table
- **Calculated Aggregates**: Stock, totals, and balances are always calculated from source tables — never stored redundantly
- **Normalization**: Schema follows 1NF, 2NF, and 3NF (see [Section 17](#17-normalization-analysis))

---

## 2. Entity Relationship Diagram (ERD)

```
┌──────────────┐         ┌──────────────┐
│   Users      │         │  Categories  │
├──────────────┤         ├──────────────┤
│ PK Id        │         │ PK CategoryId│
│    Username  │         │    Name      │
│    Password  │         └──────┬───────┘
│    FullName  │                │
│    Role      │                │ 1
│    IsActive  │                │
└──────┬───────┘                │
       │                        │
       │ 1                      │
       │              ┌─────────┴──────────┐
       │              │      Items         │
       │              ├────────────────────┤
       │              │ PK ItemId          │
       │              │ UQ Barcode (opt)   │
       │              │    Description     │
       │              │    CostPrice       │
       │              │    SalePrice       │
       │              │ FK CategoryId ─────┤
       │              │    MinStockThreshold│
       │              └────────┬───────────┘
       │                       │
       │                       │ 1
       │                       │
       │    ┌──────────────────┼──────────────────┐
       │    │                  │                   │
       │    │ *                │ *                 │ *
       │    │                  │                   │
       │  ┌─┴──────────┐  ┌───┴────────┐  ┌──────┴────────┐
       │  │ BillItems   │  │InventoryLogs│  │BillReturnItems│
       │  ├─────────────┤  ├────────────┤  ├───────────────┤
       │  │PK BillItemId│  │PK LogId    │  │PK ReturnItemId│
       │  │FK BillId    │  │FK ItemId   │  │FK ReturnId    │
       │  │FK ItemId    │  │  QtyChange │  │FK BillItemId  │
       │  │  Quantity   │  │  ChangeType│  │  Quantity     │
       │  │  UnitPrice  │  │  LogDate   │  │  UnitPrice    │
       │  │  Discount   │  └────────────┘  └───────┬───────┘
       │  └─────┬───────┘                          │
       │        │                                  │ *
       │        │ *                                │
       │        │                          ┌───────┴───────┐
       │  ┌─────┴───────────┐              │  BillReturns  │
       │  │     Bills       │              ├───────────────┤
       │  ├─────────────────┤              │PK ReturnId    │
       │  │ PK BillId       │◄─────────────│FK BillId      │
       │  │ FK CustomerId   │   1      *   │FK UserId      │
       │  │ FK UserId ──────┼──────────┐   │  RefundAmount │
       └──┤    TaxAmount    │          │   │  ReturnedAt   │
          │    Discount     │          │   └───────────────┘
          │    Status       │          │
          │    IsPrinted    │          │
          │    CreatedAt    │          │
          └─────┬───────────┘          │
                │                      │
                │ *                    │
                │                      │
          ┌─────┴───────────┐          │
          │   Payments      │          │
          ├─────────────────┤          │
          │ PK PaymentId    │          │
          │ FK BillId       │          │
          │    Amount       │          │
          │    PaymentMethod│          │
          │    TransType    │          │
          │    PaidAt       │          │
          └─────────────────┘          │
                                       │
          ┌─────────────────┐          │
          │   Customers     │          │
          ├─────────────────┤          │
          │ PK CustomerId   │──────────┘
          │    FullName     │   1      *
          │ UQ Phone        │
          │    Address      │
          │    IsActive     │
          └─────────────────┘
```

### Cardinality Summary

| Relationship | Type | Description |
|---|---|---|
| Categories → Items | 1:N | One category has many items |
| Users → Bills | 1:N | One cashier creates many bills |
| Customers → Bills | 1:N | One customer can have many bills (nullable for walk-in) |
| Bills → BillItems | 1:N | One bill has many line items |
| Items → BillItems | 1:N | One item can appear on many bills |
| Bills → Payments | 1:N | One bill can have multiple payments |
| Bills → BillReturns | 1:N | One bill can have multiple return transactions |
| BillReturns → BillReturnItems | 1:N | One return has many returned items |
| BillItems → BillReturnItems | 1:N | One bill item can be partially returned multiple times |
| Items → InventoryLogs | 1:N | One item has many stock movements |
| Users → BillReturns | 1:N | One user can process many returns |

---

## 3. Normalized Schema (3NF)

### Table 1: Users

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | INTEGER | PK, AUTOINCREMENT | Surrogate key |
| Username | TEXT | NOT NULL, UNIQUE | Login identifier |
| PasswordHash | TEXT | NOT NULL | BCrypt hash |
| FullName | TEXT | NOT NULL | Display name |
| Role | TEXT | NOT NULL, DEFAULT 'Cashier', CHECK('Admin','Cashier') | Access level |
| IsActive | INTEGER | NOT NULL, DEFAULT 1 | Soft-delete flag |
| CreatedAt | DATETIME | DEFAULT CURRENT_TIMESTAMP | Audit timestamp |

### Table 2: Categories

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| CategoryId | INTEGER | PK, AUTOINCREMENT | Surrogate key |
| Name | TEXT | NOT NULL, UNIQUE | Category name |

### Table 3: Items

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| ItemId | INTEGER | PK, AUTOINCREMENT | **Primary key** (NOT barcode) |
| Barcode | TEXT | UNIQUE (nullable) | **Optional**, unique if provided |
| Description | TEXT | NOT NULL | Product name |
| CostPrice | REAL | NOT NULL, CHECK >= 0 | Purchase cost |
| SalePrice | REAL | NOT NULL, CHECK >= 0 | Selling price |
| CategoryId | INTEGER | FK → Categories, ON DELETE SET NULL | Category link |
| MinStockThreshold | REAL | NOT NULL, DEFAULT 10 | Low-stock alert level |
| CreatedAt | DATETIME | DEFAULT CURRENT_TIMESTAMP | Audit timestamp |

> **Note:** Stock quantity is NOT stored here. It is always calculated as `SUM(InventoryLogs.QuantityChange) WHERE ItemId = ?`

### Table 4: Customers

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| CustomerId | INTEGER | PK, AUTOINCREMENT | Surrogate key |
| FullName | TEXT | NOT NULL | Customer name |
| Phone | TEXT | UNIQUE, NOT NULL | Primary phone |
| Address | TEXT | nullable | Delivery/billing address |
| IsActive | INTEGER | NOT NULL, DEFAULT 1 | Soft-delete flag |
| CreatedAt | DATETIME | DEFAULT CURRENT_TIMESTAMP | Audit timestamp |

### Table 5: Bills (IMMUTABLE)

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| BillId | INTEGER | PK, AUTOINCREMENT | **Immutable** bill identifier |
| CustomerId | INTEGER | FK → Customers, nullable | NULL = walk-in customer |
| UserId | INTEGER | FK → Users | Cashier who created bill |
| TaxAmount | REAL | DEFAULT 0 | Flat tax applied |
| DiscountAmount | REAL | DEFAULT 0 | Flat discount applied |
| Status | TEXT | DEFAULT 'Completed', CHECK('Completed','Cancelled') | Bill status |
| IsPrinted | INTEGER | DEFAULT 0 | Print tracking |
| PrintedAt | DATETIME | nullable | When printed |
| PrintAttempts | INTEGER | DEFAULT 0 | Print retry count |
| CreatedAt | DATETIME | DEFAULT CURRENT_TIMESTAMP | Bill creation time |

### Table 6: BillItems (IMMUTABLE)

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| BillItemId | INTEGER | PK, AUTOINCREMENT | **Surrogate key** (not composite) |
| BillId | INTEGER | FK → Bills, NOT NULL | Parent bill |
| ItemId | INTEGER | FK → Items, NOT NULL | Product reference |
| Quantity | REAL | NOT NULL, CHECK > 0 | Quantity sold |
| UnitPrice | REAL | NOT NULL, CHECK >= 0 | **Frozen** price at sale time |
| DiscountAmount | REAL | DEFAULT 0 | Per-line discount |

### Table 7: Payments

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| PaymentId | INTEGER | PK, AUTOINCREMENT | Surrogate key |
| BillId | INTEGER | FK → Bills, NOT NULL | Associated bill |
| Amount | REAL | NOT NULL | Payment amount (can be negative for refunds) |
| PaymentMethod | TEXT | NOT NULL, DEFAULT 'Cash', CHECK('Cash','Card','Credit') | How paid |
| TransactionType | TEXT | NOT NULL, DEFAULT 'Sale', CHECK('Sale','Credit Payment','Refund') | Why paid |
| Note | TEXT | nullable | Cashier note |
| PaidAt | DATETIME | DEFAULT CURRENT_TIMESTAMP | Payment timestamp |

### Table 8: BillReturns (Return Header)

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| ReturnId | INTEGER | PK, AUTOINCREMENT | Surrogate key |
| BillId | INTEGER | FK → Bills, NOT NULL | **Original** bill being returned against |
| UserId | INTEGER | FK → Users, nullable | Who processed the return |
| RefundAmount | REAL | NOT NULL | Cash refund amount for this return |
| ReturnedAt | DATETIME | DEFAULT CURRENT_TIMESTAMP | Return timestamp |

### Table 9: BillReturnItems (Return Detail)

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| ReturnItemId | INTEGER | PK, AUTOINCREMENT | Surrogate key |
| ReturnId | INTEGER | FK → BillReturns, NOT NULL | Parent return header |
| BillItemId | INTEGER | FK → BillItems, NOT NULL | **Original** bill line item |
| Quantity | REAL | NOT NULL, CHECK > 0 | Quantity being returned |
| UnitPrice | REAL | NOT NULL, CHECK >= 0 | Unit price at time of return |

### Table 10: InventoryLogs (Stock Audit Trail)

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| LogId | INTEGER | PK, AUTOINCREMENT | Surrogate key |
| ItemId | INTEGER | FK → Items, NOT NULL | Product reference |
| QuantityChange | REAL | NOT NULL | Positive = in, Negative = out |
| ChangeType | TEXT | NOT NULL, CHECK('Sale','Return','Purchase','Adjustment') | Movement type |
| ReferenceId | INTEGER | nullable | BillId, ReturnId, or SupplyId |
| ReferenceType | TEXT | CHECK('Bill','Return','Supply') or NULL | Reference type |
| LogDate | DATETIME | DEFAULT CURRENT_TIMESTAMP | Movement timestamp |

---

## 4. SQLite CREATE TABLE Statements

```sql
-- ═══════════════════════════════════════════════
--  GroceryPOS Normalized Schema v2.0 (3NF)
-- ═══════════════════════════════════════════════

PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = ON;

-- 1. Users
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

-- 2. Categories
CREATE TABLE IF NOT EXISTS Categories (
    CategoryId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name       TEXT    NOT NULL UNIQUE
);

-- 3. Items
CREATE TABLE IF NOT EXISTS Items (
    ItemId            INTEGER PRIMARY KEY AUTOINCREMENT,
    Barcode           TEXT    UNIQUE,          -- Optional, unique if provided
    Description       TEXT    NOT NULL,
    CostPrice         REAL    NOT NULL CHECK(CostPrice >= 0),
    SalePrice         REAL    NOT NULL CHECK(SalePrice >= 0),
    CategoryId        INTEGER,
    MinStockThreshold REAL    NOT NULL DEFAULT 10,
    CreatedAt         DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (CategoryId) REFERENCES Categories(CategoryId)
        ON DELETE SET NULL
);

-- 4. Customers
CREATE TABLE IF NOT EXISTS Customers (
    CustomerId INTEGER PRIMARY KEY AUTOINCREMENT,
    FullName   TEXT    NOT NULL,
    Phone      TEXT    UNIQUE NOT NULL,
    Address    TEXT,
    IsActive   INTEGER NOT NULL DEFAULT 1,
    CreatedAt  DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 5. Bills (IMMUTABLE after creation)
CREATE TABLE IF NOT EXISTS Bills (
    BillId         INTEGER PRIMARY KEY AUTOINCREMENT,
    CustomerId     INTEGER,
    UserId         INTEGER,
    TaxAmount      REAL    DEFAULT 0,
    DiscountAmount REAL    DEFAULT 0,
    Status         TEXT    DEFAULT 'Completed'
                   CHECK(Status IN ('Completed', 'Cancelled')),
    IsPrinted      INTEGER DEFAULT 0,
    PrintedAt      DATETIME,
    PrintAttempts  INTEGER DEFAULT 0,
    CreatedAt      DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (CustomerId) REFERENCES Customers(CustomerId)
        ON DELETE RESTRICT,
    FOREIGN KEY (UserId) REFERENCES Users(Id)
        ON DELETE SET NULL
);

-- 6. BillItems (IMMUTABLE line items with surrogate PK)
CREATE TABLE IF NOT EXISTS BillItems (
    BillItemId     INTEGER PRIMARY KEY AUTOINCREMENT,
    BillId         INTEGER NOT NULL,
    ItemId         INTEGER NOT NULL,
    Quantity       REAL    NOT NULL CHECK(Quantity > 0),
    UnitPrice      REAL    NOT NULL CHECK(UnitPrice >= 0),
    DiscountAmount REAL    DEFAULT 0,
    FOREIGN KEY (BillId) REFERENCES Bills(BillId) ON DELETE CASCADE,
    FOREIGN KEY (ItemId) REFERENCES Items(ItemId) ON DELETE RESTRICT
);

-- 7. Payments
CREATE TABLE IF NOT EXISTS Payments (
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

-- 8. BillReturns (Return Header)
CREATE TABLE IF NOT EXISTS BillReturns (
    ReturnId    INTEGER PRIMARY KEY AUTOINCREMENT,
    BillId      INTEGER NOT NULL,
    UserId      INTEGER,
    RefundAmount REAL   NOT NULL,
    ReturnedAt  DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (BillId) REFERENCES Bills(BillId) ON DELETE CASCADE,
    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE SET NULL
);

-- 9. BillReturnItems (Return Detail)
CREATE TABLE IF NOT EXISTS BillReturnItems (
    ReturnItemId INTEGER PRIMARY KEY AUTOINCREMENT,
    ReturnId     INTEGER NOT NULL,
    BillItemId   INTEGER NOT NULL,
    Quantity     REAL    NOT NULL CHECK(Quantity > 0),
    UnitPrice    REAL    NOT NULL CHECK(UnitPrice >= 0),
    FOREIGN KEY (ReturnId)   REFERENCES BillReturns(ReturnId) ON DELETE CASCADE,
    FOREIGN KEY (BillItemId) REFERENCES BillItems(BillItemId) ON DELETE RESTRICT
);

-- 10. InventoryLogs (Stock Audit Trail)
CREATE TABLE IF NOT EXISTS InventoryLogs (
    LogId          INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId         INTEGER NOT NULL,
    QuantityChange REAL    NOT NULL,
    ChangeType     TEXT    NOT NULL
                   CHECK(ChangeType IN ('Sale', 'Return', 'Purchase', 'Adjustment')),
    ReferenceId    INTEGER,
    ReferenceType  TEXT CHECK(ReferenceType IN ('Bill', 'Return', 'Supply') OR ReferenceType IS NULL),
    LogDate        DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (ItemId) REFERENCES Items(ItemId) ON DELETE CASCADE
);
```

---

## 5. Foreign Key Relationships

| Child Table | Child Column | Parent Table | Parent Column | ON DELETE | Rationale |
|---|---|---|---|---|---|
| Items | CategoryId | Categories | CategoryId | SET NULL | Keep item if category deleted |
| Bills | CustomerId | Customers | CustomerId | RESTRICT | Prevent deleting customer with bills |
| Bills | UserId | Users | Id | SET NULL | Keep bill if user deactivated |
| BillItems | BillId | Bills | BillId | CASCADE | Delete line items with bill |
| BillItems | ItemId | Items | ItemId | RESTRICT | Prevent deleting item with bill history |
| Payments | BillId | Bills | BillId | CASCADE | Delete payments with bill |
| BillReturns | BillId | Bills | BillId | CASCADE | Delete returns with bill |
| BillReturns | UserId | Users | Id | SET NULL | Keep return if user deactivated |
| BillReturnItems | ReturnId | BillReturns | ReturnId | CASCADE | Delete return items with return |
| BillReturnItems | BillItemId | BillItems | BillItemId | RESTRICT | Preserve return integrity |
| InventoryLogs | ItemId | Items | ItemId | CASCADE | Remove logs if item purged |

---

## 6. Indexes

```sql
-- Items: Fast barcode lookup (filtered — only non-NULL barcodes)
CREATE INDEX IF NOT EXISTS IX_Items_Barcode    ON Items(Barcode) WHERE Barcode IS NOT NULL;
CREATE INDEX IF NOT EXISTS IX_Items_Category   ON Items(CategoryId);

-- Bills: Daily reports, customer history
CREATE INDEX IF NOT EXISTS IX_Bills_Customer   ON Bills(CustomerId);
CREATE INDEX IF NOT EXISTS IX_Bills_CreatedAt  ON Bills(CreatedAt);
CREATE INDEX IF NOT EXISTS IX_Bills_Status     ON Bills(Status);

-- BillItems: Bill loading, item sales history
CREATE INDEX IF NOT EXISTS IX_BillItems_BillId ON BillItems(BillId);
CREATE INDEX IF NOT EXISTS IX_BillItems_ItemId ON BillItems(ItemId);

-- Payments: Bill payment lookup, daily cash reports
CREATE INDEX IF NOT EXISTS IX_Payments_BillId  ON Payments(BillId);
CREATE INDEX IF NOT EXISTS IX_Payments_PaidAt  ON Payments(PaidAt);

-- Returns: Bill return lookup
CREATE INDEX IF NOT EXISTS IX_Returns_BillId     ON BillReturns(BillId);
CREATE INDEX IF NOT EXISTS IX_ReturnItems_RetId  ON BillReturnItems(ReturnId);
CREATE INDEX IF NOT EXISTS IX_ReturnItems_BiId   ON BillReturnItems(BillItemId);

-- Inventory: Stock calculation, date-range reports
CREATE INDEX IF NOT EXISTS IX_Inventory_ItemId   ON InventoryLogs(ItemId);
CREATE INDEX IF NOT EXISTS IX_Inventory_LogDate  ON InventoryLogs(LogDate);

-- Customers: Phone search
CREATE INDEX IF NOT EXISTS IX_Customers_Phone    ON Customers(Phone);
```

---

## 7. Sample Data

### Categories
| CategoryId | Name |
|---|---|
| 1 | Dairy |
| 2 | Beverages |
| 3 | Snacks |
| 4 | Grocery |

### Items
| ItemId | Barcode | Description | CostPrice | SalePrice | CategoryId |
|---|---|---|---|---|---|
| 1 | 8961000100018 | Olper's Milk 1L | 240 | 270 | 1 |
| 2 | 5449001000996 | Coca Cola 1.5L | 130 | 160 | 2 |
| 3 | 8964001510017 | Lays Classic Chips | 50 | 70 | 3 |
| 4 | NULL | Generic Bag (no barcode) | 5 | 10 | NULL |

### Customers
| CustomerId | FullName | Phone | Address | IsActive |
|---|---|---|---|---|
| 1 | Ahmed Khan | 03001234567 | Rawat, Rawalpindi | 1 |
| 2 | Sara Ali | 03211234567 | Islamabad | 1 |

### Bills
| BillId | CustomerId | UserId | TaxAmount | DiscountAmount | Status | CreatedAt |
|---|---|---|---|---|---|---|
| 1 | 1 | 2 | 0 | 0 | Completed | 2026-03-10 10:30:00 |
| 2 | NULL | 2 | 0 | 50 | Completed | 2026-03-10 11:00:00 |

### BillItems
| BillItemId | BillId | ItemId | Quantity | UnitPrice | DiscountAmount |
|---|---|---|---|---|---|
| 1 | 1 | 1 | 2 | 270 | 0 |
| 2 | 1 | 2 | 3 | 160 | 0 |
| 3 | 2 | 3 | 5 | 70 | 0 |

### Payments
| PaymentId | BillId | Amount | PaymentMethod | TransactionType | PaidAt |
|---|---|---|---|---|---|
| 1 | 1 | 500 | Cash | Sale | 2026-03-10 10:30:00 |
| 2 | 2 | 300 | Cash | Sale | 2026-03-10 11:00:00 |
| 3 | 1 | 520 | Cash | Credit Payment | 2026-03-11 09:00:00 |

### BillReturns
| ReturnId | BillId | UserId | RefundAmount | ReturnedAt |
|---|---|---|---|---|
| 1 | 1 | 2 | 160 | 2026-03-11 14:00:00 |

### BillReturnItems
| ReturnItemId | ReturnId | BillItemId | Quantity | UnitPrice |
|---|---|---|---|---|
| 1 | 1 | 2 | 1 | 160 |

### InventoryLogs
| LogId | ItemId | QuantityChange | ChangeType | LogDate |
|---|---|---|---|---|
| 1 | 1 | 100 | Purchase | 2026-03-01 |
| 2 | 2 | 100 | Purchase | 2026-03-01 |
| 3 | 3 | 100 | Purchase | 2026-03-01 |
| 4 | 1 | -2 | Sale | 2026-03-10 10:30:00 |
| 5 | 2 | -3 | Sale | 2026-03-10 10:30:00 |
| 6 | 3 | -5 | Sale | 2026-03-10 11:00:00 |
| 7 | 2 | 1 | Return | 2026-03-11 14:00:00 |

---

## 8. SQL Queries — Bill Calculation

### Bill Total (SubTotal)
```sql
SELECT COALESCE(SUM(Quantity * UnitPrice - DiscountAmount), 0) AS SubTotal
FROM BillItems
WHERE BillId = @billId;
```

### Grand Total (with tax and discount)
```sql
SELECT
    COALESCE(SUM(bi.Quantity * bi.UnitPrice - bi.DiscountAmount), 0)
    + b.TaxAmount - b.DiscountAmount AS GrandTotal
FROM Bills b
JOIN BillItems bi ON b.BillId = bi.BillId
WHERE b.BillId = @billId
GROUP BY b.BillId;
```

### Return Amount for a Bill
```sql
SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0) AS TotalReturnValue
FROM BillReturns br
JOIN BillReturnItems bri ON br.ReturnId = bri.ReturnId
WHERE br.BillId = @billId;
```

### Net Bill Amount
```sql
-- Net = GrandTotal - ReturnValue
SELECT
    (COALESCE(SUM(bi.Quantity * bi.UnitPrice - bi.DiscountAmount), 0)
     + b.TaxAmount - b.DiscountAmount)
    -
    COALESCE((SELECT SUM(bri.Quantity * bri.UnitPrice)
              FROM BillReturns br
              JOIN BillReturnItems bri ON br.ReturnId = bri.ReturnId
              WHERE br.BillId = b.BillId), 0)
    AS NetBillAmount
FROM Bills b
JOIN BillItems bi ON b.BillId = bi.BillId
WHERE b.BillId = @billId
GROUP BY b.BillId;
```

### Remaining Balance
```sql
-- Balance = NetBillAmount - TotalPayments
SELECT
    (COALESCE(SUM(bi.Quantity * bi.UnitPrice - bi.DiscountAmount), 0)
     + b.TaxAmount - b.DiscountAmount)
    -
    COALESCE((SELECT SUM(bri.Quantity * bri.UnitPrice)
              FROM BillReturns br
              JOIN BillReturnItems bri ON br.ReturnId = bri.ReturnId
              WHERE br.BillId = b.BillId), 0)
    -
    COALESCE((SELECT SUM(Amount) FROM Payments WHERE BillId = b.BillId), 0)
    AS Balance
FROM Bills b
JOIN BillItems bi ON b.BillId = bi.BillId
WHERE b.BillId = @billId
GROUP BY b.BillId;
```

---

## 9. SQL Queries — Payment Tracking

### All Payments for a Bill
```sql
SELECT PaymentId, Amount, PaymentMethod, TransactionType, Note, PaidAt
FROM Payments
WHERE BillId = @billId
ORDER BY PaidAt ASC;
```

### Total Paid for a Bill
```sql
SELECT COALESCE(SUM(Amount), 0) AS TotalPaid
FROM Payments
WHERE BillId = @billId;
```

### Today's Total Cash Collected
```sql
SELECT COALESCE(SUM(Amount), 0)
FROM Payments
WHERE PaidAt >= date('now') AND PaidAt < date('now', '+1 day');
```

### Today's Credit Recovered (payments for old bills)
```sql
SELECT COALESCE(SUM(p.Amount), 0)
FROM Payments p
JOIN Bills b ON p.BillId = b.BillId
WHERE p.PaidAt >= date('now') AND p.PaidAt < date('now', '+1 day')
  AND b.CreatedAt < date('now');
```

### Outstanding Credit per Customer
```sql
SELECT c.CustomerId, c.FullName,
    COALESCE(SUM(bi.Quantity * bi.UnitPrice - bi.DiscountAmount) + b.TaxAmount - b.DiscountAmount, 0)
    - COALESCE((SELECT SUM(Amount) FROM Payments WHERE BillId IN
               (SELECT BillId FROM Bills WHERE CustomerId = c.CustomerId)), 0)
    AS PendingCredit
FROM Customers c
JOIN Bills b ON c.CustomerId = b.CustomerId
JOIN BillItems bi ON b.BillId = bi.BillId
WHERE b.Status != 'Cancelled'
GROUP BY c.CustomerId
HAVING PendingCredit > 0;
```

---

## 10. SQL Queries — Return Handling

### Get Already-Returned Quantity for a Specific Item on a Bill
```sql
SELECT COALESCE(SUM(bri.Quantity), 0)
FROM BillReturnItems bri
JOIN BillReturns br ON bri.ReturnId = br.ReturnId
JOIN BillItems bi ON bri.BillItemId = bi.BillItemId
JOIN Items i ON bi.ItemId = i.ItemId
WHERE br.BillId = @billId AND i.Barcode = @barcode;
```

### Get All Returns for an Original Bill
```sql
SELECT br.ReturnId, br.RefundAmount, br.ReturnedAt,
       bri.Quantity, bri.UnitPrice, i.Barcode, i.Description
FROM BillReturns br
JOIN BillReturnItems bri ON br.ReturnId = bri.ReturnId
JOIN BillItems bi ON bri.BillItemId = bi.BillItemId
JOIN Items i ON bi.ItemId = i.ItemId
WHERE br.BillId = @billId
ORDER BY br.ReturnedAt DESC;
```

### Today's Total Refunds
```sql
SELECT COALESCE(SUM(RefundAmount), 0)
FROM BillReturns
WHERE ReturnedAt >= date('now') AND ReturnedAt < date('now', '+1 day');
```

---

## 11. Example: Sale Transaction

**Scenario:** Customer Ahmed Khan buys 2x Olper's Milk (Rs. 270 each) and 3x Coca Cola (Rs. 160 each), pays Rs. 500 in cash (credit sale).

```sql
-- Step 1: Insert Bill Header
INSERT INTO Bills (CustomerId, UserId, TaxAmount, DiscountAmount, Status)
VALUES (1, 2, 0, 0, 'Completed');
-- Returns BillId = 1

-- Step 2: Insert Line Items
INSERT INTO BillItems (BillId, ItemId, Quantity, UnitPrice, DiscountAmount)
VALUES (1, 1, 2, 270, 0);  -- 2x Olper's Milk = 540

INSERT INTO BillItems (BillId, ItemId, Quantity, UnitPrice, DiscountAmount)
VALUES (1, 2, 3, 160, 0);  -- 3x Coca Cola = 480

-- Step 3: Record Stock Deductions
INSERT INTO InventoryLogs (ItemId, QuantityChange, ChangeType)
VALUES (1, -2, 'Sale');

INSERT INTO InventoryLogs (ItemId, QuantityChange, ChangeType)
VALUES (2, -3, 'Sale');

-- Step 4: Record Payment (partial — credit sale)
INSERT INTO Payments (BillId, Amount, PaymentMethod, TransactionType)
VALUES (1, 500, 'Cash', 'Sale');

-- Grand Total = 540 + 480 = 1020
-- Paid = 500, Remaining = 520 (credit)
```

---

## 12. Example: Return Transaction

**Scenario:** Customer returns 1x Coca Cola from Bill #1. Original price was Rs. 160. Bill has Rs. 520 outstanding credit.

```sql
-- Step 1: Calculate refund allocation
-- ReturnValue = 1 × 160 = 160
-- Outstanding = 520
-- CreditToReduce = MIN(520, 160) = 160
-- CashRefund = 160 - 160 = 0 (absorbed by credit reduction)

-- Step 2: Insert Return Header (against ORIGINAL bill)
INSERT INTO BillReturns (BillId, UserId, RefundAmount, ReturnedAt)
VALUES (1, 2, 0, datetime('now'));
-- Returns ReturnId = 1

-- Step 3: Insert Return Line Item (references original BillItem)
INSERT INTO BillReturnItems (ReturnId, BillItemId, Quantity, UnitPrice)
VALUES (1, 2, 1, 160);  -- BillItemId=2 was the Coca Cola line

-- Step 4: Record Stock Increase
INSERT INTO InventoryLogs (ItemId, QuantityChange, ChangeType)
VALUES (2, 1, 'Return');

-- Step 5: Record Credit Offset as Payment
INSERT INTO Payments (BillId, Amount, PaymentMethod, TransactionType)
VALUES (1, 160, 'Cash', 'Credit Payment');

-- Result:
-- Original Bill Total = 1020
-- Returns = 160
-- Net Bill = 860
-- Payments = 500 (sale) + 160 (credit offset) = 660
-- New Balance = 860 - 660 = 200
```

**Key Point:** No new bill was created. The return is recorded against Bill #1. The original BillItems are never modified.

---

## 13. Example: Payment Transaction

**Scenario:** Customer Ahmed pays Rs. 200 against outstanding Bill #1.

```sql
-- Step 1: Validate remaining balance
SELECT
    (SELECT COALESCE(SUM(Quantity * UnitPrice - DiscountAmount), 0) FROM BillItems WHERE BillId = 1)
    + (SELECT TaxAmount - DiscountAmount FROM Bills WHERE BillId = 1)
    - COALESCE((SELECT SUM(bri.Quantity * bri.UnitPrice)
                FROM BillReturns br JOIN BillReturnItems bri ON br.ReturnId = bri.ReturnId
                WHERE br.BillId = 1), 0)
    - COALESCE((SELECT SUM(Amount) FROM Payments WHERE BillId = 1), 0) AS Balance;
-- Balance = 200

-- Step 2: Record Payment if Balance >= PaymentAmount
INSERT INTO Payments (BillId, Amount, PaymentMethod, TransactionType, Note)
VALUES (1, 200, 'Cash', 'Credit Payment', 'Final credit settlement');

-- New Balance = 0 → Bill is fully paid
```

---

## 14. Inventory Update Logic

### Stock Calculation (Always Computed)
```sql
-- Current stock for any item
SELECT COALESCE(SUM(QuantityChange), 0) AS CurrentStock
FROM InventoryLogs
WHERE ItemId = @itemId;
```

### When Items Are Sold (Negative Change)
```sql
INSERT INTO InventoryLogs (ItemId, QuantityChange, ChangeType)
VALUES (@itemId, -@quantity, 'Sale');
```

### When Items Are Returned (Positive Change)
```sql
INSERT INTO InventoryLogs (ItemId, QuantityChange, ChangeType)
VALUES (@itemId, @quantity, 'Return');
```

### When Stock Is Purchased / Supplied (Positive Change)
```sql
INSERT INTO InventoryLogs (ItemId, QuantityChange, ChangeType)
VALUES (@itemId, @quantity, 'Purchase');
```

### Low Stock Alert
```sql
SELECT i.ItemId, i.Barcode, i.Description, i.MinStockThreshold,
       COALESCE(SUM(il.QuantityChange), 0) AS CurrentStock
FROM Items i
LEFT JOIN InventoryLogs il ON i.ItemId = il.ItemId
GROUP BY i.ItemId
HAVING CurrentStock <= i.MinStockThreshold
ORDER BY CurrentStock ASC;
```

---

## 15. Migration Strategy

The application handles migration automatically on startup via `DatabaseInitializer.MigrateIfNeeded()`. The migration framework uses SQLite's `PRAGMA user_version` to track schema version.

### Migration Path

```
v0 (Legacy: Bill/Item/BillDescription/BILL_RETURNS/stock)
  ↓  MigrateFromLegacySchema()
v1 (New table names: Bills/Items/BillItems/BillReturns/InventoryLogs)
  ↓  MigrateSchemaV2()
v2 (Fully normalized: surrogate PKs, correct column names, BillReturnItems)
```

### What Each Migration Does

| Migration | From → To | Changes |
|---|---|---|
| v0 → v1 | Legacy tables | Renames tables: `Bill→Bills`, `Item→Items`, `BillDescription→BillItems`, `BILL_RETURNS→BillReturns`, `stock→InventoryLogs` |
| v1 → v2 | Mismatched schema | BillItems: composite PK → `BillItemId AUTOINCREMENT`, `ItemDiscount→DiscountAmount`; Payments: `Method→PaymentMethod`, add `TransactionType`; Bills: add `IsPrinted/PrintedAt/PrintAttempts`; BillReturns: flat → header/detail; adds `BillReturnItems` table |

### Safe Migration Approach

1. All migrations use `PRAGMA foreign_keys = OFF` during table recreation
2. Data is copied using `INSERT INTO ... SELECT FROM`
3. Old tables are renamed to `*_old` before recreating
4. Transactions ensure atomicity — rollback on any failure
5. `CREATE TABLE IF NOT EXISTS` makes all operations idempotent
6. `AddColumnIfNotExists()` safely adds columns without breaking existing databases

### Manual Fresh Start (if needed)

Delete the database file and restart:
```
%AppData%\GroceryPOS\GroceryPOS.db
```
The application will recreate all tables and seed default data.

---

## 16. Application Logic Guidance

### Billing Flow (No Changes Needed)

```
1. User adds items to cart
2. BillService.CompleteBill() validates stock and calculates totals
3. BillRepository.SaveBillWithTransaction() atomically:
   a. INSERT INTO Bills → get BillId
   b. INSERT INTO BillItems for each line
   c. INSERT INTO InventoryLogs (Sale, negative qty) for each line
   d. INSERT INTO Payments for initial payment
4. Cache updated, UI refreshed
```

### Return Flow (Fixed)

```
1. User enters original Bill ID → load bill with line items
2. User selects items and quantities to return
3. ReturnService.CreateReturn() atomically:
   a. Validate return quantities ≤ (original - already returned)
   b. Calculate: creditToReduce = MIN(outstanding, returnValue)
   c. Calculate: cashRefund = returnValue - creditToReduce
   d. INSERT INTO BillReturns (header with RefundAmount = cashRefund)
   e. INSERT INTO BillReturnItems for each returned item
   f. INSERT INTO InventoryLogs (Return, positive qty) for each item
   g. INSERT INTO Payments (Credit Payment) if creditToReduce > 0
4. NO new bill created — everything references original BillId
5. Print short return receipt or full detailed bill
```

### Credit Payment Flow (No Changes Needed)

```
1. Select customer → view bill ledger
2. Select bill with outstanding balance
3. CreditService.RecordPayment() inserts into Payments
4. Balance recalculated from: GrandTotal - SUM(Payments) - SUM(Returns)
```

### Key Code Files Changed

| File | What Changed | Why |
|---|---|---|
| `Data/DatabaseInitializer.cs` | Complete schema rewrite + migration framework | Fixed all 10 issues listed above |
| `Data/Repositories/BillRepository.cs` | `i.Id` → `i.ItemId` in JOIN | Column name mismatch |
| `Data/Repositories/ItemRepository.cs` | `WHERE Id = @id` → `WHERE ItemId = @id` | Column name mismatch |
| `Services/StockService.cs` | Removed `UPDATE Items SET StockQuantity`; Fixed `i.Id` → `i.ItemId`; Fixed `l.CreatedAt` → `l.LogDate` | Non-existent column + column mismatches |
| `Services/ReportService.cs` | `i.Id` → `i.ItemId` | Column name mismatch |
| `ViewModels/BillingViewModel.cs` | `int totalRequested` → `double totalRequested` | Type mismatch (double + int → double) |

---

## 17. Normalization Analysis

### First Normal Form (1NF) ✅

- All tables have defined primary keys
- All columns contain atomic (indivisible) values
- No repeating groups or arrays
- Each row is unique

### Second Normal Form (2NF) ✅

- All non-key columns are fully functionally dependent on the entire primary key
- `BillItems` uses surrogate PK `BillItemId` — no partial dependencies
- `BillReturnItems` uses surrogate PK `ReturnItemId` — no partial dependencies

### Third Normal Form (3NF) ✅

- No transitive dependencies: all columns depend only on the primary key
- **Stock quantity** is NOT stored in Items — calculated from `SUM(InventoryLogs.QuantityChange)`
- **Bill totals** are NOT stored in Bills — calculated from `SUM(BillItems.Quantity × UnitPrice)`
- **Paid amount** is NOT stored in Bills — calculated from `SUM(Payments.Amount)`
- **Remaining balance** is NOT stored — derived: `GrandTotal - Returns - Payments`
- **Category name** is NOT stored in Items — joined from Categories table

### What Was Denormalized (Intentionally Removed)

| Removed Column | Was In | Now Calculated From |
|---|---|---|
| StockQuantity | Items | `SUM(InventoryLogs.QuantityChange)` |
| SubTotal | Bills | `SUM(BillItems.Quantity × UnitPrice)` |
| PaidAmount | Bills | `SUM(Payments.Amount)` |
| RemainingAmount | Bills | `GrandTotal - Returns - Payments` |
| PaymentStatus | Bills | Derived from `RemainingAmount` |

> These values exist only as **calculated properties in C# model classes** — never stored in the database.

---

## 18. Receipt Printing Logic

### Short Return Receipt (Default)

Contains:
- Bill ID (original)
- Return ID
- Returned items with quantity and unit price
- Refund amount (cash given back)
- Credit adjustment amount (if any)
- Date/time of return

### Full Detailed Bill (Optional)

Contains:
- Store header (name, address, phone)
- Original bill items with quantities and prices
- Subtotal, tax, discount, grand total
- Return history (all returns for this bill)
- Payment history (all payments for this bill)
- Current remaining balance
- Cashier name and date/time

### Data Query for Full Receipt

```sql
-- Original items
SELECT bi.Quantity, bi.UnitPrice, bi.DiscountAmount, i.Description
FROM BillItems bi JOIN Items i ON bi.ItemId = i.ItemId
WHERE bi.BillId = @billId;

-- Returns
SELECT br.ReturnId, br.ReturnedAt, br.RefundAmount,
       bri.Quantity, bri.UnitPrice, i.Description
FROM BillReturns br
JOIN BillReturnItems bri ON br.ReturnId = bri.ReturnId
JOIN BillItems bi ON bri.BillItemId = bi.BillItemId
JOIN Items i ON bi.ItemId = i.ItemId
WHERE br.BillId = @billId;

-- Payments
SELECT Amount, PaymentMethod, TransactionType, PaidAt
FROM Payments WHERE BillId = @billId ORDER BY PaidAt;
```

---

## Appendix: Quick Reference

### Bill Calculation Formula

```
SubTotal        = Σ(BillItems.Quantity × BillItems.UnitPrice - BillItems.DiscountAmount)
GrandTotal      = SubTotal + TaxAmount - DiscountAmount
ReturnValue     = Σ(BillReturnItems.Quantity × BillReturnItems.UnitPrice)
NetBillAmount   = GrandTotal - ReturnValue
TotalPaid       = Σ(Payments.Amount)
Balance         = NetBillAmount - TotalPaid
PaymentStatus   = Balance ≤ 0 ? "Paid" : TotalPaid > 0 ? "Partial" : "Unpaid"
```

### Inventory Formula

```
CurrentStock    = Σ(InventoryLogs.QuantityChange) WHERE ItemId = ?
  Sale:         QuantityChange = -quantity (negative)
  Return:       QuantityChange = +quantity (positive)
  Purchase:     QuantityChange = +quantity (positive)
  Adjustment:   QuantityChange = ±quantity (either)
```

### Negative Balance Support

The schema supports negative totals when refund exceeds remaining:
- `Payments.Amount` has no CHECK constraint preventing negatives
- `BillReturns.RefundAmount` can exceed the remaining balance
- The application calculates the cash refund to give back to the customer
