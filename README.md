# MG Business Platform

MG Business Platform is a Windows-first, offline-first business management platform under active development. The repository contains a WPF management client, a local SQLite database, a customer storefront API, an initial HTTPS reverse-proxy integration, finance/inventory domain models, signed-license verification and a separate license manager, and automated tests.

> **Release status: NOT PRODUCTION-READY.** The Windows CI pipeline has completed successful build/test/package runs on recent commits, but full accounting/POS/module scope, external-network publication tests, security review and operator acceptance remain incomplete. Do not use it as the sole accounting record for a live business until the release gates pass.

## Current implementation

- Windows WPF management UI with product creation/editing and CSV import/export, inventory movement history, customer/supplier records, multi-line POS cart, invoices/payments, trial balance and financial statements, online order fulfillment/cancellation, backup/restore, activation diagnostics, game-session controls and local web-host controls.
- Product CSV import validates the complete file before writing, skips existing SKUs without overwriting them, and records imported opening stock in the inventory ledger. See [product CSV format and safety rules](docs/PRODUCT_CSV_IMPORT.md). Audit history can also be exported to date-filtered CSV.
- Local SQLite persistence with versioned EF Core migrations, an inventory movement ledger, customers/suppliers, sales invoices, journal entries and payment records.
- Consistent online SQLite backups, integrity checks and guarded restore with an automatic safety backup.
- Customer-facing Persian RTL storefront, public product catalog and ordering, server-side price calculation and stock checks, idempotent order submission, request throttling, and basic security headers.
- Online reservations for store-defined tables/game stations/rooms/service slots, capacity and time-window validation, overlap rejection, idempotent requests, and desktop approval/cancellation controls. Reservations are opt-in and remain requests until the store confirms them.
- Opt-in Caddy reverse proxy for automatic HTTPS certificate provisioning/renewal when public DNS and inbound networking are correctly configured; in-app diagnostics show DNS A/AAAA records, detected public IPv4 and local 80/443 port availability. Compatible UPnP IGD routers can optionally forward TCP 80/443 after explicit confirmation, with manual configuration as fallback. Store settings can synchronize a confirmed ParsPack DNS A record, preview the customer HTTPS URL, generate a QR code and export it as PNG.
- Domain models for money, chart of accounts, balanced journal entries, financial periods, sales invoices, payment allocations, business/branch identity, catalog items, and stock movements.
- Initial profit-and-loss and balance-sheet summaries, trial balance, and transactional accounting posting for multi-line POS checkout.
- Separate Persian RTL graphical License Manager and command-line tool, RSA-PSS signing/verification, device/software serial binding, expiry checks, desktop module gating and separate installer/ZIP artifacts. Server-side authorization across every protected operation still needs completion.
- Windows release pipeline that produces the main app installer, separate license-manager installer, and checksummed portable/source packages.
- CI workflow is configured to restore, build, run automated tests, publish Windows binaries, bundle Caddy, validate archives/checksums and compile both Inno Setup installers.
- Versioned Windows prereleases are published by `release-windows.yml` on explicitly marked `[release]` commits or a manual workflow dispatch. Download installers, portable ZIPs, source archives and SHA-256 checksums from the latest verified [GitHub Release](https://github.com/Mahdi0383/MG-Business-Platform/releases).

**Latest verification note (2026-10-27):** the latest main commit has not yet been build/test verified. GitHub Actions jobs are failing before a runner is assigned (`runner_id: 0`, no steps/logs); the last successful Windows CI evidence applies to an older commit. Do not treat current changes as a verified release until the runner issue is resolved and the full pipeline passes.

## Important current limitations

- Public domain publication is not magic: DNS must point to the store's public endpoint and TCP 80/443 must reach the Windows computer. Compatible routers may be configured through optional UPnP after explicit consent; manual forwarding remains the fallback. CGNAT or blocked inbound ports still requires a public IP or a compatible outbound tunnel/relay, which is not yet included.
- A green local health check does not prove public internet reachability. Test from a phone on mobile data.
- Customer club/membership authentication, verified phone OTP, loyalty points, customer-facing reservation history, automatic SMS notifications and self-service reservation cancellation are not implemented yet.
- The current POS supports a multi-line walk-in cart with stock deduction, invoice issuance, COGS/sales journal entries, and full payment in one database transaction. Basic selected-invoice receipt printing is implemented for narrow thermal-printer content through the Windows print driver; the 58 mm layout and real printer hardware still need operator validation. Advanced discounts, barcode workflows, returns/refunds, cash-flow statements, tax policies, shift closing, multi-branch operation, and full cafe/game-center workflows remain unfinished.
- Latest completed Windows CI verification before the current release-workflow correction passed with zero compiler errors, 186 automated tests, both Windows installers compiled, and package/source archive checks verified. The latest published GitHub Release must be checked for its source commit before treating its binaries as current. Successful CI does not prove public internet reachability, physical hardware compatibility, or operator acceptance.

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
