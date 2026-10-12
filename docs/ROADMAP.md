# Roadmap and Acceptance Criteria

Work is delivered in small milestones. A phase is not complete because code exists; its acceptance criteria and tests must pass.

## Phase 0 — Requirements and technical proof

- Confirm Windows baseline, deployment modes, first business type and initial language/currency requirements.
- Compare WPF/Avalonia and validate printing, barcode input, RTL, packaging and test automation.
- Write module contracts, domain glossary, accounting rules and threat model.
- Define supported database modes and backup/restore objectives.

**Exit gate:** documented decisions, working minimal prototype, risks and explicit out-of-scope list.

## Phase 1 — Repository and platform foundation

- Establish solution layout, SDK pinning after compatibility validation, formatting/analyzers and CI.
- Add configuration, structured logging, error handling, database migrations, audit infrastructure and backup/restore skeleton.
- Add dependency/architecture tests and a reproducible local build.

**Exit gate:** clean build and tests on the supported Windows environment; migrations and restore smoke test pass.

## Phase 2 — Core operations vertical slice

- Business setup, administrator, users/roles/permissions.
- Customers/suppliers, products/services, units, prices and taxes/discount rules.
- Draft/posted sales, invoices, payments, returns, receipt abstraction and audit history.

**Exit gate:** end-to-end sales flow works offline; permission and idempotency tests pass; totals reconcile.

## Phase 3 — Accounting and inventory

- Chart of accounts, journal posting, general ledger, AR/AP, cash/bank, trial balance and financial statements.
- Purchasing, inventory movements, valuation, COGS, stock count and returns.
- Financial period locking, correction/reversal workflows and audit reporting.

**Exit gate:** seeded scenarios reconcile across invoice, payment, stock and ledger; no unbalanced posted journal can be committed.

## Phase 4 — Industry modules

- Cafe/restaurant: tables, open orders, takeaway, split/merge/transfer bill, kitchen queue, recipes, ingredient costing and waste.
- Game cafe: devices, tariffs, timer/session recovery, pause/resume/extensions, reservations and per-player charges.
- Retail: barcode, variants, units, serials and multiple warehouses.
- Add other industries based on validated demand.

**Exit gate:** module-specific workflow and recovery tests pass without bypassing shared core rules.

## Phase 5 — Licensing and packaging

- Separate License Manager, signed offline entitlements, module/expiry/device/branch limits and renewal workflows.
- Installer, upgrade/uninstall policy, data migration, backup guidance and code signing assessment.
- Define expired-license data access/export behavior.

**Exit gate:** invalid/tampered/expired entitlements fail safely; no private signing key is present in the main app; business data is preserved.

## Phase 6 — Online capabilities

- Secure API, hosted deployment option, QR ordering, online reservations, notifications and multi-branch administration.
- Define synchronization ownership, retries, conflict handling, observability and service recovery.
- Add PostgreSQL deployment and migration tests.

**Exit gate:** security review, integration/load tests, recovery drills and documented operations.

## Continuous quality gates

- Unit, integration, architecture and UI tests where feasible.
- Accounting, authorization, inventory concurrency, reservation conflict, timer recovery and idempotency coverage.
- Performance measurements against representative data volumes.
- Automated dependency/security checks and secret scanning.
- Windows manual validation for printing and device integration; Linux CI alone is not proof of Windows UI compatibility.
- Release notes, known limitations, backup/restore verification and reproducible build instructions.
