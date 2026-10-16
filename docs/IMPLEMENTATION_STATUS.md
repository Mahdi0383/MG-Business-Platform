# Implementation status

This document distinguishes source-code presence from verified behavior. A feature is not production-ready merely because its code compiles.

## Implemented and covered by automated tests

- .NET 10 solution with Domain, Infrastructure, Storefront, WPF Desktop, signed-license verification, a separate Persian RTL License Manager GUI and a CLI companion.
- Currency-aware money value object, chart-of-accounts primitives, balanced double-entry journal validation, financial-period boundaries, trial-balance and P&L/balance-sheet calculations.
- Product catalog, customer/supplier records, stock movements and valuation calculations, sales invoices, partial payments and sales-to-ledger posting.
- SQLite persistence, versioned EF Core migrations, database initialization, backup/restore service, migration and recovery tests.
- Desktop workflows for product creation/editing/availability, inventory adjustments/history, customer/supplier records, single-item POS checkout, invoice payment collection, online order fulfillment/cancellation, trial balance, financial statements, store settings, backup/restore, activation diagnostics and public HTTPS publication.
- Game-cafe session domain aggregate with pause/resume/completion/cancellation transitions, recoverable paused timers, billable-minute rounding, per-player entry/time/additional charges, SQLite persistence and desktop session controls.
- Customer storefront API/UI with server-side pricing and stock validation, transactional order creation, same-key idempotent retries, rate limits, host allow-listing, request-size limits and basic security headers.
- Online customer reservations with a resource registry, capacity/time-window validation, UTC normalization, overlap checks, idempotent retries, rate-limited public endpoints, and desktop resource/reservation management. Reservations remain opt-in and require the `Reservations` entitlement plus explicit store enablement.
- PC-POS settings with reusable local profiles for manual, TCP/IP, RS-232/USB virtual COM, vendor local API, Windows service and vendor SDK categories. Profile validation, safe local persistence, backup import/export and TCP transport-only diagnostics are covered by tests; diagnostics never issue payment commands.
- Bundled Caddy reverse-proxy management, TLS certificate automation, local port checks, DNS/public-IP diagnostics, publication status checks and optional Windows firewall configuration. Settings can display the customer HTTPS URL, preview a generated QR code and save it as PNG. Actual public access still depends on DNS, router/NAT, ISP policy and inbound reachability.
- RSA-PSS signed offline license payloads, device/software serial binding, expiry and module validation, separate license generation/verification UI and CLI, independent installers/source archives, and desktop module gating based on verified entitlements. A matching RSA-3072 vendor signing key is provisioned; its private half is kept outside the repository and must remain offline under vendor control.
- Windows CI pipeline that restores, builds, runs tests, publishes self-contained Windows packages, compiles both installers, verifies package presence and uploads artifacts. Latest verified main-branch run passed 162/162 automated tests and verified the desktop, storefront, license manager, source archive and installer build steps.
- Latest completed main-branch validation for commit `a092659fcb142b655a6966dd83686c1e9bf7852d`: Release build succeeded with zero compiler errors, all 146 automated tests passed, both Windows installers compiled, and required package artifacts were verified. The run includes multi-line POS validation, hardened license key handling, and storefront QR generation/export.

## Important limitations — not a complete commercial product yet

- Accounting still lacks a full general-ledger browsing workflow, cash-flow statement, complete accounts receivable/payable aging, purchase invoices and supplier-payment workflow, tax policies, bank reconciliation, period closing/reopening and closing entries, refunds/credit notes, and comprehensive reversal/audit controls.
- Inventory valuation is not yet fully integrated with purchase cost layers/weighted-average costing and accounting COGS across all transaction types.
- POS supports a multi-line cart and transactional checkout; discounts, returns, barcode workflows, receipt-printer setup and end-of-day cash close remain.
- Cafe/restaurant operations remain incomplete: tables, open tickets, kitchen queue, recipes/ingredients, modifiers, split/merge/transfer bills, service charges, tips and local fiscal receipt rules.
- Game-cafe operations remain incomplete: device/room registry, availability scheduling, tariff management, customer history, group editing after start, extensions and fully integrated settlement/invoice/payment flow.
- Advanced reservations linked to tables/game stations, loyalty, retail variants, returns/exchanges, repair jobs, wholesale, multiple warehouses and branch synchronization remain.
- Authentication, robust roles/permissions, server-side administrative authorization, audit event persistence, import/export and retention controls need completion.
- Direct hosting cannot overcome carrier-grade NAT or blocked inbound ports. Provider-specific DNS APIs and an independent outbound relay/tunnel are not implemented; a public VPS or compatible relay may be required where direct hosting is impossible.
- No automated migration from prior MG Game Cafe databases, verified upgrade/rollback drill, signed update pipeline, comprehensive security/penetration test, performance/load test or full Windows operator acceptance test has been completed.
- Hardware integrations (receipt printers, barcode scanners, payment terminals) have not been validated against physical devices. PC-POS profiles are configurable, but vendor-specific payment execution is not implemented until the exact terminal model, acquiring bank, official protocol/SDK and certification requirements are available.

## Verification status

- Latest verified main-branch candidate before this status refresh: `65af7afe10c015a7b80f1113f3ee34178757abcf` — Release build succeeded, 162/162 automated tests passed, and required Windows package artifacts were verified. This documentation/release commit must be validated again on its own exact SHA.
- Local .NET SDK execution was not available in the authoring environment; build/test evidence comes from GitHub Actions.
- Public HTTPS must be tested from a truly external network, such as a phone on mobile data. A successful local health check alone does not prove public reachability.
- Licensing currently gates desktop tabs and local/public storefront startup. This is not a substitute for server-side authorization on every administrative API; the public storefront must remain isolated from administrative operations.
- Production readiness is **NOT VERIFIED** until all mandatory gates in [RELEASE_GATES.md](RELEASE_GATES.md) have current evidence.
