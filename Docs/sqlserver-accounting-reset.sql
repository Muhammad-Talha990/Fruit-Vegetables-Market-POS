/* =========================================================
   GroceryPOS - SQL Server clean reset + normalized schema
   Canonical payment table: dbo.bill_payment (ONLY)
   ========================================================= */

/* 1) BACKUP (edit path and DB name first) */
BACKUP DATABASE [GroceryPOS]
TO DISK = N'C:\SqlBackups\GroceryPOS_FULL.bak'
WITH INIT, COMPRESSION, STATS = 10;
GO

/* 2) DROP + RECREATE DATABASE */
USE [master];
GO
ALTER DATABASE [GroceryPOS] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
GO
DROP DATABASE [GroceryPOS];
GO
CREATE DATABASE [GroceryPOS];
GO
USE [GroceryPOS];
GO

/* 3) TABLES */
CREATE TABLE dbo.customers (
    id            INT IDENTITY(1,1) PRIMARY KEY,
    full_name     NVARCHAR(150) NOT NULL,
    phone         NVARCHAR(30)  NOT NULL UNIQUE,
    created_at    DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

CREATE TABLE dbo.sales (
    id            INT IDENTITY(1,1) PRIMARY KEY,
    customer_id   INT NULL,
    created_at    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_sales_customers
        FOREIGN KEY (customer_id) REFERENCES dbo.customers(id)
);
GO

CREATE TABLE dbo.sale_items (
    id            INT IDENTITY(1,1) PRIMARY KEY,
    sale_id       INT NOT NULL,
    item_code     NVARCHAR(60) NOT NULL,
    description   NVARCHAR(250) NOT NULL,
    quantity      DECIMAL(18,3) NOT NULL CHECK (quantity > 0),
    unit_price    DECIMAL(18,2) NOT NULL CHECK (unit_price >= 0),
    CONSTRAINT FK_sale_items_sales
        FOREIGN KEY (sale_id) REFERENCES dbo.sales(id) ON DELETE CASCADE
);
GO

/* ONLY payment/refund table */
CREATE TABLE dbo.bill_payment (
    id            BIGINT IDENTITY(1,1) PRIMARY KEY,
    bill_id       INT NOT NULL,
    amount        DECIMAL(18,2) NOT NULL CHECK (amount >= 0),
    type          VARCHAR(10) NOT NULL CHECK (type IN ('payment', 'refund')),
    created_at    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_bill_payment_sales
        FOREIGN KEY (bill_id) REFERENCES dbo.sales(id) ON DELETE CASCADE
);
GO

CREATE TABLE dbo.returns (
    id            BIGINT IDENTITY(1,1) PRIMARY KEY,
    bill_id       INT NOT NULL,
    amount        DECIMAL(18,2) NOT NULL CHECK (amount >= 0),
    created_at    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_returns_sales
        FOREIGN KEY (bill_id) REFERENCES dbo.sales(id) ON DELETE CASCADE
);
GO

CREATE TABLE dbo.customer_ledger (
    id            BIGINT IDENTITY(1,1) PRIMARY KEY,
    customer_id   INT NOT NULL,
    entry_type    VARCHAR(10) NOT NULL CHECK (entry_type IN ('sale', 'payment', 'return', 'refund')),
    bill_id       INT NULL,
    debit         DECIMAL(18,2) NOT NULL DEFAULT 0,
    credit        DECIMAL(18,2) NOT NULL DEFAULT 0,
    created_at    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_customer_ledger_customers
        FOREIGN KEY (customer_id) REFERENCES dbo.customers(id),
    CONSTRAINT FK_customer_ledger_sales
        FOREIGN KEY (bill_id) REFERENCES dbo.sales(id)
);
GO

/* 4) INDEXES */
CREATE INDEX IX_sales_customer_id ON dbo.sales(customer_id, created_at);
CREATE INDEX IX_sale_items_sale_id ON dbo.sale_items(sale_id);
CREATE INDEX IX_bill_payment_bill ON dbo.bill_payment(bill_id, created_at);
CREATE INDEX IX_bill_payment_type ON dbo.bill_payment(type, created_at);
CREATE INDEX IX_returns_bill ON dbo.returns(bill_id, created_at);
CREATE INDEX IX_customer_ledger_customer ON dbo.customer_ledger(customer_id, created_at);
GO

/* 5) SAMPLE DATA FOR VALIDATION */
INSERT INTO dbo.customers(full_name, phone) VALUES
('Ali Khan', '03001234567'),
('Sara Ahmed', '03111222333');

INSERT INTO dbo.sales(customer_id) VALUES (1), (2);

INSERT INTO dbo.sale_items(sale_id, item_code, description, quantity, unit_price) VALUES
(1, 'SKU-001', 'Milk 1L', 2, 250),
(1, 'SKU-002', 'Bread',   1, 120),
(2, 'SKU-003', 'Tea 900g',1, 900);

/* Bill 1: total 620, customer pays 500 now */
INSERT INTO dbo.bill_payment(bill_id, amount, type) VALUES (1, 500, 'payment');
INSERT INTO dbo.customer_ledger(customer_id, entry_type, bill_id, debit, credit)
VALUES (1, 'sale', 1, 620, 0), (1, 'payment', 1, 0, 500);

/* Return on bill 1: 150 (<= pending 120? no, this is > pending by 30) */
INSERT INTO dbo.returns(bill_id, amount) VALUES (1, 150);
INSERT INTO dbo.customer_ledger(customer_id, entry_type, bill_id, debit, credit)
VALUES (1, 'return', 1, 0, 150);

/* refund only extra above pending: 30 */
INSERT INTO dbo.bill_payment(bill_id, amount, type) VALUES (1, 30, 'refund');
INSERT INTO dbo.customer_ledger(customer_id, entry_type, bill_id, debit, credit)
VALUES (1, 'refund', 1, 30, 0);
GO

/* 6) VERIFICATION QUERIES */

/* A. Confirm no duplicate payment tables */
SELECT TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE'
  AND TABLE_NAME IN ('Payments', 'bill_payment');
GO

/* B. Receivable per bill:
      receivable = sale_total - payments - returns
      (refund does NOT reduce receivable; it affects cash only) */
SELECT
    s.id AS bill_id,
    sale_total = COALESCE(si.sale_total, 0),
    paid_total = COALESCE(bp.payments, 0),
    return_total = COALESCE(r.returns, 0),
    receivable = COALESCE(si.sale_total, 0) - COALESCE(bp.payments, 0) - COALESCE(r.returns, 0)
FROM dbo.sales s
OUTER APPLY (
    SELECT SUM(quantity * unit_price) AS sale_total
    FROM dbo.sale_items
    WHERE sale_id = s.id
) si
OUTER APPLY (
    SELECT SUM(amount) AS payments
    FROM dbo.bill_payment
    WHERE bill_id = s.id AND type = 'payment'
) bp
OUTER APPLY (
    SELECT SUM(amount) AS returns
    FROM dbo.returns
    WHERE bill_id = s.id
) r;
GO

/* C. Net cash movement:
      +payment, -refund */
SELECT
    cash_in  = COALESCE(SUM(CASE WHEN type = 'payment' THEN amount END), 0),
    cash_out = COALESCE(SUM(CASE WHEN type = 'refund' THEN amount END), 0),
    net_cash = COALESCE(SUM(CASE WHEN type = 'payment' THEN amount
                                 WHEN type = 'refund' THEN -amount
                                 ELSE 0 END), 0)
FROM dbo.bill_payment;
GO

/* D. Ledger consistency check (per customer) */
SELECT
    c.id,
    c.full_name,
    ledger_balance = COALESCE(SUM(l.debit - l.credit), 0)
FROM dbo.customers c
LEFT JOIN dbo.customer_ledger l ON l.customer_id = c.id
GROUP BY c.id, c.full_name;
GO
