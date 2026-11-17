# Implementation status

This document distinguishes source-code presence from verified behavior. A feature is not production-ready merely because its code compiles.

## Implemented and covered by automated tests

- .NET 10 solution with Domain, Infrastructure, Storefront, WPF Desktop, signed-license verification, a separate Persian RTL License Manager GUI and a CLI companion.
- Currency-aware money primitives, chart-of-accounts entities, balanced double-entry journals, financial periods, trial balance, general-ledger running balances, P&L/balance-sheet calculations, cash/bank movement summaries, and receivable/payable aging by recorded due date, with an explicit issue-date fallback for legacy invoices without a due date.
- Product catalog with edit/availability controls, validated CSV import/export, case-insensitive duplicate-SKU skipping, opening-stock ledger entries, customers and suppliers, stock movements/history/valuation, multi-line POS checkout, sales invoices, partial payments and sales-to-ledger posting.
- Supplier purchase invoices, weighted-average unit-cost updates, stock receipts, supplier payments and corresponding accounting journal entries.
- SQLite persistence, versioned EF Core migrations, initialization, transactional backup/restore, integrity checks and business-schema identity checks before restore. Persistent audit events are automatically recorded for tracked entity creates/updates/deletes, with actor/machine, timestamp, entity key and changed field names (not before/after values), plus a date-filtered read-only desktop history view and UTF-8 CSV export with spreadsheet formula-prefix neutralization.
- Desktop workflows for inventory adjustments, customer/supplier management, POS, invoice payments, purchase invoices/payments, online order fulfillment/cancellation, trial balance, general ledger, financial statements, store settings, database backup/restore and license diagnostics.
- Game-cafe session domain aggregate with pause/resume/completion/cancellation transitions, recoverable paused timers, billable-minute rounding, per-player entry/time/additional charges in the configured store currency, SQLite persistence, prevention of overlapping active/paused sessions on one device, and invoice/payment integration.
- Customer storefront API/UI with server-side price and stock validation, transactional order creation, same-key idempotent retries that reject mismatched payloads, rate limits, host allow-listing, request-size limits and basic security headers.
- Online reservation resources and booking requests with validation, idempotency, overlap checks and desktop confirmation/cancellation controls.
- PC-POS profile settings for manual, TCP/IP, serial/USB virtual COM, vendor local API, Windows service and vendor SDK connection types. TCP diagnostics verify transport only; they do not claim payment approval or replace model-specific vendor protocol adapters.
- Bundled Caddy reverse-proxy management, TLS certificate automation, local port checks, DNS/public-IP diagnostics, HTTPS status checks, generated customer QR links and optional Windows firewall configuration. The storefront Kestrel listener is explicitly bound to localhost:8080 so direct launch or inherited ASPNETCORE_URLS settings cannot expose the SQLite-backed service on LAN/public interfaces. Compatible UPnP IGD routers can optionally map TCP 80/443 after explicit consent; the app records ownership and removes only mappings it can verify it created. ParsPack CDN DNS A-record synchronization is available from Store Settings; the API token is stored in Windows Credential Manager and existing records are replaced only after confirmation. Other DNS providers remain manual.
- RSA-PSS signed offline license payloads, software/hardware serial binding, expiry and module validation, separate license-generation/verification UI and CLI, desktop module gating, and independent Windows packaging. The private signing key must remain outside the application and source control.
- Windows CI/release workflows that restore, build, run tests, publish self-contained Windows packages, compile both installers, validate source archives and produce SHA-256 checksums.

## Important limitations — not a complete commercial product yet

- Accounting still needs a formally classified cash-flow statement (the current report summarizes selected cash/bank ledger movements), migration of legacy invoices to verified contractual due dates (legacy records without due dates fall back to issue-date aging), tax policies, bank reconciliation, period close/reopen and closing entries, refunds/credit notes, robust reversal workflows, audit-event retention policy and comprehensive accounting reconciliation.
- Inventory valuation is not yet fully integrated with purchase cost layers and COGS across every transaction type; tax, discounts and rounding policy need explicit end-to-end coverage.
- POS now has basic selected-invoice receipt printing through the Windows print driver with a narrow layout suitable for common 58 mm thermal printers. Barcode workflows, configurable receipt templates, returns/exchanges, discounts, shift/end-of-day cash closing and physical printer/payment-terminal validation remain.
- Cafe/restaurant operations remain incomplete: tables, open tickets, kitchen queue, recipes/ingredients, modifiers, split/merge/transfer bills, service charges, tips and local fiscal receipt rules.
- Game-cafe operations still need a managed device/room registry, availability scheduling, tariff management, customer history, editing groups after start, and broader automated acceptance tests for timer recovery and final settlement.
- Branch synchronization, multi-warehouse operations, loyalty, retail variants, repair jobs, wholesale workflows and a complete permissions/role administration interface remain.
- Multi-PC operation is not yet a complete centrally authenticated deployment. Do not share the SQLite database file over a network share; a supported central service/database and desktop-client API path must be implemented and load-tested.
- Direct self-hosting cannot overcome carrier-grade NAT or blocked inbound ports. DNS automation currently supports ParsPack CDN zones only; other providers require manual records. Optional UPnP forwarding is supported on compatible routers with explicit consent, with manual configuration as fallback. An independent outbound relay/tunnel is not implemented, so a public VPS or compatible relay may be required where direct hosting is impossible.
- Audit events currently identify the Windows account and machine, but do not yet record application-level user identities/roles, use a tamper-evident hash chain, or provide a retention policy. Audit history is an operational trace, not a tamper-proof forensic log.
- No automated migration from prior MG Game Cafe databases, verified upgrade/rollback drill, signed update pipeline, comprehensive security/penetration test, performance/load test or full Windows operator acceptance test has been completed.
- Physical receipt printers, barcode scanners, payment terminals and router/DNS configurations have not been validated against real customer hardware and networks.

## Verification status

Evidence is scoped to the exact commit and workflow run. A successful pull-request run does not place unmerged code on `main`.

### Default branch — verified integrated snapshot
- Main commit: `b829a7d7442e0d2df07bf14d2bcf836c856dfcad` (2026-11-17).
- Windows CI [#44119923455](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44119923455) passed on this exact main commit: Release build succeeded with **0 warnings / 0 errors**, **242 automated tests passed**, both installers compiled, and package/checksum verification plus QA artifact upload succeeded. QA artifact ID: `14345505723` (14-day retention; not a production release).
- NuGet audit [#44119923573](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44119923573) passed; no direct or transitive NuGet vulnerabilities were reported.
- CodeQL [#44119923327](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44119923327) completed successfully. Review the repository's Code scanning alerts; successful SARIF upload is not proof of zero findings.
- Changed-C# formatting [#44119923285](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44119923285) passed on this main commit. Note: this push changed documentation only, so the incremental formatter did not re-check previously merged C# files; PR #114 addresses the one earlier-discovered line-group formatting defect in `DatabaseMaintenanceServiceTests.cs`.
- Verified Windows Release workflow [#44119923293](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44119923293) was skipped by its release gate. No production release was published from this commit.

### Merged pull-request evidence
The following changes are integrated in main. Each run verifies the exact PR head before merge; the combined main snapshot is separately verified by run #44119923455 above.
- PR #96: SQLite initialization/maintenance locking, verified pre-migration backups, safe restore and migration rollback regression — Windows CI [#43688475574](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/43688475574), **232 tests passed**.
- PR #97: backup/restore destination preservation and active-journal regressions — Windows CI [#41597777507](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/41597777507), **226 tests passed**.
- PR #98: verified Windows package/checksum QA artifacts — Windows CI [#42351864628](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/42351864628).
- PR #100: Inno Setup architecture identifiers changed to `x64compatible` — Windows CI [#41795498279](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/41795498279).
- PR #101: NuGet vulnerability auditing, Dependabot and Node 24-compatible pinned Actions — Windows CI [#42461210182](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/42461210182); audit [#42461210169](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/42461210169), no vulnerabilities reported on that head.
- PR #102: CodeQL workflow — Windows CI [#42776197435](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/42776197435); CodeQL [#42776198096](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/42776198096).
- PR #103: incremental changed-C# formatting check — Windows CI [#43388897734](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/43388897734); formatting [#43388897674](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/43388897674).
- PR #106: documented production backend contract and release gates (documentation-only).
- PR #107: per-response storefront CSP nonces — Windows CI [#44113621114](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44113621114), **224 tests passed**.
- PR #108: browser-storage schema validation and POS demo inventory checks — web CI [#44115786919](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44115786919), **17 frontend tests passed**, npm audit 0 vulnerabilities.
- PR #109: database-level one-open-Game-Net-session-per-device constraint — Windows CI [#44115402384](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44115402384), **224 tests passed**.
- PR #110: POS idempotency, including changed-sale-date rejection — Windows CI [#44117747162](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44117747162), **227 tests passed**.
- PR #111: SQLite triggers preventing overlapping active reservations — Windows CI [#44117122520](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44117122520), **224 tests passed**.

### Open pull requests
- PR #105 remains **Draft**. Head `ff963dd0abf5b88ed3f1cc8d9194fc195cd9761a`: web CI [#44119616370](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44119616370) passed **17 frontend tests**, npm audit 0 vulnerabilities, Vite build and artifact upload; Windows CI [#44119616352](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44119616352) passed **242 tests**, 0 warnings/errors, NuGet audit and installer/package verification. It remains a static demo: localStorage is synthetic, production API/authentication/tenant isolation/server-side financial transactions are absent, Pages deploy was skipped for the PR, and the generated `package-lock.json` is in the CI artifact but not yet committed; `npm ci` is still outstanding.
- PR #114 addresses the incremental formatter's earlier finding on `DatabaseMaintenanceServiceTests.cs`. Its latest head and exact-head formatting/Windows CI must pass before merge.

### Workflow and security configuration
- NuGet audit, CodeQL and incremental changed-file C# formatting are integrated and have passed on the exact main snapshot above.
- Inno Setup scripts use `x64compatible`; Windows workflows use pinned Node 24-compatible Actions.
- GitHub metadata reports repository visibility as **public**. Confirm this is intentional; if source confidentiality is required, change visibility in Settings → General.
- Dependency Review previously failed because GitHub Dependency Graph was disabled. NuGet auditing is active; enabling Dependency Graph and validating a separate Dependency Review workflow remain follow-ups.
- PR-level and historical CI evidence is commit-scoped; always verify the exact main commit before releases.

### Remaining release gates
- No legacy MG Game Cafe importer is present on `main`. An unmerged branch `feature/legacy-sqlite-product-import` contains a generic product-only SQLite mapper and tests, but it is not an open PR, is diverged from `main` (3 commits ahead / 2 behind), and its XAML button references `ImportLegacyDatabase_Click`, which is absent from that branch's `MainWindow.xaml.cs` (no other desktop partial file exists). Static review also found heuristic table selection and a hard-coded `IRR` fallback; the branch does not import historical invoices, payments, customers, suppliers or stock movements. Do not treat it as a release-ready legacy migration. The supported automatic upgrade boundary remains the MG Business Platform initial EF migration history; unknown/legacy histories fail closed. The actual old schema/source and an explicit currency/mapping confirmation are still required.
- Backup retention and a production-like restore drill, migration/upgrade/rollback validation on supported versions, and real install/uninstall validation remain incomplete.
- Full accounting acceptance (period closing, adjustments/reversals, tax/discount/refund policies, reports), full café/game-net acceptance, and licensing entitlement/security acceptance remain incomplete.
- Printer, barcode, payment-terminal, DNS, HTTPS, CGNAT, WAN-IP-change and certificate-renewal behavior require physical hardware or external-network validation.
- Local .NET execution was not available in the authoring environment; evidence above is from GitHub Actions.
- Production readiness remains **NOT VERIFIED** until every applicable gate in [RELEASE_GATES.md](RELEASE_GATES.md) has current evidence.
