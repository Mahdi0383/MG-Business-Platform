# MG Business Platform

MG Business Platform is a Windows-first, offline-first business management platform under active development. The repository contains a WPF management client, a local SQLite database, a customer storefront API, an initial HTTPS reverse-proxy integration, finance/inventory domain models, signed-license verification and a separate license manager, and automated tests.

> **Release status: NOT PRODUCTION-READY.** The Windows CI pipeline has completed successful build/test/package runs on recent commits, but full accounting/POS/module scope, external-network publication tests, security review and operator acceptance remain incomplete. Do not use it as the sole accounting record for a live business until the release gates pass.

## Current implementation

- Windows WPF management UI with product creation/editing, inventory movement history, customer/supplier records, multi-line POS cart, invoices/payments, trial balance and financial statements, online order fulfillment/cancellation, backup/restore, activation diagnostics, game-session controls and local web-host controls.
- Local SQLite persistence with versioned EF Core migrations, an inventory movement ledger, customers/suppliers, sales invoices, journal entries and payment records.
- Consistent online SQLite backups, integrity checks and guarded restore with an automatic safety backup.
- Customer-facing Persian RTL storefront, public product catalog and ordering, server-side price calculation and stock checks, idempotent order submission, request throttling, and basic security headers.
- Online reservations for store-defined tables/game stations/rooms/service slots, capacity and time-window validation, overlap rejection, idempotent requests, and desktop approval/cancellation controls. Reservations are opt-in and remain requests until the store confirms them.
- Opt-in Caddy reverse proxy for automatic HTTPS certificate provisioning/renewal when public DNS and inbound networking are correctly configured; in-app diagnostics show DNS A/AAAA records, detected public IPv4 and local 80/443 port availability. Store settings can preview the customer HTTPS URL, generate a QR code and export it as PNG after the hostname and online ordering are configured.
- Domain models for money, chart of accounts, balanced journal entries, financial periods, sales invoices, payment allocations, business/branch identity, catalog items, and stock movements.
- Initial profit-and-loss and balance-sheet summaries, trial balance, and accounting posting for single-item POS checkout.
- Separate Persian RTL graphical License Manager and command-line tool, RSA-PSS signing/verification, device/software serial binding, expiry checks, desktop module gating and separate installer/ZIP artifacts. Server-side authorization across every protected operation still needs completion.
- Windows release pipeline that produces the main app installer, separate license-manager installer, and checksummed portable packages.
- CI workflow designed to restore, build, run tests, publish Windows binaries, bundle Caddy, create a ZIP, and create an Inno Setup installer.

## Important current limitations

- Public domain publication is not magic: DNS must point to the store's public endpoint and TCP 80/443 must reach the Windows computer. Router port forwarding is model-specific and currently requires the operator. CGNAT or blocked inbound ports requires a separate outbound tunnel/relay or public host; no universal tunnel is included.
- A green local health check does not prove public internet reachability. Test from a phone on mobile data.
- Customer club/membership authentication, verified phone OTP, loyalty points, customer-facing reservation history, automatic SMS notifications and self-service reservation cancellation are not implemented yet.
- The current POS supports a multi-line walk-in cart with stock deduction, invoice issuance, COGS/sales journal entries, and full payment in one database transaction. Advanced discounts, barcode workflows, returns/refunds, purchase invoices/payables, cash-flow statements, tax policies, receipt printing, multi-branch operation, and full cafe/game-center workflows remain unfinished.
- Latest verified main-branch Windows CI for commit a092659fcb142b655a6966dd83686c1e9bf7852d succeeded: Release build had zero compiler errors, all 146 automated tests passed, both Windows installers compiled, and required package artifacts were verified. This does not imply unfinished product scope, public internet reachability, physical hardware, or operator acceptance has been verified.

## Build and test on Windows

Install the .NET 10 SDK and PowerShell, then run from the repository root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\scripts\publish-win-x64.ps1
```

The script restores, builds, runs automated tests, publishes self-contained Windows x64 executables for the desktop app, storefront and separate license manager, downloads the official Caddy binary, creates portable packages and separate main/license-manager source ZIPs with SHA-256 checksums, and creates two installers when Inno Setup 6 is installed.

The expected Windows artifacts are:
- `artifacts/MG-Business-Platform-win-x64.zip`
- `artifacts/MG-Business-Platform-win-x64.zip.sha256`
- `artifacts/installer/MG-Business-Platform-Setup-win-x64.exe`
- `artifacts/MG-License-Manager-win-x64.zip` and `artifacts/MG-License-Manager-win-x64.zip.sha256`
- `artifacts/installer/MG-License-Manager-Setup-win-x64.exe`
- `artifacts/MG-Business-Platform-SOURCE.zip` and its `.sha256` checksum
- `artifacts/MG-License-Manager-SOURCE.zip` and its `.sha256` checksum

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
- [License Manager usage and key handling](docs/LICENSE_MANAGER.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)
- [PC-POS device integration architecture](docs/PC_POS_INTEGRATION.md)
- [Contribution and development workflow](CONTRIBUTING.md)

## Product principles

- Auditable double-entry accounting and exact decimal money calculations.
- Modular monolith before premature microservices.
- Persian/Farsi and right-to-left UI as first-class requirements.
- Least privilege, signed license entitlements, secure backups, and no direct public exposure of database files.
- Small increments with tests and reviewable Git history.

This repository is private. Keep all signing secrets and production credentials out of source control.
