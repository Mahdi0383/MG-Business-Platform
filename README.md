# MG Business Platform

**MG Business Platform** is a modular, offline-first business management platform designed to grow from a single Windows workstation to LAN and multi-branch deployments.

> **Current status:** requirements and architecture foundation. Features described here are planned unless a later implementation and test explicitly proves otherwise.

## Principles

- Offline-first core workflows.
- Auditable, balanced double-entry accounting.
- Modular monolith with explicit module ownership and contracts.
- Least privilege, signed licenses, secure backups, and no direct public exposure of database files.
- Persian/Farsi and right-to-left UI as first-class requirements.
- Small, testable increments with reviewable Git history.

## Planned platform capabilities

- Shared core: businesses/branches, users/roles/permissions, customers/suppliers, products/services, pricing, sales, invoices, payments, cash/bank, settings, audit, import/export, backup/restore.
- Accounting: chart of accounts, journals, general ledger, receivables/payables, trial balance, financial statements, inventory valuation/COGS, fixed assets, financial periods and closing.
- Inventory and purchasing: warehouses, stock movements, purchasing, returns, stock counts, waste and expiry where relevant.
- Business modules: cafe/restaurant POS, game cafe/gaming center, retail, supermarket, clothing, repair/service business and wholesale.
- Optional capabilities: loyalty, QR ordering, reservations, notifications, advanced reports, payroll and multi-branch operations.
- Separate **MG License Manager** for signed module entitlements; private signing keys must never ship inside the main application.

These are target capabilities, not claims that they are already implemented.

## Documentation

- [Product vision](docs/PRODUCT_VISION.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Roadmap and acceptance criteria](docs/ROADMAP.md)
- [Architecture decisions](docs/DECISIONS.md)
- [Module boundaries](docs/MODULES.md)
- [Security and licensing](docs/SECURITY.md)
- [Contribution and development workflow](CONTRIBUTING.md)

## Initial technology direction

The leading option is C#/.NET for shared business logic and a Windows desktop client, SQLite for single-computer mode, and PostgreSQL for central multi-client deployments. UI framework, target .NET version, persistence boundaries and packaging must be validated by a small compatibility prototype before final commitment.

## Release gate

Do not treat this as production accounting software until financial invariants, permissions, data migrations, crash recovery, backup/restore, security controls and supported deployment modes pass documented automated and manual tests.

## License

Distribution and source licensing terms will be decided before any public release. This repository is private.
