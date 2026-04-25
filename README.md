# GroceryPOS

Point-of-sale (POS) desktop application for grocery stores — WPF (.NET) sample.

## Description

GroceryPOS is a Windows desktop application (WPF) that manages billing, inventory, customer credit ledgers, returns, and reports for a small retail grocery store.

## Features

- Billing and invoice printing
- Customer ledger and credit management
- Stock and inventory management
- Returns and return-audit groups
- Reports and printing

## Tech Stack

- .NET (WPF) — C#
- SQLite (embedded) via DatabaseHelper
- MVVM architecture

## Getting Started

Prerequisites:

- .NET SDK (recommended 6.0 / 7.0 or the version used in the project)
- Visual Studio 2022+ (or VS Code with C# extension)

Install & run:

```powershell
dotnet restore
dotnet build
dotnet run --project GroceryPOS.csproj
```

If using Visual Studio, open `GroceryPOS-master.sln` and run the project.

## Configuration & Secrets

- Do not commit secrets (API keys, DB credentials) to the repository. Use environment variables or a `.env` file that is ignored by git.
- Example environment usage (pseudo):

```powershell
# set an env var for local run
$env:MY_APP_CONN = "Data Source=local.db"
```

## Database

- The repo includes SQL reset scripts: `reset_database_clean.sql`, `reset_transactional_data.sql`.
- Back up your DB before running reset scripts.

## Screenshots

Placeholder for screenshots — add images to `/Docs/screenshots` and reference them here.

## License

Add your license here (e.g., MIT). Create a `LICENSE` file at repository root.

## Contributing

- Follow the code style used across the repo.
- Open issues for bugs and feature requests.

## Cleaning up tracked build files (recommended commands)

To stop tracking `bin/` and `obj/` (safe, local files remain):

```bash
git rm -r --cached bin obj Debug Release
git add .gitignore
git commit -m "chore: ignore build artifacts"
```

If you accidentally committed secrets, consider using `git filter-repo` or the `BFG Repo-Cleaner` to purge sensitive history. See the notes below.

## Notes on removing secrets from history

- BFG example (faster, simpler):

```bash
# remove a file from history
bfg --delete-files .env
# or replace a secret value
bfg --replace-text replacements.txt
git reflog expire --expire=now --all && git gc --prune=now --aggressive
```

- `git filter-repo` (recommended modern tool):

```bash
git filter-repo --invert-paths --paths .env
```

Be careful: rewriting history requires force-pushing and coordination with collaborators.
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
