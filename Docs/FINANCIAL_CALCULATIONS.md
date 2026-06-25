# GroceryPOS Financial Calculations - Complete Validation Guide

**Version:** 2.0 (After Database Optimization & Clean)  
**Date:** April 22, 2026  
**Status:** ✓ Production-Ready

---

## Table of Contents

1. [Bill Calculation Formula](#1-bill-calculation-formula)
2. [Daily Dashboard Metrics](#2-daily-dashboard-metrics)
3. [Return & Refund Logic](#3-return--refund-logic)
4. [Payment Method Breakdown](#4-payment-method-breakdown)
5. [Verification Checklist](#5-verification-checklist)
6. [Testing Scenarios](#6-testing-scenarios)
7. [Database Views & Reports](#7-database-views--reports)

---

## 1. Bill Calculation Formula

### Core Formula (Per Bill)

Each bill goes through the following calculation flow:

```
STEP 1: Calculate SubTotal
  SubTotal = Σ(ItemQuantity × ItemPrice) - ItemDiscounts
             for all items on the bill

STEP 2: Calculate GrandTotal (Bill Total)
  GrandTotal = SubTotal + TaxAmount - BillLevelDiscount
  
  Example:
    Item 1: 2 × 100 = 200
    Item 2: 1 × 150 = 150
    SubTotal = 350
    Tax = 17.50
    Bill Discount = 10
    GrandTotal = 350 + 17.50 - 10 = 357.50

STEP 3: Calculate NetTotal (After Returns)
  NetTotal = GrandTotal - ReturnedAmount
  
  Example:
    If 1 unit of Item 1 was returned:
    ReturnedAmount = 1 × 100 = 100
    NetTotal = 357.50 - 100 = 257.50

STEP 4: Calculate Paid Amount
  PaidAmount = InitialPayment 
             + Σ(CreditPayments received after sale)
             - Σ(Refunds issued)
  
  Example:
    Initial payment at checkout: 200
    Subsequent payment (next day): 50
    Refund issued: 0
    PaidAmount = 200 + 50 - 0 = 250

STEP 5: Calculate Remaining Amount (Customer Owes)
  RemainingAmount = MAX(0, NetTotal - PaidAmount)
  
  Example:
    NetTotal = 257.50
    PaidAmount = 250
    RemainingAmount = 257.50 - 250 = 7.50 (customer owes)

STEP 6: Calculate Credit Due Amount (Overpayment)
  CreditDueAmount = MAX(0, PaidAmount - NetTotal)
  
  Example:
    If PaidAmount was 300 instead:
    CreditDueAmount = 300 - 257.50 = 42.50 (customer is owed)
```

### Validation Rules

✓ **RemainingAmount & CreditDueAmount are mutually exclusive:**
  - If RemainingAmount > 0, then CreditDueAmount = 0
  - If CreditDueAmount > 0, then RemainingAmount = 0
  - At least one should always equal 0 (except edge cases with rounding)

✓ **Payment Status (2-State):**
  - "Paid" when RemainingAmount ≤ 0.01 (accounting for rounding)
  - "PartialPaid" when RemainingAmount > 0.01

✓ **Return Consistency:**
  - ReturnedAmount can never exceed GrandTotal
  - NetTotal should never go negative
  - All returns must reference valid BillItems

---

## 2. Daily Dashboard Metrics

### Daily Total Sales

```
Total Sales = Σ(GrandTotal for all bills created today)
            = Σ(SubTotal + Tax - Discount)

WHERE:
  - Bills.CreatedAt is TODAY
  - Bills.Status ≠ 'Cancelled'
  - Excludes returns (BillReturns has separate table)

SQL Calculation:
  SELECT SUM(
      COALESCE((SELECT SUM(bi.Quantity * bi.UnitPrice)
                FROM BillItems bi
                WHERE bi.BillId = b.BillId), 0)
      + COALESCE(b.TaxAmount, 0)
      - COALESCE(b.DiscountAmount, 0)
  ) FROM Bills b
  WHERE date(b.CreatedAt) = TODAY
    AND b.Status ≠ 'Cancelled';
```

### Daily Returns Total

```
Returns = Σ(ReturnValue for all returns today)
        = Σ(UnitPrice × ReturnedQuantity)

WHERE:
  - BillReturns.ReturnedAt is TODAY
  - Only counts items actually returned

SQL Calculation:
  SELECT SUM(bri.Quantity * bri.UnitPrice)
  FROM BillReturnItems bri
  JOIN BillReturns br ON bri.ReturnId = br.ReturnId
  WHERE date(br.ReturnedAt) = TODAY;
```

### Daily Net Sales

```
Net Sales = Total Sales - Returns

Example:
  Total Sales: 1000
  Returns: 150
  Net Sales: 850
```

### Daily Cash Collected

```
Cash In Drawer = Initial Cash Today 
               + Subsequent Cash Payments Today
               - Cash Refunds Today

STEP-BY-STEP:

1. Initial Cash (from bills created today)
   Initial Cash = Σ(InitialPayment from Bills)
   WHERE:
     - Bills.CreatedAt = TODAY
     - Bills.BillPaymentMethod = 'Cash'
     - Bills.Status ≠ 'Cancelled'

2. Subsequent Cash Payments (from existing bills paid today)
   Subsequent Cash = Σ(bill_payment.Amount)
   WHERE:
     - bill_payment.CreatedAt = TODAY
     - bill_payment.PaymentMethod = 'Cash'
     - bill_payment.Type = 'Payment'

3. Cash Refunds (returned today)
   Cash Refunds = Σ(bill_payment.Amount)
   WHERE:
     - bill_payment.CreatedAt = TODAY
     - bill_payment.PaymentMethod = 'Cash'
     - bill_payment.Type = 'Refund'

4. Final Calculation
   Cash In Drawer = Initial Cash + Subsequent - Refunds

Example:
  Initial Cash: 300
  Subsequent Payments: 150
  Refunds: 50
  Cash In Drawer: 300 + 150 - 50 = 400
```

### Daily Credit Owed by Customers

```
Customer Credit = Σ(RemainingAmount for all bills)

WHERE:
  - Bills.Status ≠ 'Cancelled'
  - RemainingAmount > 0
  - Includes all outstanding balances regardless of creation date

Calculation for each bill:
  RemainingAmount = MAX(0, NetTotal - PaidAmount)

Formula:
  SELECT SUM(MAX(0, 
    (GrandTotal - Returns) - (Initial + Subsequent Payments - Refunds)
  )) FROM Bills
```

### Daily Recovered Credit

```
Recovered Credit = Σ(CreditPayment.Amount received today)

WHERE:
  - bill_payment.CreatedAt = TODAY
  - bill_payment.Type = 'Payment'
  - The bill was created BEFORE today (recovery of old credit)
  - bill_payment.CreatedAt > bill.CreatedAt (payment after sale)
  - Bill.Status ≠ 'Cancelled'
  - Bill had outstanding balance at creation time

This metric tracks: Yesterday's credit that was paid today
```

### Daily Online Payments

```
Online Payments = Σ(Online payment transactions today)

1. Initial Online Payments (bills created as Online today)
   Initial Online = Σ(InitialPayment from Bills)
   WHERE:
     - Bills.CreatedAt = TODAY
     - Bills.BillPaymentMethod = 'Online'
     - Bills.Status ≠ 'Cancelled'

2. Subsequent Online Payments/Refunds
   Subsequent Online = Σ(bill_payment.Amount)
   WHERE:
     - bill_payment.CreatedAt = TODAY
     - bill_payment.PaymentMethod = 'Online'
     - (Type = 'Payment' adds, Type = 'Refund' subtracts)

Online Breakdown by Sub-Method:
  - Easypaisa (OnlinePaymentMethod = 'Easypaisa')
  - JazzCash (OnlinePaymentMethod = 'JazzCash')
  - Bank Transfer (OnlinePaymentMethod = 'BankTransfer')
```

---

## 3. Return & Refund Logic

### Return Transaction Types

#### TYPE A: Return with Cash Refund (Change)
```
Customer returns items → Receives cash immediately

Action:
  1. Create BillReturn header with ReturnId
  2. Add items to BillReturnItems with UnitPrice × ReturnQuantity
  3. Record bill_payment transaction (Type = 'Refund', PaymentMethod = 'Cash')
  4. Reduce PaidAmount and RemainingAmount accordingly

Impact:
  - Cash out of drawer (negative for cash metric)
  - NetTotal reduced by return value
  - RemainingAmount updated

Example:
  Original Bill:
    GrandTotal: 200
    Paid: 100 (partial)
    Remaining: 100
  
  Return: 50 worth of items
    ReturnedAmount: 50
    NetTotal: 200 - 50 = 150
    
    If refunding in cash:
      Refund: 50 (or partial refund based on paid ratio)
      New Remaining: 100 (or adjusted based on payment policy)
```

#### TYPE B: Return with Store Credit
```
Customer returns items → Credit applied to account

Action:
  1. Create BillReturn header with StoreCreditIssued = ReturnValue
  2. Add items to BillReturnItems
  3. Do NOT record payment (keep as pending)
  4. Update PaidAmount via StoreCreditRefundedAt logic

Impact:
  - No cash out
  - Customer can use credit on next purchase
  - RemainingAmount reduced by store credit
```

### Return Validation Rules

✓ **Cannot return more than was purchased:**
  - Total ReturnedAmount ≤ GrandTotal

✓ **Must reference valid BillItems:**
  - Each BillReturnItem.BillItemId must exist in BillItems

✓ **Quantity checks:**
  - ReturnedQuantity ≤ OriginalQuantity for each item

✓ **Return should reduce outstanding balance:**
  - If bill was partial paid, return should prioritize unpaid portion

---

## 4. Payment Method Breakdown

### Cash Payments

```
Cash is tracked in three contexts:

1. At Bill Creation:
   Bills.InitialPayment recorded with Bills.BillPaymentMethod = 'Cash'

2. Subsequent Payments:
   bill_payment records with PaymentMethod = 'Cash' and Type = 'Payment'

3. Refunds:
   bill_payment records with PaymentMethod = 'Cash' and Type = 'Refund'

Daily Cash Metric = Sum all cash flows above
```

### Online Payments (Easypaisa, JazzCash, Bank Transfer)

```
Online payments have additional metadata:

1. At Bill Creation:
   - Bills.BillPaymentMethod = 'Online'
   - Bills.OnlinePaymentMethod = 'Easypaisa' | 'JazzCash' | 'BankTransfer'
   - Bills.AccountId = reference to receiving account

2. Subsequent Payments:
   - bill_payment.PaymentMethod = 'Online'
   - bill_payment.OnlinePaymentMethod = sub-method (optional)
   - bill_payment.AccountId = receiving account

Daily Online Breakdown:
  Easypaisa Total   = SUM where OnlinePaymentMethod = 'Easypaisa'
  JazzCash Total    = SUM where OnlinePaymentMethod = 'JazzCash'
  BankTransfer Total = SUM where OnlinePaymentMethod = 'BankTransfer'
```

---

## 5. Verification Checklist

Use this checklist to verify all calculations are correct:

### Daily Verification

- [ ] Total Sales matches SUM of all bills' GrandTotal
- [ ] Returns matches SUM of all BillReturnItems value
- [ ] Net Sales = Total Sales - Returns
- [ ] Cash In Drawer = Initial + Subsequent - Refunds
- [ ] Cash In Drawer ≤ Total Sales (sanity check)
- [ ] Customer Credit Sum = SUM of all RemainingAmounts
- [ ] Payment Methods Sum = Cash + Online + Other
- [ ] Recovered Credit does not include day-of sales

### Per-Bill Verification

For each bill, verify:

- [ ] SubTotal = SUM(Quantity × UnitPrice - ItemDiscount)
- [ ] GrandTotal = SubTotal + Tax - BillDiscount
- [ ] NetTotal = GrandTotal - ReturnedAmount
- [ ] PaidAmount = InitialPayment + Payments - Refunds
- [ ] RemainingAmount = MAX(0, NetTotal - PaidAmount)
- [ ] CreditDueAmount = MAX(0, PaidAmount - NetTotal)
- [ ] RemainingAmount + CreditDueAmount ≠ both > 0

### Return Verification

For each return, verify:

- [ ] ReturnedAmount ≤ GrandTotal
- [ ] All BillReturnItems reference valid BillItems
- [ ] Total Returned ≤ Original Quantity for each item
- [ ] NetTotal = GrandTotal - ReturnedAmount (≥ 0)
- [ ] Refund amount doesn't exceed returned value

### Payment Verification

For each payment, verify:

- [ ] Amount ≤ Remaining Balance (for payment)
- [ ] Amount ≤ Overpaid amount (for refund)
- [ ] Payment method is consistent
- [ ] Payment date is later than bill date

---

## 6. Testing Scenarios

### Scenario 1: Simple Cash Sale (Full Payment)

```
Bill:
  Item: 1 × 100 = 100
  Tax: 10
  Discount: 0
  GrandTotal: 110
  
Payment:
  Cash Received: 110
  Change: 0
  InitialPayment: 110
  
Result:
  SubTotal: 100
  GrandTotal: 110
  NetTotal: 110 (no returns)
  PaidAmount: 110
  RemainingAmount: 0
  Status: Paid ✓
```

### Scenario 2: Partial Payment (Credit Sale)

```
Bill:
  Item: 2 × 100 = 200
  Item: 1 × 50 = 50
  SubTotal: 250
  Tax: 25
  BillDiscount: 10
  GrandTotal: 265
  
Payment:
  InitialPayment: 100 (customer pays partial)
  
Result:
  NetTotal: 265
  PaidAmount: 100
  RemainingAmount: 165
  Status: PartialPaid ✓
  
Later Payment:
  +50 payment received
  NewPaidAmount: 150
  NewRemainingAmount: 115
```

### Scenario 3: Partial Return

```
Original Bill:
  Item 1: 2 × 100 = 200
  Item 2: 1 × 50 = 50
  SubTotal: 250
  GrandTotal: 250
  InitialPayment: 200
  RemainingAmount: 50
  
Return:
  Return Item 1: 1 × 100 (1 unit of the 2 items)
  ReturnedAmount: 100
  
Result:
  NetTotal: 250 - 100 = 150
  PaidAmount: 200 (unchanged, already paid)
  CreditDueAmount: 200 - 150 = 50 (customer overpaid, gets credit)
  RemainingAmount: 0
  Status: Paid ✓ (with 50 credit issued)
```

### Scenario 4: Return with Cash Refund

```
Original Bill:
  GrandTotal: 300
  InitialPayment: 150 (partial)
  RemainingAmount: 150
  
Return:
  ReturnedAmount: 100
  ReturnedCash: 100
  
Result:
  NetTotal: 300 - 100 = 200
  PaidAmount: 150
  RemainingAmount: 50
  Cash Refund: 100 out of drawer
```

---

## 7. Database Views & Reports

### Recommended Reports to Generate

1. **Daily Sales Summary**
   - Total Sales
   - Total Returns
   - Net Sales
   - Cash Collected
   - Online Payments
   - Customer Credit Outstanding

2. **Bill-by-Bill Audit**
   - Invoice Number
   - GrandTotal
   - Paid Amount
   - Remaining Due
   - Status
   - Payment Method
   - Returns (if any)

3. **Payment Method Breakdown**
   - Cash Sales
   - Cash Payments Today
   - Cash Refunds Today
   - Online Transactions (by sub-method)
   - Outstanding Credit

4. **Customer Credit Report**
   - Customer Name
   - Outstanding Balance
   - Bill Count
   - Oldest Bill Date
   - Last Payment Date

### Query Validation

For each dashboard metric, verify:

```sql
-- Test 1: Total Sales Today
SELECT SUM(GrandTotal) FROM Bills
WHERE date(CreatedAt) = TODAY;

-- Test 2: Returns Today
SELECT SUM(ReturnedAmount) FROM BillReturns
WHERE date(ReturnedAt) = TODAY;

-- Test 3: Net Sales
SELECT (SELECT SUM(...) FROM Bills WHERE date(CreatedAt)=TODAY) -
       (SELECT SUM(...) FROM BillReturns WHERE date(ReturnedAt)=TODAY);

-- Test 4: Cash In Drawer
SELECT (Initial + Subsequent - Refunds) FROM (
  SELECT 
    SUM(InitialPayment) as Initial,
    (SELECT SUM(Amount) FROM bill_payment WHERE Type='Payment' AND PaymentMethod='Cash' AND date(CreatedAt)=TODAY) as Subsequent,
    (SELECT SUM(Amount) FROM bill_payment WHERE Type='Refund' AND PaymentMethod='Cash' AND date(CreatedAt)=TODAY) as Refunds
  FROM Bills
  WHERE date(CreatedAt)=TODAY AND BillPaymentMethod='Cash'
);
```

---

## Production Validation Checklist

Before deploying to production, verify:

- [ ] All formulas calculate correctly with test data
- [ ] Dashboard metrics match database totals
- [ ] Returns reduce outstanding balance correctly
- [ ] Payment methods are tracked independently
- [ ] AUTOINCREMENT sequences are reset after database cleanup
- [ ] No orphaned records (BillReturns without Bills, etc.)
- [ ] Rounding errors are handled (use ROUND(..., 2))
- [ ] NULL handling is correct (use COALESCE)
- [ ] Cancelled bills are excluded from totals
- [ ] Staff can retrieve reports for any date range
- [ ] Refund logic prevents double-refunding
- [ ] Credit payments cannot exceed remaining balance

---

## Support & Troubleshooting

### Common Issues

**Issue: Dashboard totals don't match database**
- Check date filtering (should be TODAY, not CURRENT_TIMESTAMP)
- Verify Cancelled bills are excluded
- Check for NULL values (use COALESCE)
- Verify ROUND(..., 2) is applied to all calculations

**Issue: Cash drawer balance is negative**
- Check if refunds exceed collections
- Verify all refunds have corresponding returns
- Check for data entry errors in payment amount

**Issue: Customer credit not tracking correctly**
- Verify RemainingAmount formula
- Check that all payments are recorded
- Verify returns update NetTotal correctly

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 2.0 | 2026-04-22 | Production-ready, AUTOINCREMENT reset, enhanced verification |
| 1.5 | 2026-04-01 | Financial audit pass, formula validation |
| 1.0 | 2026-03-15 | Initial database design |

