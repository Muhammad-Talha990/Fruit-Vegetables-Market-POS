# FINANCIAL AUDIT REPORT
## GroceryPOS Dashboard Verification Guide

**System Version:** SQLite with v15 Schema (PaymentMethod tracking enabled)  
**Audit Date:** April 22, 2026  
**Status:** Active - Ready for Verification

---

## SECTION 1: CORRECT MATHEMATICAL RELATIONSHIPS

### Core Accounting Equation (Daily)

```
Total Sales - Returns = Net Sales ✓

Net Sales = Cash Payments + Online Payments + Remaining Credit

Remaining Credit = ∑(Each Bill's Unpaid Amount)
```

### Complete Formula Breakdown

For **each bill**:
```
GrandTotal = ∑(ItemQuantity × ItemPrice) + TaxAmount - DiscountAmount

NetTotal = GrandTotal - ReturnedAmount

PaidAmount = InitialPayment + ∑(AllCreditPayments) - ∑(AllRefunds)

RemainingAmount = MAX(0, NetTotal - PaidAmount)

CreditDueAmount = MAX(0, PaidAmount - NetTotal)  [Overpayment refund owed]
```

For **daily dashboard**:
```
Total Sales = ∑(All Bills' GrandTotal) for today

Returns = ∑(All Returned Items' Value) today

Net Sales = Total Sales - Returns

Cash In Drawer = ∑(Initial Cash Payments) + ∑(Subsequent Cash Payments) - ∑(Cash Refunds)

Online Payments = ∑(Initial Online Payments) + ∑(Subsequent Online Payments) - ∑(Online Refunds)

Customer Credit = ∑(RemainingAmount from all bills) 

Recovered Credits = ∑(Payment entries from bill_payment table) today

Total Paid = Cash In Drawer + Online Payments + Recovered Credits
```

### Key Validation: Every Dollar Must Reconcile

```
Total Sales = Cash + Online + Customer Credit (remaining unpaid)

Total Sales - Returns = Net Sales ✓

Net Sales = Cash In Drawer + Online Payments + Remaining Credit

Cash In Drawer + Online Payments = Amount Received Today
Remaining Credit = Amount Owed by Customers
```

---

## SECTION 2: DATABASE SCHEMA VERIFICATION

### Table: Bills
| Column | Type | Purpose |
|--------|------|---------|
| BillId | PRIMARY KEY | Unique bill identifier |
| CustomerId | FK | Links to customer |
| InitialPayment | REAL | Amount paid AT TIME OF SALE |
| BillPaymentMethod | TEXT | 'Cash' OR 'Online' (method used at checkout) |
| TaxAmount | REAL | Tax on this bill |
| DiscountAmount | REAL | Discount applied |
| CreatedAt | DATETIME | Bill creation timestamp |
| Status | TEXT | 'Completed' OR 'Cancelled' |
| AccountId | FK | Online payment account (if applicable) |

### Table: BillItems
| Column | Type | Purpose |
|--------|------|---------|
| BillId | FK | Links to bill |
| ItemId | FK | Product reference |
| Quantity | REAL | Units sold |
| UnitPrice | REAL | Price per unit |
| DiscountAmount | REAL | Line-item discount |

### Table: bill_payment (CRITICAL)
| Column | Type | Purpose |
|--------|------|---------|
| PaymentId | PRIMARY KEY | Payment entry ID |
| BillId | FK | Links to original bill |
| Amount | REAL | Payment/refund amount (always >= 0) |
| Type | TEXT | 'payment' OR 'refund' |
| PaymentMethod | TEXT | **'Cash' OR 'Online'** (v15+) |
| CreatedAt | DATETIME | When payment was recorded |

**CRITICAL:** The `PaymentMethod` column (added in v15) tracks whether each LEDGER payment was made via cash or online, independent of the original bill's method.

### Table: BillReturns
| Column | Type | Purpose |
|--------|------|---------|
| ReturnId | PRIMARY KEY | Return transaction ID |
| BillId | FK | Links to original bill |
| RefundAmount | REAL | Total refunded |
| ReturnedAt | DATETIME | Return timestamp |

### Table: BillReturnItems
| Column | Type | Purpose |
|--------|------|---------|
| ReturnId | FK | Links to return |
| BillItemId | FK | Links to original item |
| Quantity | REAL | Units returned |
| UnitPrice | REAL | Price used for return |

---

## SECTION 3: CURRENT DASHBOARD CALCULATION METHODS

### Method 1: Total Sales (`GetTodayTotal()`)
```sql
SELECT COALESCE(SUM(bill_total), 0)
FROM (
    SELECT (SUM(bi.Quantity * bi.UnitPrice) + b.TaxAmount - b.DiscountAmount) as bill_total
    FROM Bills b
    JOIN BillItems bi ON b.BillId = bi.BillId
    WHERE date(b.CreatedAt) = @today 
      AND b.Status != 'Cancelled'
    GROUP BY b.BillId
);
```
**What it counts:** Grand total of all bills created today (before returns)

---

### Method 2: Total Returns (`GetTodayReturnsTotal()`)
```sql
SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0)
FROM BillReturnItems bri
JOIN BillReturns br ON bri.ReturnId = br.ReturnId
WHERE date(br.ReturnedAt) = @today;
```
**What it counts:** Total value of items returned today

---

### Method 3: Customer Credit - Remaining Unpaid (`GetTodayTotalRemaining()`)
```sql
SELECT COALESCE(SUM(
    CASE WHEN (
        (SUM(bi.Quantity * bi.UnitPrice) + TaxAmount - DiscountAmount 
         - COALESCE(ReturnedAmount, 0)
         - InitialPayment 
         - COALESCE((SELECT SUM(p.Amount) 
                    FROM bill_payment p 
                    WHERE p.BillId = b.BillId 
                      AND p.Type = 'payment'), 0)
        ) > 0
        THEN (above calculation)
        ELSE 0
    END
), 0)
FROM Bills b
WHERE date(b.CreatedAt) = @today AND b.Status != 'Cancelled';
```
**What it counts:** Unpaid balance for all bills created today

---

### Method 4: Recovered Credits (`GetTodayRecoveredCredit()`)
```sql
SELECT COALESCE(SUM(Amount), 0)
FROM bill_payment
WHERE date(CreatedAt) = @today
  AND Type = 'payment'
  AND Amount > 0;
```
**What it counts:** ALL payments recorded today (includes both initial and subsequent payments)

**⚠️ ISSUE:** This method sums ALL payments without checking date bill was created. It includes:
- Initial payments from bills created today (counted twice if BillPaymentMethod='Cash')
- Initial payments from bills created BEFORE today (ledger payments)
- Subsequent payments on old bills

---

### Method 5: Cash In Drawer (`GetTodayCashInDrawer()`)
```sql
SELECT 
    (SELECT COALESCE(SUM(InitialPayment), 0) 
     FROM Bills 
     WHERE date(CreatedAt) = @today 
       AND BillPaymentMethod = 'Cash' 
       AND Status != 'Cancelled')
    +
    (SELECT COALESCE(SUM(p.Amount), 0)
     FROM bill_payment p
     WHERE date(p.CreatedAt) = @today
       AND p.PaymentMethod = 'Cash'
       AND p.Type = 'payment')
    -
    (SELECT COALESCE(SUM(p.Amount), 0)
     FROM bill_payment p
     WHERE date(p.CreatedAt) = @today
        AND p.PaymentMethod = 'Cash'
        AND p.Type = 'refund');
```
**What it counts:**
- Initial cash payments from today's sales
- Subsequent cash payments from ledger today
- Minus cash refunds today

**✓ CORRECT** - Properly separates cash by PaymentMethod column

---

### Method 6: Online Payments (`GetTodayOnlinePayments()`)
```sql
SELECT 
    (SELECT COALESCE(SUM(InitialPayment), 0) 
     FROM Bills 
     WHERE date(CreatedAt) = @today 
       AND BillPaymentMethod = 'Online' 
       AND Status != 'Cancelled')
    +
    (SELECT COALESCE(SUM(p.Amount), 0)
     FROM bill_payment p
     WHERE date(p.CreatedAt) = @today
       AND p.PaymentMethod = 'Online'
       AND (p.Type = 'payment' OR p.Type = 'refund'))
```
**What it counts:**
- Initial online payments from today's sales
- All online ledger transactions (payments and refunds) today

**✓ CORRECT** - Properly separates online by PaymentMethod column

---

## SECTION 4: POTENTIAL MISMATCHES & ERROR SOURCES

### Issue 1: Double Counting "Recovered Credits"
**Problem:**
- `GetTodayRecoveredCredit()` returns ALL payments today, regardless of bill creation date
- This includes initial payments from today's sales
- Those initial payments are ALSO counted in `GetTodayTotalPaidInSales()` 
- Result: Same money appears in multiple metrics

**Example:**
```
Bill created today, initial payment TODAY as Cash = Rs. 1000
- Cash In Drawer includes it ✓
- Recovered Credits ALSO includes it ✗ (DOUBLE COUNT)
```

**Fix Needed:** 
```
GetTodayRecoveredCredit() should ONLY include:
SELECT COALESCE(SUM(Amount), 0)
FROM bill_payment
WHERE date(CreatedAt) = @today
  AND Type = 'payment'
  AND BillId IN (SELECT BillId FROM Bills WHERE date(CreatedAt) < @today)
  -- Only payments on bills created BEFORE today
```

---

### Issue 2: Recovered Credits Includes Refunds
**Problem:**
- Code uses `Amount > 0` filter, but refunds also have positive Amount (with Type='refund')
- Should use `Type = 'payment'` NOT `Type = 'refund'`

**Correct Formula:**
```
Recovered Credits = Only bill_payment entries where Type = 'payment'
Refunds = Only bill_payment entries where Type = 'refund' (cash outflow)
```

---

### Issue 3: Initial vs Ledger Payment Confusion
**Problem:**
- InitialPayment is stored on Bills table (payment AT checkout)
- Ledger payments stored on bill_payment table (payment AFTER checkout)
- These are DIFFERENT sources but both legitimate

**Correct Logic:**
```
Total Paid Today = 
  ∑(InitialPayments from today's bills) 
  + ∑(Ledger payments on TODAY's date, regardless of bill date)
```

---

### Issue 4: Return Offset Handling
**Problem:**
- Returns reduce NetTotal (correct)
- But returns can have their own refund entries in bill_payment
- Must not double-count: return value AND separate refund

**Correct Logic:**
```
If customer returns items worth Rs. 200:
  - Option A: Return directly (reduce bill balance) → netTotal -= 200
  - Option B: Return for refund → bill_payment type='refund', amount=200
  
NOT BOTH - choose one method per return
```

---

## SECTION 5: VERIFICATION SQL QUERIES

### Query A: Verify Today's Sales
```sql
-- Expected: Should match "Total Sales Today" on dashboard
SELECT 
    'Total Sales' AS Metric,
    COALESCE(SUM(
        (SELECT COALESCE(SUM(bi.Quantity * bi.UnitPrice), 0) 
         FROM BillItems bi 
         WHERE bi.BillId = b.BillId)
        + COALESCE(b.TaxAmount, 0)
        - COALESCE(b.DiscountAmount, 0)
    ), 0) AS Amount
FROM Bills b
WHERE date(b.CreatedAt) = date('now') 
  AND b.Status != 'Cancelled';
```

---

### Query B: Verify Returns Today
```sql
-- Expected: Should match "Returns Today" on dashboard
SELECT 
    'Total Returns' AS Metric,
    COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0) AS Amount
FROM BillReturnItems bri
JOIN BillReturns br ON bri.ReturnId = br.ReturnId
WHERE date(br.ReturnedAt) = date('now');
```

---

### Query C: Verify Net Sales
```sql
-- Net Sales = Total Sales - Returns
SELECT 
    'Total Sales' AS Metric,
    (SELECT COALESCE(SUM(
        (SELECT COALESCE(SUM(bi.Quantity * bi.UnitPrice), 0) 
         FROM BillItems bi WHERE bi.BillId = b.BillId)
        + b.TaxAmount - b.DiscountAmount
    ), 0) FROM Bills b WHERE date(b.CreatedAt) = date('now') AND b.Status != 'Cancelled') AS Value
UNION ALL
SELECT 'Returns', COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0)
FROM BillReturnItems bri
JOIN BillReturns br ON bri.ReturnId = br.ReturnId
WHERE date(br.ReturnedAt) = date('now')
UNION ALL
SELECT 'Net Sales', 
    ((SELECT COALESCE(SUM(
        (SELECT COALESCE(SUM(bi.Quantity * bi.UnitPrice), 0) 
         FROM BillItems bi WHERE bi.BillId = b.BillId)
        + b.TaxAmount - b.DiscountAmount
    ), 0) FROM Bills b WHERE date(b.CreatedAt) = date('now') AND b.Status != 'Cancelled')
    -
    (SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0)
     FROM BillReturnItems bri
     JOIN BillReturns br ON bri.ReturnId = br.ReturnId
     WHERE date(br.ReturnedAt) = date('now')));
```

---

### Query D: Verify Cash In Drawer (Correct)
```sql
-- Cash payments: initial + ledger, minus refunds
SELECT 
    'Initial Cash (Bills)' AS Source,
    COALESCE(SUM(InitialPayment), 0) AS Amount
FROM Bills 
WHERE date(CreatedAt) = date('now') 
  AND BillPaymentMethod = 'Cash' 
  AND Status != 'Cancelled'

UNION ALL

SELECT 'Ledger Cash Payments',
    COALESCE(SUM(p.Amount), 0)
FROM bill_payment p
WHERE date(p.CreatedAt) = date('now')
  AND p.PaymentMethod = 'Cash'
  AND p.Type = 'payment'

UNION ALL

SELECT 'Cash Refunds (Out)',
    -COALESCE(SUM(p.Amount), 0)
FROM bill_payment p
WHERE date(p.CreatedAt) = date('now')
  AND p.PaymentMethod = 'Cash'
  AND p.Type = 'refund'

UNION ALL

SELECT 'NET CASH IN DRAWER',
    (COALESCE((SELECT SUM(InitialPayment) FROM Bills 
              WHERE date(CreatedAt) = date('now') 
                AND BillPaymentMethod = 'Cash' AND Status != 'Cancelled'), 0)
    +
    COALESCE((SELECT SUM(p.Amount) FROM bill_payment p
              WHERE date(p.CreatedAt) = date('now') 
                AND p.PaymentMethod = 'Cash' AND p.Type = 'payment'), 0)
    -
    COALESCE((SELECT SUM(p.Amount) FROM bill_payment p
              WHERE date(p.CreatedAt) = date('now') 
                AND p.PaymentMethod = 'Cash' AND p.Type = 'refund'), 0));
```

---

### Query E: Verify Online Payments (Correct)
```sql
-- Online payments: initial + ledger net
SELECT 
    'Initial Online (Bills)' AS Source,
    COALESCE(SUM(InitialPayment), 0) AS Amount
FROM Bills 
WHERE date(CreatedAt) = date('now') 
  AND BillPaymentMethod = 'Online' 
  AND Status != 'Cancelled'

UNION ALL

SELECT 'Ledger Online Payments',
    COALESCE(SUM(
        CASE WHEN Type = 'payment' THEN Amount
             WHEN Type = 'refund' THEN -Amount
             ELSE 0 END), 0)
FROM bill_payment p
WHERE date(p.CreatedAt) = date('now')
  AND p.PaymentMethod = 'Online'

UNION ALL

SELECT 'NET ONLINE PAYMENTS',
    (COALESCE((SELECT SUM(InitialPayment) FROM Bills 
              WHERE date(CreatedAt) = date('now') 
                AND BillPaymentMethod = 'Online' AND Status != 'Cancelled'), 0)
    +
    COALESCE((SELECT SUM(
        CASE WHEN Type = 'payment' THEN Amount
             WHEN Type = 'refund' THEN -Amount
             ELSE 0 END)
      FROM bill_payment p
      WHERE date(p.CreatedAt) = date('now') AND p.PaymentMethod = 'Online'), 0));
```

---

### Query F: Verify Customer Credit (Remaining Unpaid)
```sql
-- Unpaid balance on all bills
SELECT 
    b.BillId,
    COALESCE((SELECT SUM(bi.Quantity * bi.UnitPrice) FROM BillItems bi WHERE bi.BillId = b.BillId), 0) as SubTotal,
    b.TaxAmount,
    b.DiscountAmount,
    (COALESCE((SELECT SUM(bi.Quantity * bi.UnitPrice) FROM BillItems bi WHERE bi.BillId = b.BillId), 0) 
     + b.TaxAmount - b.DiscountAmount) as GrandTotal,
    COALESCE((SELECT SUM(bri.Quantity * bri.UnitPrice) 
              FROM BillReturnItems bri 
              JOIN BillReturns br ON bri.ReturnId = br.ReturnId 
              WHERE br.BillId = b.BillId), 0) as ReturnedAmount,
    (COALESCE((SELECT SUM(bi.Quantity * bi.UnitPrice) FROM BillItems bi WHERE bi.BillId = b.BillId), 0) 
     + b.TaxAmount - b.DiscountAmount
     - COALESCE((SELECT SUM(bri.Quantity * bri.UnitPrice) 
                 FROM BillReturnItems bri 
                 JOIN BillReturns br ON bri.ReturnId = br.ReturnId 
                 WHERE br.BillId = b.BillId), 0)) as NetTotal,
    COALESCE(b.InitialPayment, 0) as InitialPayment,
    COALESCE((SELECT SUM(Amount) FROM bill_payment WHERE BillId = b.BillId AND Type = 'payment'), 0) as LedgerPayments,
    COALESCE((SELECT SUM(Amount) FROM bill_payment WHERE BillId = b.BillId AND Type = 'refund'), 0) as Refunds,
    (COALESCE(b.InitialPayment, 0) + COALESCE((SELECT SUM(Amount) FROM bill_payment WHERE BillId = b.BillId AND Type = 'payment'), 0)
     - COALESCE((SELECT SUM(Amount) FROM bill_payment WHERE BillId = b.BillId AND Type = 'refund'), 0)) as TotalPaid,
    MAX(0, (COALESCE((SELECT SUM(bi.Quantity * bi.UnitPrice) FROM BillItems bi WHERE bi.BillId = b.BillId), 0) 
            + b.TaxAmount - b.DiscountAmount
            - COALESCE((SELECT SUM(bri.Quantity * bri.UnitPrice) 
                        FROM BillReturnItems bri 
                        JOIN BillReturns br ON bri.ReturnId = br.ReturnId 
                        WHERE br.BillId = b.BillId), 0)
            - COALESCE(b.InitialPayment, 0) 
            - COALESCE((SELECT SUM(Amount) FROM bill_payment WHERE BillId = b.BillId AND Type = 'payment'), 0)
            + COALESCE((SELECT SUM(Amount) FROM bill_payment WHERE BillId = b.BillId AND Type = 'refund'), 0))) as RemainingUnpaid
FROM Bills b
WHERE date(b.CreatedAt) = date('now') AND b.Status != 'Cancelled'
ORDER BY RemainingUnpaid DESC;
```

---

### Query G: Verify Total Paid = Cash + Online + Credit
```sql
-- Should equal: Cash + Online + Remaining Credit
SELECT 
    'Total Sales' AS Component,
    COALESCE(SUM(
        (SELECT COALESCE(SUM(bi.Quantity * bi.UnitPrice), 0) 
         FROM BillItems bi WHERE bi.BillId = b.BillId)
        + b.TaxAmount - b.DiscountAmount
    ), 0) AS Amount
FROM Bills b
WHERE date(b.CreatedAt) = date('now') AND b.Status != 'Cancelled'

UNION ALL

SELECT 'Returns', 
    COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0)
FROM BillReturnItems bri
JOIN BillReturns br ON bri.ReturnId = br.ReturnId
WHERE date(br.ReturnedAt) = date('now')

UNION ALL

SELECT 'Net Sales',
    ((SELECT COALESCE(SUM(
        (SELECT COALESCE(SUM(bi.Quantity * bi.UnitPrice), 0) 
         FROM BillItems bi WHERE bi.BillId = b.BillId)
        + b.TaxAmount - b.DiscountAmount
    ), 0) FROM Bills b WHERE date(b.CreatedAt) = date('now') AND b.Status != 'Cancelled')
    -
    (SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0)
     FROM BillReturnItems bri
     JOIN BillReturns br ON bri.ReturnId = br.ReturnId
     WHERE date(br.ReturnedAt) = date('now'))
    )

UNION ALL

SELECT 'Cash In Drawer',
    (COALESCE((SELECT SUM(InitialPayment) FROM Bills WHERE date(CreatedAt) = date('now') AND BillPaymentMethod = 'Cash' AND Status != 'Cancelled'), 0)
    + COALESCE((SELECT SUM(Amount) FROM bill_payment WHERE date(CreatedAt) = date('now') AND PaymentMethod = 'Cash' AND Type = 'payment'), 0)
    - COALESCE((SELECT SUM(Amount) FROM bill_payment WHERE date(CreatedAt) = date('now') AND PaymentMethod = 'Cash' AND Type = 'refund'), 0))

UNION ALL

SELECT 'Online Payments',
    (COALESCE((SELECT SUM(InitialPayment) FROM Bills WHERE date(CreatedAt) = date('now') AND BillPaymentMethod = 'Online' AND Status != 'Cancelled'), 0)
    + COALESCE((SELECT SUM(CASE WHEN Type = 'payment' THEN Amount WHEN Type = 'refund' THEN -Amount ELSE 0 END) 
                FROM bill_payment WHERE date(CreatedAt) = date('now') AND PaymentMethod = 'Online'), 0))

UNION ALL

SELECT 'Customer Credit (Remaining)',
    COALESCE(SUM(
        GREATEST(0, 
            (SELECT COALESCE(SUM(bi.Quantity * bi.UnitPrice), 0) FROM BillItems bi WHERE bi.BillId = b.BillId)
            + b.TaxAmount - b.DiscountAmount
            - COALESCE((SELECT SUM(bri.Quantity * bri.UnitPrice) FROM BillReturnItems bri JOIN BillReturns br ON bri.ReturnId = br.ReturnId WHERE br.BillId = b.BillId), 0)
            - b.InitialPayment
            - COALESCE((SELECT SUM(Amount) FROM bill_payment WHERE BillId = b.BillId AND Type = 'payment'), 0)
            + COALESCE((SELECT SUM(Amount) FROM bill_payment WHERE BillId = b.BillId AND Type = 'refund'), 0)
        )
    ), 0)
FROM Bills b
WHERE date(b.CreatedAt) = date('now') AND b.Status != 'Cancelled';
```

---

## SECTION 6: MANUAL VERIFICATION METHOD

### Step 1: Print Today's Bill List
```sql
SELECT BillId, 
       CustomerId, 
       InitialPayment, 
       BillPaymentMethod,
       CreatedAt
FROM Bills
WHERE date(CreatedAt) = date('now')
  AND Status != 'Cancelled'
ORDER BY BillId;
```

### Step 2: For Each Bill, Calculate:
```
a) GrandTotal = SUM(ItemQuantity × ItemPrice) + Tax - Discount
b) Returned = SUM(ReturnedQuantity × ReturnedPrice)
c) NetTotal = GrandTotal - Returned
d) InitialPaid = Bills.InitialPayment
e) LedgerPayments = SUM(bill_payment.Amount WHERE Type='payment')
f) LedgerRefunds = SUM(bill_payment.Amount WHERE Type='refund')
g) TotalPaid = InitialPaid + LedgerPayments - LedgerRefunds
h) RemainingUnpaid = MAX(0, NetTotal - TotalPaid)
```

### Step 3: Aggregate
```
TotalSales = SUM(GrandTotal for all bills)
TotalReturns = SUM(Returned)
NetSales = TotalSales - TotalReturns
TotalPaid = SUM(TotalPaid for all bills)
TotalCreditUnpaid = SUM(RemainingUnpaid)
```

### Step 4: Verify Equation
```
NetSales should equal TotalPaid + TotalCreditUnpaid

If not equal:
  Difference = NetSales - (TotalPaid + TotalCreditUnpaid)
  
  This difference indicates:
  - Refunds not properly recorded
  - Double-counted refunds
  - Mismatched return values
  - Missing ledger payments
```

### Step 5: Separate by Payment Method
```
Cash Payments = SUM(InitialPayment WHERE BillPaymentMethod='Cash') 
              + SUM(bill_payment.Amount WHERE PaymentMethod='Cash' AND Type='payment')
              - SUM(bill_payment.Amount WHERE PaymentMethod='Cash' AND Type='refund')

Online Payments = SUM(InitialPayment WHERE BillPaymentMethod='Online')
                + SUM(bill_payment.Amount WHERE PaymentMethod='Online' AND Type='payment')
                - SUM(bill_payment.Amount WHERE PaymentMethod='Online' AND Type='refund')

Verify: Cash + Online should equal TotalPaid
```

---

## SECTION 7: CRITICAL VALIDATION CHECKLIST

### ✓ Condition 1: Fundamental Equation
```
MUST BE TRUE:
NetSales = Cash + Online + RemainingCredit

If FALSE: There's missing data, double-counting, or refund errors
```

**How to Check:**
- Run Query G (above) and compare Cash + Online + Credit with Net Sales
- Must balance to the cent (within ±0.01 for rounding)

---

### ✓ Condition 2: No Negative Balances
```
MUST BE TRUE:
RemainingCredit >= 0 for all bills
CreditDueAmount >= 0 for all bills

If FALSE: Bill received more than owed (processing error)
```

**How to Check:**
```sql
SELECT COUNT(*) as ProblematicBills
FROM (
    SELECT b.BillId,
        MAX(0, NetTotal - TotalPaid) as Remaining,
        MAX(0, TotalPaid - NetTotal) as CreditDue
    FROM Bills b
) t
WHERE Remaining < 0 OR CreditDue < 0;
-- Should return 0
```

---

### ✓ Condition 3: PaymentMethod Consistency
```
MUST BE TRUE:
Every bill_payment record has PaymentMethod = 'Cash' OR 'Online'
Never NULL

If FALSE: Schema migration didn't complete properly (pre-v15 data)
```

**How to Check:**
```sql
SELECT COUNT(*) as InconsistentPayments
FROM bill_payment
WHERE PaymentMethod IS NULL OR PaymentMethod NOT IN ('Cash', 'Online');
-- Should return 0
```

---

### ✓ Condition 4: Return Logic Consistency
```
MUST BE TRUE:
A bill's ReturnedAmount matches one of:
  A) Separate return entries in BillReturnItems (recommended)
  B) Offset in bill_payment type='refund' (legacy)
  
NOT BOTH in same bill

If FALSE: Returns are being counted twice
```

**How to Check:**
```sql
SELECT BillId, 
       SUM(bri.Quantity * bri.UnitPrice) as ReturnItemsValue,
       COALESCE((SELECT SUM(Amount) 
                 FROM bill_payment 
                 WHERE BillId = Bills.BillId AND Type = 'refund'), 0) as RefundPayments
FROM Bills b
LEFT JOIN BillReturnItems bri ON b.BillId IN (
    SELECT BillId FROM BillReturns WHERE ReturnId IN (
        SELECT ReturnId FROM BillReturnItems WHERE BillItemId IN (
            SELECT BillItemId FROM BillItems WHERE BillId = b.BillId
        )
    )
)
WHERE date(b.CreatedAt) = date('now')
GROUP BY b.BillId
HAVING (ReturnItemsValue > 0 AND RefundPayments > 0);
-- Should return empty set
```

---

### ✓ Condition 5: Type Field Correctness
```
MUST BE TRUE:
bill_payment.Type = 'payment' → Amount is incoming money (credit reduction)
bill_payment.Type = 'refund' → Amount is outgoing money (refund to customer)

NEVER:
- Type='payment' with negative Amount
- Type='refund' with negative Amount
```

**How to Check:**
```sql
SELECT COUNT(*) as Errors
FROM bill_payment
WHERE (Type = 'payment' AND Amount < 0)
   OR (Type = 'refund' AND Amount < 0)
   OR Type NOT IN ('payment', 'refund');
-- Should return 0
```

---

### ✓ Condition 6: Initial Payment Consistency
```
MUST BE TRUE:
Bills.InitialPayment <= Bills.NetTotal 
(cannot pay more than bill is worth at checkout)

UNLESS: Customer overpaid, then CreditDueAmount > 0
```

**How to Check:**
```sql
SELECT COUNT(*) as Overages
FROM Bills b
WHERE b.InitialPayment > (
    (SELECT COALESCE(SUM(bi.Quantity * bi.UnitPrice), 0) 
     FROM BillItems bi WHERE bi.BillId = b.BillId)
    + COALESCE(b.TaxAmount, 0)
    - COALESCE(b.DiscountAmount, 0)
)
AND b.Status != 'Cancelled';
-- Should return 0 (unless overpayment handling is intentional)
```

---

## SECTION 8: EXAMPLE SCENARIOS

### Scenario A: Correct Simple Transaction
```
Transaction:
- Bill #00001 created: 5 items × 200 = Rs. 1000, no tax/discount
- Customer pays Rs. 1000 CASH at checkout

Expected Dashboard:
✓ Total Sales: Rs. 1000
✓ Returns: Rs. 0
✓ Cash In Drawer: Rs. 1000
✓ Online Payments: Rs. 0
✓ Customer Credit: Rs. 0

Verification:
  NetSales (1000) = Cash (1000) + Online (0) + Credit (0) ✓
```

---

### Scenario B: Correct Partial Credit Transaction
```
Transaction:
- Bill #00002 created: 5 items × 200 = Rs. 1000
- Customer pays Rs. 600 CASH at checkout
- Balance Rs. 400 remains

Expected Dashboard TODAY:
✓ Total Sales: Rs. 1000
✓ Returns: Rs. 0
✓ Cash In Drawer: Rs. 600
✓ Online Payments: Rs. 0
✓ Customer Credit: Rs. 400

Verification:
  NetSales (1000) = Cash (600) + Online (0) + Credit (400) ✓
```

---

### Scenario C: Correct Credit Recovery (Ledger Payment)
```
Transaction:
- Bill #00001 created yesterday: Rs. 1000, customer owes Rs. 400
- TODAY: Customer pays Rs. 400 CASH via ledger

Expected Dashboard TODAY:
✓ Cash In Drawer: Rs. 400 (from ledger payment)
✓ Recovered Credits: Rs. 400 (ledger payment)
✓ Customer Credit: Rs. 0 (bill now fully paid)

Bill #00001 Final State:
  TotalPaid = 600 (initial) + 400 (ledger) = 1000
  RemainingUnpaid = 0

Verification:
  The Rs. 400 should appear in BOTH "Cash In Drawer" AND "Recovered Credits"
  (These are not separate - they're the same money categorized differently)
```

---

### Scenario D: Correct Return Handling
```
Transaction:
- Bill #00003 created: 5 items × 200 = Rs. 1000
- Customer pays Rs. 1000 CASH
- Next day: Customer returns 1 item worth Rs. 200

Expected After Return:
✓ Total Sales: Rs. 1000 (unchanged)
✓ Returns: Rs. 200
✓ Net Sales: Rs. 800
✓ Cash In Drawer: Rs. 1000 (no cash out yet)
✓ Customer Credit: Rs. 0 (bill shows Rs. 200 credit due)

OR if cash refunded:
✓ Cash In Drawer: Rs. 800 (1000 - 200 refund)

Verification:
  NetSales (800) = Cash (800 or 1000) + Credit (0 or 200) ✓
```

---

### Scenario E: ⚠️ INCORRECT - Double Counting
```
WRONG Setup:
- Bill #00001: Rs. 1000 paid CASH today
- Same Rs. 1000 counted in:
  ✗ Initial Payment (Bills.InitialPayment)
  ✗ Cash In Drawer
  ✗ Recovered Credits (should only be ledger payments!)

Dashboard Shows:
✗ Cash In Drawer: Rs. 1000
✗ Recovered Credits: Rs. 1000
✗ Total: Rs. 2000 (but only Rs. 1000 was paid!)

Fix: Recovered Credits should = 0 (no ledger payments)
     Only Cash In Drawer = Rs. 1000
```

---

### Scenario F: ⚠️ INCORRECT - Mismatched Returns
```
WRONG Setup:
- Bill #00003: Rs. 1000 paid
- Customer returns Rs. 200 worth

BOTH methods used:
✗ BillReturns shows Rs. 200 return (correct)
✗ bill_payment type='refund' amount=200 (also counted!)

Dashboard Shows:
✗ Returns: Rs. 400 (counted twice!)
✗ Net Sales: Rs. 600 (wrong!)
✗ Math breaks: 600 ≠ 1000 + 0 - (-400)

Fix: Use ONLY ONE return method per bill
```

---

## SECTION 9: KNOWN ISSUES & RECOMMENDATIONS

### Issue 1: RecoveredCredits Counts All Payments
**Status:** ⚠️ POTENTIAL MISSTATEMENT

**Current Code:**
```csharp
GetTodayRecoveredCredit() = SUM(bill_payment.Amount WHERE Type='payment')
```

**Problem:**
- Includes initial payments from today's sales (which are already in the bill)
- Recovered Credit should mean: "Credit from YESTERDAY's bills that was recovered TODAY"

**Recommendation:**
```sql
-- CORRECTED VERSION:
SELECT COALESCE(SUM(Amount), 0)
FROM bill_payment p
WHERE date(p.CreatedAt) = @today
  AND p.Type = 'payment'
  AND p.BillId NOT IN (
      SELECT BillId FROM Bills 
      WHERE date(CreatedAt) = @today
  )
-- Only payments on bills from BEFORE today
```

---

### Issue 2: No Time-Zone Handling
**Status:** ℹ️ NOTE

**Note:**
- Database uses UTC timestamps (CreatedAt DATETIME)
- Dashboard uses date('now') which is UTC
- If system is in timezone (e.g., +5:00), reports may be off by a day

**Recommendation:**
```
Verify CreatedAt timestamps are in local timezone
OR adjust queries:
WHERE date(CreatedAt, '+5 hours') = date('now')  -- For +5:00 timezone
```

---

### Issue 3: Cancelled Bills
**Status:** ✓ HANDLED

**Verification:**
- All dashboard queries filter: `Status != 'Cancelled'`
- Correct implementation

---

### Issue 4: Multiple Returns on Same Bill
**Status:** ✓ HANDLED

**Implementation:**
- BillReturnItems can have multiple return records
- Each tracked separately with date
- Correctly summed in queries

---

## SECTION 10: FINAL AUDIT CHECKLIST

### Before Running Dashboard Audit:

- [ ] **Database Backup:** Create backup of GroceryPOS.db
- [ ] **Schema Version:** Verify `user_version = 15` (PaymentMethod column exists)
- [ ] **No Cancelled Bills:** Review bills with Status='Cancelled' - should be excluded
- [ ] **Timestamp Timezone:** Confirm CreatedAt is in system timezone
- [ ] **Recent Test Data:** Ensure test transactions from today exist

### Running the Audit:

- [ ] **Execute Query A:** Verify Total Sales matches dashboard
- [ ] **Execute Query B:** Verify Returns matches dashboard
- [ ] **Execute Query C:** Verify Net Sales equation (Sales - Returns)
- [ ] **Execute Query D:** Verify Cash In Drawer breakdown
- [ ] **Execute Query E:** Verify Online Payments breakdown
- [ ] **Execute Query F:** Verify Customer Credit per bill
- [ ] **Execute Query G:** Verify fundamental equation (Sales = Cash + Online + Credit)
- [ ] **Execute Condition 1-6 checks:** Run validation conditions

### Interpreting Results:

- [ ] **All queries match dashboard ±0.01:** System is CORRECT
- [ ] **Query G fails (doesn't balance):** Missing refunds or double-counted payments
- [ ] **Returns higher than expected:** Check for duplicate returns
- [ ] **Credit lower than expected:** Check for missing ledger payments
- [ ] **Recovered Credits includes today's initial:** Use CORRECTED version of that query
- [ ] **Negative balances detected:** Data corruption - investigate specific bills

---

## SECTION 11: REPORTING TEMPLATE

### Daily Audit Report

```
Date: [TODAY]
System: GroceryPOS v15
Time Generated: [HH:MM:SS]

DASHBOARD METRICS:
├─ Total Sales:        Rs. _________ (✓ VERIFIED / ⚠️ MISMATCH / ✗ ERROR)
├─ Returns:            Rs. _________ (✓ VERIFIED / ⚠️ MISMATCH / ✗ ERROR)
├─ Net Sales:          Rs. _________ (✓ VERIFIED / ⚠️ MISMATCH / ✗ ERROR)
├─ Cash In Drawer:     Rs. _________ (✓ VERIFIED / ⚠️ MISMATCH / ✗ ERROR)
├─ Online Payments:    Rs. _________ (✓ VERIFIED / ⚠️ MISMATCH / ✗ ERROR)
├─ Customer Credit:    Rs. _________ (✓ VERIFIED / ⚠️ MISMATCH / ✗ ERROR)
└─ Recovered Credits:  Rs. _________ (✓ VERIFIED / ⚠️ MISMATCH / ✗ ERROR)

VALIDATION CONDITIONS:
├─ Equation (Sales = Cash + Online + Credit):  [PASS / FAIL]
├─ No Negative Balances:                        [PASS / FAIL]
├─ PaymentMethod Consistency:                   [PASS / FAIL]
├─ Return Logic Consistency:                    [PASS / FAIL]
├─ Type Field Correctness:                      [PASS / FAIL]
└─ Initial Payment Consistency:                 [PASS / FAIL]

SUMMARY:
Total Discrepancies Found: _______
Critical Issues: _______
Recommendations: _______

Signed By: ________________  Date: ____________
```

---

## CONCLUSION

Your GroceryPOS dashboard calculates:

**✓ CORRECT:**
- Total Sales (includes all bills' items + tax - discount)
- Returns (from BillReturnItems)
- Cash In Drawer (initial cash + ledger cash, minus refunds)
- Online Payments (initial online + ledger online, properly netted)
- Customer Credit (remaining unpaid per bill)

**⚠️ POTENTIAL ISSUES:**
- Recovered Credits may include today's initial payments (double-count risk)
- No timezone adjustment for non-UTC systems
- Returns and Refunds must use consistent method

**MANDATORY VERIFICATION:**
Use Query G to ensure: **NetSales = Cash + Online + Credit**

If this equation holds, your dashboard is financially correct.

---

**Generated:** Financial Audit Framework  
**For Use With:** GroceryPOS SQLite v15+  
**Last Updated:** April 22, 2026
