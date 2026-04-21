# GroceryPOS

A desktop Point of Sale (POS) application for grocery stores, built with C# WPF (.NET 8) and SQLite.

## Project Description

GroceryPOS is a Windows desktop billing and store-management system designed for daily retail workflows.  
It supports billing, stock handling, customer credit, returns, reporting, and receipt printing in a single application.

## Features

- Billing and invoicing with barcode support and multiple payment types
- Customer management with credit tracking and ledger history
- Product and inventory management with low-stock monitoring
- Supplier bill entry with attachment support
- Returns and refund handling (cash or credit adjustment)
- Sales and operational reports with date-based filtering
- Thermal receipt printing (80mm) and PDF export
- Dashboard metrics for sales, returns, credit, and payment split

## Tech Stack

- UI: WPF (XAML)
- Runtime: .NET 8 (Windows)
- Architecture: MVVM
- Database: SQLite (`Microsoft.Data.Sqlite`)
- DI: `Microsoft.Extensions.DependencyInjection`
- Password Hashing: `BCrypt.Net-Next`
- Printing: `System.Drawing`

## Repository Structure

```text
GroceryPOS-master/
├── Converters/
├── Data/
│   ├── Repositories/
│   ├── DatabaseHelper.cs
│   └── DatabaseInitializer.cs
├── Docs/
├── Exceptions/
├── Helpers/
├── Models/
├── Services/
├── Themes/
├── ViewModels/
├── Views/
│   └── Controls/
├── App.xaml
├── App.xaml.cs
├── GroceryPOS.csproj
└── GroceryPOS-master.sln
```

## Setup Instructions

### Prerequisites

- Windows OS (WPF is Windows-only)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Run Locally

```bash
dotnet restore
dotnet build GroceryPOS-master.sln
dotnet run --project GroceryPOS.csproj
```

The SQLite database file is generated automatically on first run.

## Database Notes

- SQLite schema is initialized and migrated in `DatabaseInitializer`
- Database path is handled by `DatabaseHelper`
- Full schema and accounting design notes: [DATABASE_DESIGN.md](DATABASE_DESIGN.md)

## Delivery / Production Notes

- Keep generated logs and local databases out of version control
- Do not commit local environment secrets (`.env`, local appsettings overrides)
- Build in Release mode for client delivery:

```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

## License

This project is for educational and personal use unless otherwise agreed with the client.
