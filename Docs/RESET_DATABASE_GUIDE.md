# GroceryPOS Database Reset for Testing

## Overview

This guide explains how to reset **only transactional data** in your GroceryPOS SQLite database while preserving all master data (products, customers, accounts, settings).

Perfect for **testing and quality assurance** without losing your product catalog and customer registry.

## What Gets Deleted (Transactional Data)

✗ **Bills** - Sales invoices  
✗ **BillItems** - Line items from bills  
✗ **bill_payment** - Payment transaction records  
✗ **BillReturns** - Return headers  
✗ **BillReturnItems** - Returned items  
✗ **InventoryLogs** - Stock movement audit trail  
✗ **CustomerLedger** - Customer accounting entries  

## What's Preserved (Master Data)

✓ **Items** - Product catalog (descriptions, prices, stock thresholds)  
✓ **Customers** - Customer registry (names, phone numbers, addresses)  
✓ **Accounts** - Payment methods/bank accounts  
✓ **Users** - System users (Admin/Cashier roles)  
✓ **Categories** - Product categories  
✓ **Database Schema** - All tables and indexes remain intact  

---

## Method 1: Direct SQL Execution

### Using SQLite CLI

```bash
# From your database directory
sqlite3 GroceryPOS.db < reset_transactional_data.sql
```

### Using Database GUI Tools (SQLite Studio, DB Browser, etc.)

1. Open `GroceryPOS.db`
2. Execute the SQL script: `reset_transactional_data.sql`
3. Verify master data is intact with verification queries (commented at end of script)

---

## Method 2: Programmatic Reset (C# Application)

### Quick Usage

```csharp
// Simple reset
var result = DatabaseResetUtility.ResetTransactionalData();
Console.WriteLine(result); // Detailed summary
```

### With AUTOINCREMENT Reset (Optional)

```csharp
// Reset with sequence restart (IDs start from 1 again)
var result = DatabaseResetUtility.ResetTransactionalData(resetAutoIncrement: true);

if (result.IsSuccess)
{
    MessageBox.Show(result.Message);
}
else
{
    MessageBox.Show($"Error: {result.ErrorDetails}");
}
```

### Example Integration (Admin/Debug Menu)

```csharp
// Add to your Admin panel or debug utilities
private void OnResetTransactionalDataClick()
{
    var confirmation = MessageBox.Show(
        "Clear all transactional data (Bills, Payments, Returns)?\n\nMaster data will be preserved.",
        "Reset Transactional Data",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning
    );

    if (confirmation == MessageBoxResult.Yes)
    {
        var result = DatabaseResetUtility.ResetTransactionalData(resetAutoIncrement: false);
        
        // Log the summary
        AppLogger.Info(result.ToString());
        
        // Show result to user
        MessageBox.Show(result.ToString(), "Reset Complete");
        
        // Optionally refresh UI
        RefreshMainWindow();
    }
}
```

---

## Available Files

### 1. **reset_transactional_data.sql**
   - Pure SQL script for direct database execution
   - Includes verification queries (commented)
   - Safe with PRAGMA foreign_keys handling
   - Can be executed manually or by any database tool

### 2. **Data/DatabaseResetUtility.cs**
   - C# helper class for programmatic reset
   - Provides detailed summary with record counts
   - Handles referential integrity
   - Optional AUTOINCREMENT sequence reset
   - Integrated logging via AppLogger

---

## Safety Features

✓ **Foreign Key Enforcement** - Constraints are temporarily disabled during deletion, then re-enabled  
✓ **Referential Integrity** - Deletion order respects table dependencies  
✓ **VACUUM Optimization** - Database is optimized after cleanup  
✓ **Summary Reporting** - Detailed counts of deleted/preserved records  
✓ **Error Handling** - All exceptions are caught and logged  
✓ **Master Data Verification** - System confirms all master data remains intact  

---

## Verification Queries

After reset, run these queries to confirm:

```sql
-- Verify transactional data is cleared
SELECT COUNT(*) as Bills FROM Bills;              -- Should be 0
SELECT COUNT(*) as BillItems FROM BillItems;      -- Should be 0
SELECT COUNT(*) as Payments FROM bill_payment;    -- Should be 0
SELECT COUNT(*) as Returns FROM BillReturns;      -- Should be 0

-- Verify master data is preserved
SELECT COUNT(*) as Items FROM Items;              -- Should show your products
SELECT COUNT(*) as Customers FROM Customers;      -- Should show your customers
SELECT COUNT(*) as Accounts FROM Accounts;        -- Should show your accounts
SELECT COUNT(*) as Users FROM Users;              -- Should show your users
```

---

## Important Notes

⚠️ **Backup First** - Always backup your database before running reset in production  
⚠️ **No Undo** - This operation cannot be undone. Make a backup copy before testing.  
⚠️ **Testing Only** - Use in development/QA environments, not in production without proper backup strategy  
⚠️ **Concurrent Usage** - Ensure no users are accessing the database during reset  

---

## Troubleshooting

### "Foreign Key Constraint Failed"
- The script handles this by temporarily disabling PRAGMA foreign_keys
- Ensure no other connections are accessing the database

### "Database is Locked"
- Close all connections to the database first
- Ensure the application is not running
- Try again after 10-15 seconds

### AUTOINCREMENT Not Resetting
- The script has this commented out (optional feature)
- Uncomment the AUTOINCREMENT reset lines in the SQL if needed
- Or use `resetAutoIncrement: true` parameter in C# method

---

## Example: Complete Testing Workflow

```csharp
public void RunTestingCycle()
{
    // 1. Reset transactional data
    var result = DatabaseResetUtility.ResetTransactionalData();
    
    if (!result.IsSuccess)
    {
        AppLogger.Error($"Reset failed: {result.ErrorDetails}");
        return;
    }
    
    // 2. Verify master data counts
    AppLogger.Info($"Items preserved: {result.ItemsPreserved}");
    AppLogger.Info($"Customers preserved: {result.CustomersPreserved}");
    
    // 3. Run your test scenarios
    RunSalesTest();
    RunReturnTest();
    RunPaymentTest();
    
    // 4. Optionally reset again for next cycle
    // DatabaseResetUtility.ResetTransactionalData();
}
```

---

## Additional References

- **Database Schema**: See `Data/DatabaseInitializer.cs` for complete schema documentation
- **Entity Models**: See `Models/` directory for data model definitions
- **Database Design**: See `DATABASE_DESIGN.md` for architectural details

---

## Support

For issues or questions:
1. Check the verification queries above
2. Review `DatabaseInitializer.cs` for schema details
3. Check application logs in `AppLogger` for detailed errors
4. Ensure database file is not read-only: `Properties → General → Attributes`
