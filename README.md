# MG Business Platform

MG Business Platform is a Windows-first, offline-first business management platform under active development. The repository contains a WPF management client, a local SQLite database, a customer storefront API, an initial HTTPS reverse-proxy integration, finance/inventory domain models, and automated tests.

> **Release status: NOT PRODUCTION-READY.** The project has not yet completed a verified Windows build, end-to-end release test, or the full accounting/POS/module scope. Do not use it as the sole accounting record for a live business until the release gates pass.

## Current implementation

- Windows WPF management UI with product creation, storefront settings, online order list, order cancellation with stock restoration, and local web-host controls.
- Local SQLite persistence with versioned EF Core migrations, an inventory movement ledger, customers/suppliers, sales invoices, journal entries and payment records.
- Consistent online SQLite backups, integrity checks and guarded restore with an automatic safety backup.
- Customer-facing Persian RTL storefront, public product catalog, server-side price calculation, stock checks, order idempotency, request throttling, and basic security headers.
- Opt-in Caddy reverse proxy for automatic HTTPS certificate provisioning/renewal when public DNS and inbound networking are correctly configured.
- Domain models for money, chart of accounts, balanced journal entries, financial periods, sales invoices, payment allocations, business/branch identity, catalog items, and stock movements.
- CI workflow designed to restore, build, run tests, publish Windows binaries, bundle Caddy, create a ZIP, and create an Inno Setup installer.

## Important current limitations

- Public domain publication is not magic: DNS must point to the store's public endpoint and TCP 80/443 must reach the Windows computer. Router port forwarding is model-specific and currently requires the operator. CGNAT or blocked inbound ports requires a separate outbound tunnel/relay or public host; no universal tunnel is included.
- A green local health check does not prove public internet reachability. Test from a phone on mobile data.
- The current POS supports single-item walk-in checkout with stock deduction, invoice issuance, COGS/sales journal entries, and full payment in one database transaction. Advanced carts/discounts, barcode workflows, purchase invoices/payables, refunds, full balance sheet/P&L/cash-flow statements, tax policies, receipt printing, multi-branch operation, full cafe/game-center workflows, and the separate license manager remain unfinished.
- Schema migration and order flow tests are committed, but their success must be confirmed from the actual Windows CI run. No build or test is claimed as passed until verified.

## Build and test on Windows

Install the .NET 10 SDK and PowerShell, then run from the repository root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\scripts\publish-win-x64.ps1
```

The script restores, builds, runs automated tests, publishes self-contained Windows x64 executables, downloads the official Caddy binary, creates a portable ZIP and SHA-256 file, and creates an installer when Inno Setup 6 is installed.

The expected Windows artifacts are:
- `artifacts/MG-Business-Platform-win-x64.zip`
- `artifacts/MG-Business-Platform-win-x64.zip.sha256`
- `artifacts/installer/MG-Business-Platform-Setup-win-x64.exe`

## Documentation

- [Product vision](docs/PRODUCT_VISION.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Roadmap and acceptance criteria](docs/ROADMAP.md)
- [Implementation status](docs/IMPLEMENTATION_STATUS.md)
- [Self-hosted domain publishing](docs/SELF_HOSTED_PUBLISHING.md)
- [Domain-hosting architecture](docs/DOMAIN_HOSTING.md)
- [Release gates](docs/RELEASE_GATES.md)
- [Architecture decisions](docs/DECISIONS.md)
- [Module boundaries](docs/MODULES.md)
- [Security and licensing](docs/SECURITY.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)
- [Contribution and development workflow](CONTRIBUTING.md)

## Product principles

- Auditable double-entry accounting and exact decimal money calculations.
- Modular monolith before premature microservices.
- Persian/Farsi and right-to-left UI as first-class requirements.
- Least privilege, signed license entitlements, secure backups, and no direct public exposure of database files.
- Small increments with tests and reviewable Git history.

This repository is private. Keep all signing secrets and production credentials out of source control.
