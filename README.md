# Fruit & Vegetable Market POS (سبزی منڈی)

[![.NET Build](https://github.com/Muhammad-Talha990/Fruit-Vegetables-Market-POS/actions/workflows/dotnet-build.yml/badge.svg)](https://github.com/Muhammad-Talha990/Fruit-Vegetables-Market-POS/actions/workflows/dotnet-build.yml)
![Platform](https://img.shields.io/badge/platform-Windows-blue)
![Framework](https://img.shields.io/badge/framework-.NET%208%20WPF-orange)
![License](https://img.shields.io/badge/license-MIT-green)

A professional Point of Sale (POS) for fruit & vegetable markets (سبزی منڈی) — branded as **PMC (Pak Madinah Commission Agents)**. Built with .NET 8 WPF, SQLite, and a clear daily-selling workflow.

---

## 🚀 Key Features

### 🛒 Billing & Invoicing
- **High-Speed Checkout**: Optimized for barcode scanners and keyboard-only operation.
- **Multi-Tab Interface**: Handle multiple customers simultaneously with an intuitive tab system.
- **Dynamic Pricing**: Automatic calculation of subtotals, taxes, and discounts.
- **Payment Flexibility**: Support for Cash, Bank Transfer, Easypaisa, and JazzCash.

### 👥 Customer & Credit Management
- **Smart Ledgers**: 100% accurate, chronological transaction history for every customer.
- **Credit Tracking**: Manage 'Udhar' (Store Credit) with automated balance reconciliation.
- **Return Processing**: Integrated return module that updates stock and credit ledgers in real-time.

### 📦 Daily Menu & Catalog
- **Today's Menu**: Select which items sell today with type-based unit prices.
- **Product Photos**: Menu cards load images from `Assets/Products`.
- **Catalog Management**: Add fruit/vegetable items with English + Urdu names.

### 📊 Professional Reporting & Analytics
- **Interactive Analytics Dashboard**: WPF bar charts for sales trends and top products.
- **KPI Summary Cards**: Revenue, returns, credit due, recovered credit, cash drawer, online.
- **Thermal Printing**: 80mm ESC/POS receipts, gate pass, and payment slips.

---

## 🛠 Tech Stack

- **Core**: .NET 8 (Windows) with WPF (XAML)
- **Architecture**: MVVM (Model-View-ViewModel) for clean separation of concerns.
- **Database**: High-performance SQLite engine with 3NF normalized schema.
- **Security**: BCrypt hashing for user credentials.
- **Reliability**: Transactional integrity for all financial operations.

---

## 📁 Repository Structure

```text
FruitVegetableMarketPOS/
├── Assets/          # Icons, Branding, and Media assets
├── Data/            # Repository pattern implementation and SQLite Logic
├── Docs/            # Detailed documentation (Schema, Audits, Financials)
├── Helpers/         # Utility classes and shared logic
├── Models/          # Core business entities
├── Services/        # Business logic and domain services
├── ViewModels/      # Application state and UI logic
├── Views/           # WPF Windows, UserControls, and Themes
└── Scripts/         # Utility scripts for maintenance and publishing
```

---

## 🚦 Getting Started

### Prerequisites
- **Operating System**: Windows 10/11
- **Developer Tools**: .NET 8 SDK or Visual Studio 2022

### Build & Run

1. **Clone the repository**:
   ```bash
   git clone https://github.com/Muhammad-Talha990/Fruit-Vegetables-Market-POS.git
   cd Fruit-Vegetables-Market-POS
   ```

2. **Restore & Build**:
   ```bash
   dotnet restore FruitVegetableMarketPOS.sln
   dotnet build FruitVegetableMarketPOS.sln
   ```

3. **Launch**:
   ```bash
   dotnet run --project FruitVegetableMarketPOS.csproj
   ```
   Or from PowerShell in this folder:
   ```powershell
   .\run.ps1
   ```

*Note: The SQLite database is created automatically under `%LOCALAPPDATA%\FruitVegetableMarketPOS\` on first launch.*

---

## 🛡 Security & Configuration

- **Local DB only**: No cloud connection strings are committed.
- **BCrypt**: User passwords are hashed before storage.
- **Runtime config**: Printer settings stay on the local machine (`printer_config.txt` is gitignored).
- **Default login** (change after first install): see the user manual — do not commit production credentials.

---

## 📄 License

This project is licensed under the **MIT License**. See the [LICENSE](LICENSE) file for details.

---

## 🤝 Contributing

Contributions are always welcome! Please see our [Contributing Guidelines](CONTRIBUTING.md) for details on how to propose bug fixes and improvements.

---

## 📜 Changelog

See the [CHANGELOG.md](CHANGELOG.md) for a history of updates and new features.

---

## 👨‍💻 Author

**Muhammad Talha**  
*Senior Software Engineer & POS Specialist*

---

