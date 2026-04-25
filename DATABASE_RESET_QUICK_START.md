# GroceryPOS Database Reset - Quick Start

## 🎯 What This Does

Safely clears **transactional data** (sales, returns, payments) while preserving **master data** (products, customers, accounts).

Perfect for testing without losing your product catalog and customer registry.

---

## 📁 New Files Created

| File | Purpose | Usage |
|------|---------|-------|
| `reset_transactional_data.sql` | Raw SQL script | Direct execution via SQLite CLI or GUI tools |
| `Data/DatabaseResetUtility.cs` | C# helper class | Call from your application code |
| `reset-database.ps1` | PowerShell script | Command-line execution on Windows |
| `RESET_DATABASE_GUIDE.md` | Full documentation | Comprehensive guide with examples |
| `DATABASE_RESET_QUICK_START.md` | This file | Quick reference |

---

## ⚡ Quick Start Options

### Option 1: PowerShell (Easiest)

```powershell
# From the GroceryPOS-master directory
./reset-database.ps1

# With options
./reset-database.ps1 -DatabasePath "GroceryPOS.db" -Backup $true -Verify $true
```

### Option 2: SQL Direct Execution

```bash
# Using sqlite3 CLI
sqlite3 GroceryPOS.db < reset_transactional_data.sql

# Using any database GUI tool:
# 1. Open GroceryPOS.db
# 2. Run the SQL script: reset_transactional_data.sql
```

### Option 3: From Your Application (C#)

```csharp
// Add to Admin/Settings page or debug menu
var result = DatabaseResetUtility.ResetTransactionalData();
MessageBox.Show(result.ToString());
```

---

## ✅ What Gets Deleted

- Bills (sales invoices)
- BillItems (line items)
- Payments (bill_payment)
- Returns & Return Items
- Inventory Logs (stock history)
- Customer Ledger (transaction history)

## ✓ What's Preserved

- **Items** (product catalog)
- **Customers** (customer registry)
- **Accounts** (payment methods)
- **Users** (system users)
- **Categories** (product categories)

---

## 🔐 Safety Features

✅ Automatic backup created before reset  
✅ Foreign key integrity maintained  
✅ Database optimized after cleanup  
✅ Detailed summary with record counts  
✅ Verification queries included  

---

## ⚠️ Important

1. **Backup your database first** - Always have a backup
2. **No undo** - This operation cannot be reversed
3. **Close the application** - Ensure no active connections
4. **Test first** - Try on a copy of your database

---

## 🐛 Troubleshooting

**PowerShell error: "sqlite3 not found"**
- Install SQLite CLI: `choco install sqlite` or download from https://www.sqlite.org/download.html
- Or use Option 2 (direct SQL) or Option 3 (C# code)

**"Database is locked" error**
- Close the application completely
- Wait 10-15 seconds and try again

**Reset didn't work?**
- Check `RESET_DATABASE_GUIDE.md` for detailed troubleshooting
- Verify master data with the verification queries in the SQL script

---

## 📊 Verification

After reset, check these:

```sql
-- Transactional data should be 0
SELECT COUNT(*) FROM Bills;

-- Master data should be preserved
SELECT COUNT(*) FROM Items;
SELECT COUNT(*) FROM Customers;
```

---

## 📞 Next Steps

1. Read **`RESET_DATABASE_GUIDE.md`** for complete documentation
2. Choose your preferred reset method (PowerShell, SQL, or C#)
3. Create a backup of your database
4. Execute the reset
5. Verify master data is intact
6. Start testing

---

## 📝 Example: Complete Reset Workflow

```powershell
# 1. Backup current database
Copy-Item GroceryPOS.db GroceryPOS.db.backup

# 2. Execute reset
./reset-database.ps1 -Verify $true

# 3. Verify in application
# Launch GroceryPOS and confirm Products/Customers are still there
# But Bills/Transactions are cleared

# 4. Start testing
# Create new test sales, returns, payments, etc.
```

---

**Need more help?** See `RESET_DATABASE_GUIDE.md` for comprehensive documentation and examples.
