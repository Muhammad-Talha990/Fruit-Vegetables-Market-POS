# GroceryPOS - Professional Setup & Deployment Guide

**Version:** 3.0 (Production-Ready)  
**Last Updated:** April 22, 2026  
**Status:** ✓ Ready for Deployment

---

## Table of Contents

1. [Quick Start](#1-quick-start)
2. [System Requirements](#2-system-requirements)
3. [Development Setup](#3-development-setup)
4. [Database Initialization](#4-database-initialization)
5. [Database Reset Procedures](#5-database-reset-procedures)
6. [Testing Checklist](#6-testing-checklist)
7. [Production Deployment](#7-production-deployment)
8. [Troubleshooting](#8-troubleshooting)
9. [Project Structure](#9-project-structure)
10. [Maintenance & Support](#10-maintenance--support)

---

## 1. Quick Start

### For Developers (Windows)

```bash
# 1. Clone/Extract the repository
cd D:\GroceryPOS-Latest\GroceryPOS-master

# 2. Restore dependencies
dotnet restore

# 3. Build the project
dotnet build GroceryPOS-master.sln

# 4. Run the application
dotnet run --project GroceryPOS.csproj

# 5. Login with default credentials
# Username: admin
# Password: admin
```

### For End-Users

1. Extract the release package
2. Double-click `GroceryPOS.exe`
3. Login with your credentials
4. Dashboard appears with today's metrics

---

## 2. System Requirements

### Minimum Requirements

| Component | Requirement |
|-----------|-------------|
| OS | Windows 10 or higher |
| .NET Runtime | .NET 8.0 (Windows Desktop) |
| RAM | 512 MB minimum |
| Disk Space | 100 MB for application + database |
| Display | 1024×768 minimum |

### Software Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.Data.Sqlite | 8.0.12 | Database access |
| Microsoft.Extensions.DependencyInjection | 10.0.3 | Dependency injection |
| BCrypt.Net-Next | 4.0.3 | Password hashing |
| System.Drawing.Common | 8.0.12 | Thermal receipt printing |

All dependencies are automatically managed via NuGet.

### Development Requirements

- **IDE:** Visual Studio 2022 (or VS Code with C# extension)
- **SDK:** .NET 8.0 SDK
- **Git:** For version control (optional)

---

## 3. Development Setup

### Step 1: Install Prerequisites

```bash
# Check if .NET 8 SDK is installed
dotnet --version

# If not installed, download from: https://dotnet.microsoft.com/download/dotnet/8.0
```

### Step 2: Clone the Repository

```bash
# Using Git (recommended)
git clone <repository-url>
cd GroceryPOS-master

# Or extract from ZIP
# Extract to: D:\GroceryPOS-Latest\GroceryPOS-master
```

### Step 3: Restore Dependencies

```bash
dotnet restore
```

### Step 4: Build the Solution

```bash
# Debug build (faster, with debugging symbols)
dotnet build GroceryPOS-master.sln -c Debug

# Release build (optimized for production)
dotnet build GroceryPOS-master.sln -c Release
```

### Step 5: Run Locally

```bash
dotnet run --project GroceryPOS.csproj
```

The application will:
1. Create the database at: `%APPDATA%\GroceryPOS\GroceryPOS.db`
2. Initialize all tables and seed default data
3. Show the login screen

---

## 4. Database Initialization

### Automatic Initialization (On First Run)

When you start the application for the first time:

1. `DatabaseInitializer` checks if database exists
2. If not, creates all 10 tables (normalized 3NF schema)
3. Initializes indexes for performance
4. Seeds default master data:
   - 1 Admin user (`admin` / `admin`)
   - 1 Default category (uncategorized)
   - Sample products for testing

### Database Location

```
Windows User Profile:
  %APPDATA%\GroceryPOS\GroceryPOS.db
  
Typically:
  C:\Users\<YourUsername>\AppData\Roaming\GroceryPOS\GroceryPOS.db
```

### Database Schema

The database contains 10 normalized (3NF) tables:

1. **Users** - System users (Admin/Cashier roles)
2. **Categories** - Product categories
3. **Items** - Product catalog (prices, stock thresholds)
4. **Customers** - Customer registry
5. **Accounts** - Payment accounts (bank, online payment methods)
6. **Bills** - Sales invoices (immutable once created)
7. **BillItems** - Line items on bills
8. **bill_payment** - Payment transaction log
9. **BillReturns** - Product return headers
10. **BillReturnItems** - Returned items details
11. **InventoryLogs** - Stock movement audit trail
12. **CustomerLedger** - Customer accounting journal

**Design Principles:**
- ✓ All totals are **calculated** (never stored redundantly)
- ✓ Stock is **calculated** from InventoryLogs
- ✓ Bills are **immutable** once created
- ✓ Full **audit trail** for all transactions
- ✓ **Referential integrity** maintained via foreign keys

---

## 5. Database Reset Procedures

### Scenario A: Fresh Installation (Delete Everything)

**WARNING:** This removes ALL data including master data.

```bash
# 1. Locate the database
# Windows: %APPDATA%\GroceryPOS\GroceryPOS.db

# 2. Stop the application if running

# 3. Delete the database file
# The application will recreate it on next run with fresh schema

# 4. Start the application
# New database with default data will be created
```

### Scenario B: Clean Slate (Keep Master Data)

**Recommended for testing.** Clears transactions but preserves:
- Products (Items)
- Customers
- Users
- Accounts (payment methods)
- Categories

#### Method 1: Using C# Utility (Recommended)

```csharp
// Add this to your Admin panel or debug menu
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

#### Method 2: Using SQL Script (Direct Database)

```bash
# 1. Stop the application

# 2. Open the database with SQLite tool:
# - SQLite Studio
# - DB Browser for SQLite
# - Command line: sqlite3 GroceryPOS.db

# 3. Execute the reset script
.read reset_database_clean.sql

# 4. Verify the reset completed successfully
# Result: All transactional data cleared, master data intact
```

#### Method 3: Using PowerShell (Advanced)

```powershell
# From project directory
$dbPath = "$env:APPDATA\GroceryPOS\GroceryPOS.db"
$sqlScriptPath = ".\reset_database_clean.sql"

# Use sqlite3 CLI if installed
sqlite3 $dbPath < $sqlScriptPath

Write-Host "Database reset completed"
```

### What Gets Deleted

```
✗ Bills (sales invoices)
✗ BillItems (line items)
✗ bill_payment (payment transactions)
✗ BillReturns (returns)
✗ BillReturnItems (returned items)
✗ InventoryLogs (stock audit trail)
✗ CustomerLedger (accounting journal)
```

### What's Preserved

```
✓ Items (1000+ products)
✓ Categories (20+ categories)
✓ Customers (100+ registered customers)
✓ Users (admin, cashiers)
✓ Accounts (payment methods)
```

### After Reset

- Dashboard shows: 0 sales, 0 credit, 0 cash collected
- Stock quantities reset to current levels
- Customer credit balances reset to 0
- Ready for fresh testing

---

## 6. Testing Checklist

### Pre-Launch Testing

Before deploying to production, verify:

#### Database Integrity
- [ ] Database creates without errors on fresh install
- [ ] All 10 tables are created
- [ ] Foreign key constraints are enforced
- [ ] Default master data is seeded
- [ ] AUTOINCREMENT sequences start at 1

#### User Authentication
- [ ] Default admin login works (admin/admin)
- [ ] Password hashing prevents plain-text storage
- [ ] Invalid login shows error
- [ ] Logout clears session

#### Financial Calculations
- [ ] Simple cash sale calculates correctly
- [ ] Partial payment (credit) works
- [ ] Return updates totals correctly
- [ ] Dashboard metrics match database
- [ ] Cash in drawer calculation is accurate
- [ ] Online payment tracking works

#### Transactions
- [ ] Bill can be created with items
- [ ] Bill calculates GrandTotal correctly
- [ ] Stock is deducted on bill completion
- [ ] Return can be created from existing bill
- [ ] Payment received reduces outstanding balance
- [ ] Receipt prints correctly

#### Reports
- [ ] Daily sales report shows today's bills
- [ ] Date range filtering works
- [ ] Product-wise report groups by item
- [ ] Returns are counted separately
- [ ] PDF export works

### Test Data Scenarios

Run through these scenarios:

```
1. Cash Sale (Full Payment)
   - Create bill with 3 items
   - Pay full amount in cash
   - Verify: Status = Paid, RemainingAmount = 0
   - Print receipt

2. Credit Sale (Partial Payment)
   - Create bill with customer
   - Pay 50% at checkout
   - Verify: Status = PartialPaid, RemainingAmount > 0
   - Later: Receive remaining payment
   - Verify: Status = Paid

3. Return with Cash Refund
   - Create bill, create return
   - Refund in cash
   - Verify: Cash reduced, RemainingAmount updated
   - Check receipt shows return

4. Online Payment (Easypaisa)
   - Create bill with Online payment
   - Select Easypaisa method
   - Verify: Dashboard shows online total
   - Check payment breakdown

5. Multi-Day Report
   - Create sales over 3 days
   - Run daily, weekly, monthly reports
   - Verify: Correct totals for each period
```

---

## 7. Production Deployment

### Step 1: Prepare Release Build

```bash
# Create optimized release build
dotnet publish -c Release -r win-x64 --self-contained false

# Output goes to:
# bin\Release\net8.0-windows\win-x64\publish\
```

### Step 2: Create Installer Package

Option A: Manual (Folder Copy)
```bash
# 1. Copy publish folder to deployment location:
#    D:\POS\GroceryPOS\
# 2. Create shortcut to GroceryPOS.exe
# 3. Distribute folder or ZIP file
```

Option B: Windows Installer (WiX Toolset)
```bash
# For professional deployment, create MSI installer
# (Implementation outside this scope)
```

### Step 3: Pre-Deployment Verification

- [ ] Build runs without errors
- [ ] All dependencies included
- [ ] Database initializes on clean system
- [ ] Login works
- [ ] Dashboard loads
- [ ] Test transaction completes

### Step 4: Installation on Client Machine

1. User receives GroceryPOS folder or MSI
2. User runs setup / copies to Program Files
3. User creates desktop shortcut (optional)
4. First run: Database created automatically
5. Login with provided credentials

### Step 5: Data Migration (If Applicable)

If migrating from old system:

```bash
# 1. Backup existing database:
#    Copy %APPDATA%\GroceryPOS\GroceryPOS.db to safe location

# 2. Run import script (if provided)
#    Imports old data into new schema

# 3. Run verification queries:
#    SELECT COUNT(*) FROM Items;
#    SELECT COUNT(*) FROM Customers;

# 4. Test critical workflows:
#    Create bill, return, payment, report
```

---

## 8. Troubleshooting

### Application Won't Start

**Problem:** "Application startup failed"

```
Solution:
1. Check if .NET 8 is installed: dotnet --version
2. If not, install from: https://dotnet.microsoft.com/download/dotnet/8.0
3. Restart the application
```

### Database Not Found

**Problem:** "Database initialization failed"

```
Solution:
1. Verify folder exists: %APPDATA%\GroceryPOS\
2. If not, create manually
3. Ensure user has write permissions
4. Restart application (database will be created)
```

### Login Failed

**Problem:** "Invalid username or password"

```
Solution:
1. Verify correct credentials (default: admin/admin)
2. Check if database was initialized
3. For first run, create admin user in database:
   INSERT INTO Users (Username, PasswordHash, FullName, Role)
   VALUES ('admin', '<hashed_password>', 'Administrator', 'Admin');
4. Restart application
```

### Dashboard Shows Wrong Totals

**Problem:** "Sales total doesn't match bills"

```
Solution:
1. Verify database integrity:
   - Check for orphaned BillItems (bills with no header)
   - Check for cancelled bills being included
   - Verify date filtering (should be TODAY not NOW)
2. Check database script output for errors
3. Run verification queries from FINANCIAL_CALCULATIONS.md
```

### Printing Not Working

**Problem:** "Receipt doesn't print"

```
Solution:
1. Verify printer is installed and ready
2. Check Windows print spooler service: services.msc
3. Ensure thermal printer driver is installed
4. Test print to PDF first (verify layout)
5. Check application logs: %APPDATA%\GroceryPOS\logs\
```

### Database Corruption

**Problem:** "Foreign key constraint violation" or errors on startup

```
Solution:
1. Close application
2. Backup database: copy %APPDATA%\GroceryPOS\GroceryPOS.db
3. Delete corrupted database
4. Restart application (fresh database created)
5. Restore data from backup if needed
```

---

## 9. Project Structure

```
GroceryPOS-master/
│
├── App.xaml                    # Application root XAML
├── App.xaml.cs                 # Application startup & DI configuration
│
├── Converters/
│   └── Converters.cs          # Value converters for XAML binding
│
├── Data/
│   ├── DatabaseHelper.cs      # SQLite connection management
│   ├── DatabaseInitializer.cs # Schema creation & migrations
│   ├── DatabaseResetUtility.cs # Transactional data reset
│   └── Repositories/
│       ├── BillRepository.cs          # Bill data access
│       ├── BillReturnRepository.cs    # Returns data access
│       ├── CustomerRepository.cs      # Customer management
│       ├── ItemRepository.cs          # Product data access
│       ├── UserRepository.cs          # User authentication
│       ├── CreditPaymentRepository.cs # Payment transactions
│       ├── AccountRepository.cs       # Payment accounts
│       └── CustomerLedgerRepository.cs # Accounting journal
│
├── Exceptions/
│   └── BusinessException.cs   # Custom exception types
│
├── Helpers/
│   ├── AppLogger.cs           # Logging utility
│   ├── FocusHelper.cs         # UI focus management
│   └── PasswordHelper.cs      # Password hashing
│
├── Models/
│   ├── Bill.cs                # Bill entity
│   ├── BillDescription.cs     # Bill line item
│   ├── BillReturn.cs          # Return transaction
│   ├── CartItem.cs            # Shopping cart item
│   ├── CreditPayment.cs       # Payment installment
│   ├── Customer.cs            # Customer entity
│   ├── CustomerLedgerEntry.cs # Accounting entry
│   ├── Item.cs                # Product entity
│   ├── User.cs                # User entity
│   └── ...
│
├── Services/
│   ├── AuthService.cs         # Authentication & authorization
│   ├── BillService.cs         # Bill business logic
│   ├── CreditService.cs       # Credit management
│   ├── CustomerService.cs     # Customer operations
│   ├── ItemService.cs         # Product management
│   ├── PrintService.cs        # Receipt printing
│   ├── ReportService.cs       # Report generation
│   ├── ReturnService.cs       # Return handling
│   ├── StockService.cs        # Inventory management
│   ├── DataCacheService.cs    # In-memory cache
│   └── ...
│
├── ViewModels/
│   ├── BaseViewModel.cs       # Base MVVM class
│   ├── DashboardViewModel.cs  # Dashboard logic
│   ├── BillingViewModel.cs    # Billing screen logic
│   ├── ReportsViewModel.cs    # Reports screen logic
│   ├── CustomerManagementViewModel.cs
│   ├── ReturnViewModel.cs
│   └── ...
│
├── Views/
│   ├── LoginView.xaml         # Login screen
│   ├── MainWindow.xaml        # Main application window
│   ├── DashboardView.xaml     # Dashboard screen
│   ├── BillingView.xaml       # Billing screen
│   ├── ReportsView.xaml       # Reports screen
│   └── ...
│
├── Themes/
│   └── (WPF styling resources)
│
├── GroceryPOS.csproj          # Project configuration
├── GroceryPOS-master.sln      # Solution file
│
└── Documentation
    ├── README.md                        # Project overview
    ├── DATABASE_DESIGN.md              # Database schema & design
    ├── DATABASE_RESET_QUICK_START.md   # Quick reset guide
    ├── RESET_DATABASE_GUIDE.md         # Detailed reset guide
    ├── FINANCIAL_AUDIT.md              # Financial verification
    ├── FINANCIAL_CALCULATIONS.md       # Complete formulas & validation
    └── SETUP_DEPLOYMENT.md             # This file
```

---

## 10. Maintenance & Support

### Regular Maintenance Tasks

**Daily:**
- Monitor application logs for errors
- Backup critical sales data
- Verify cash drawer balance

**Weekly:**
- Run inventory verification
- Check customer outstanding balances
- Review sales trends

**Monthly:**
- Full database backup
- Clear old logs (keep 30 days)
- Review system performance
- Update product prices/items as needed

### Database Backup Procedure

```bash
# Manual backup (recommended weekly)
cd %APPDATA%\GroceryPOS
copy GroceryPOS.db GroceryPOS.db.backup.[DATE]

# Or use PowerShell
$source = "$env:APPDATA\GroceryPOS\GroceryPOS.db"
$dest = "$env:APPDATA\GroceryPOS\backups\GroceryPOS.db.$(Get-Date -f 'yyyyMMdd-HHmmss').backup"
Copy-Item $source $dest
```

### Log Files

Application logs are saved to:
```
%APPDATA%\GroceryPOS\logs\log_YYYY-MM-DD.txt
```

Logs include:
- Startup/shutdown events
- Database operations
- User actions
- Errors and exceptions

**Recommendation:** Archive logs after 30 days.

### Updating the Application

```bash
# 1. Stop the application

# 2. Backup database:
copy %APPDATA%\GroceryPOS\GroceryPOS.db backup-before-update.db

# 3. Update application files (new version)
# Copy new GroceryPOS.exe and DLLs

# 4. Restart application
# Database schema migrates automatically if needed

# 5. Verify all features work correctly
```

### Support & Contact

For issues or support:

1. Check logs: `%APPDATA%\GroceryPOS\logs\`
2. Review troubleshooting section above
3. Consult documentation files
4. Contact development team with:
   - Error message
   - Log file excerpt
   - Steps to reproduce
   - System information (Windows version, .NET version)

---

## Appendix A: Default Credentials

| User | Username | Password | Role |
|------|----------|----------|------|
| Admin | admin | admin | Admin (full access) |

**Action Required:** Change admin password on first login!

---

## Appendix B: File Locations

| Item | Location |
|------|----------|
| Database | `%APPDATA%\GroceryPOS\GroceryPOS.db` |
| Logs | `%APPDATA%\GroceryPOS\logs\` |
| Config | (embedded in application) |
| Backups | User-defined |

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 3.0 | 2026-04-22 | Production-ready, comprehensive guide |
| 2.5 | 2026-04-15 | Database optimization, AUTOINCREMENT reset |
| 2.0 | 2026-04-01 | Financial calculations verified |
| 1.0 | 2026-03-15 | Initial release |

---

**Document Status:** ✓ APPROVED FOR PRODUCTION

**Next Steps:**
1. Build Release version: `dotnet publish -c Release`
2. Run final QA testing (use Testing Checklist above)
3. Deploy to production environment
4. Monitor logs for first week
5. Gather user feedback

