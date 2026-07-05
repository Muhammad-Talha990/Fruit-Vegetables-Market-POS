# GroceryPOS — Enterprise Grade Retail Management System

![Build Status](https://img.shields.io/badge/build-passing-brightgreen)
![Platform](https://img.shields.io/badge/platform-Windows-blue)
![Framework](https://img.shields.io/badge/framework-.NET%208%20WPF-orange)
![License](https://img.shields.io/badge/license-MIT-green)

A professional, commercial-ready Point of Sale (POS) and Inventory Management System designed for grocery stores and retail outlets. Built with a focus on data integrity (3NF SQL Design), performance, and a premium user experience.

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

### 📦 Inventory & Stock Control
- **Audit Trails**: Every stock movement is logged with a detailed reference (Sale, Return, Purchase).
- **Low Stock Alerts**: Visual indicators and reports for items reaching critical thresholds.
- **Stock Purchases**: Manage supplier invoices and deduct purchase amounts from the cash drawer automatically.

### 📊 Professional Reporting & Analytics (v1.1.0)
- **Interactive Analytics Dashboard**: Beautiful pure-WPF bar charts displaying daily sales trends and top-performing products.
- **KPI Summary Cards**: Real-time metrics for Total Revenue, Net Sales, Returns, Average Order Value, and Outstanding Credit.
- **Advanced Audit Trail**: Comprehensive transaction grid with search, advanced filtering, and color-coded status badges for Sales and Returns.
- **Financial Audits**: Daily and date-ranged sales, returns, and credit recovery reports.
- **Thermal Printing**: Industry-standard 80mm thermal receipt printing with professional branding.

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
GroceryPOS/
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
   git clone https://github.com/Muhammad-Talha990/GroceryPOS.git
   cd GroceryPOS
   ```

2. **Restore & Build**:
   ```bash
   dotnet restore
   dotnet build
   ```

3. **Launch**:
   ```bash
   dotnet run --project GroceryPOS.csproj
   ```

*Note: The database is initialized automatically on the first launch.*

---

## 🛡 Security & Configuration

This project follows professional security standards:
- **No Hardcoded Secrets**: Credentials are moved to external configurations.
- **BCrypt Hashing**: All user passwords are encrypted before storage.
- **Portable DB**: Connection strings are resolved dynamically at runtime.

---

## 📄 License

This project is licensed under the **MIT License**. See the [LICENSE](LICENSE) file for details.

---

## 👨‍💻 Author

**Muhammad Talha**  
*Senior Software Engineer & POS Specialist*

---

