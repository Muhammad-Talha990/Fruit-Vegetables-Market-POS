# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0] - 2026-07-05
### Added
- **Interactive Analytics Dashboard**: Beautiful pure-WPF bar charts displaying daily sales trends and top-performing products.
- **KPI Summary Cards**: Real-time metrics for Total Revenue, Net Sales, Returns, Average Order Value, and Outstanding Credit.
- **Advanced Audit Trail**: Comprehensive transaction grid with search, advanced filtering, and color-coded status badges for Sales and Returns.
- Added GitHub Actions CI Pipeline for automated builds.
- Added GitHub Community Standards (Issue Templates, PR Template, Contributing Guide).

### Changed
- Refactored Return logic to calculate `PaidAmount` accurately after partial and full returns.
- Standardized documentation structure in `/Docs/Manuals/`.
- Updated `.gitignore` to keep the repository strictly free of build outputs and secrets.

## [1.0.0] - Initial Release
### Added
- Core POS billing functionality.
- Customer Ledger and Credit Management.
- Inventory control and purchase tracking.
- Database reset utilities for development and staging.
