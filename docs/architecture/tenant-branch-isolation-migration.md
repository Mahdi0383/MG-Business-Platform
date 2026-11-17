# ADR: Tenant and branch isolation migration

- Status: Proposed
- Date: 2026-11-17
- Scope: SQLite persistence, desktop and Storefront hosts, accounting, inventory, game sessions, reservations, audit trail.
- This document is an implementation gate, not a claim that tenant isolation already exists.

## Context

The current persistence model mixes tenant-neutral records with partial branch ownership. GameSessionRecord, ReservationResourceRecord, and PurchaseInvoiceRecord carry BranchId; many other operational records do not. StoreSettings is constrained to singleton Id = 1. Several business identifiers are globally unique. The domain already contains Business and Branch concepts, but the EF persistence model does not yet provide complete ownership enforcement.

The database is also used by desktop/local workflows and the Storefront host. A web-request-only tenant assumption or an unconditional EF query filter would break trusted local workflows and initialization. Query filters alone cannot prevent cross-tenant foreign-key references or unsafe direct SQL.

## Required ownership model

1. Every tenant-owned row has a non-null BusinessId.
2. Branch-scoped rows additionally have a non-null BranchId; the database proves that the branch belongs to the same business.
3. Business-wide records use an explicit business scope, not a fabricated branch.
4. Every request/command receives an immutable execution scope from a trusted authenticated principal or an explicit trusted local operator context. Client-supplied tenant IDs are selectors at most and are never authority.
5. System migration/bootstrap jobs use a separate explicit system scope and cannot invoke customer-facing operational commands.

## Persistence inventory

| Record family | Current state observed | Required target |
| --- | --- | --- |
| StoreProduct catalog | No ownership columns; SKU is globally unique and StockQuantity is stored on the product | BusinessId; unique (BusinessId, SKU). Product identity and commercial fields are business-owned; remove StockQuantity as the authoritative multi-branch balance only after a verified migration. |
| Branch inventory balances and stock movements | StockMovementRecord has ProductId but no BusinessId/BranchId; StoreProduct has one global StockQuantity | BusinessId + BranchId + ProductId. Use a branch-scoped balance/projection and branch-scoped movements; enforce same-business product/branch references with composite keys. Preserve legacy quantity as an opening balance for one explicitly approved branch. |
| Ledger accounts | Global unique Code | BusinessId; unique (BusinessId, Code) |
| Sales invoices/lines, payments | No ownership columns; globally unique invoice/payment numbers | BusinessId + BranchId on header/payment; unique tenant-scoped numbers; child ownership proven through composite FK |
| Journal entries/lines | No ownership columns; global EntryNumber and (SourceType, SourceId) uniqueness | BusinessId + optional BranchId; tenant-scoped unique keys; account FK includes BusinessId |
| Purchase invoices | BranchId only | Add BusinessId and composite Branch ownership; tenant-scoped InvoiceNumber |
| Customers/suppliers | No ownership columns | BusinessId; explicit sharing policy if cross-branch |
| Game sessions/players | BranchId only | Add BusinessId; branch ownership FK |
| Reservation resources | BranchId only | Add BusinessId; branch ownership FK |
| Customer reservations | No ownership columns; global idempotency key | BusinessId + BranchId; composite resource FK; tenant-scoped idempotency and reservation number |
| Customer orders/lines | No ownership columns; global idempotency key | BusinessId + BranchId; tenant-scoped idempotency and order number; product ownership FK |
| StoreSettings | Singleton Id=1 | Replace with BusinessId-keyed settings and explicit public host uniqueness |
| Audit events | No tenant/branch/actor IDs; actor derived from process OS identity | BusinessId, optional BranchId, StaffUserId/system actor, correlation ID, immutable event metadata |

This inventory must be reconciled against the complete EF model and every write/query path before generating migrations.

## Migration sequence (expand, backfill, constrain)

### Phase 0 — preflight and compatibility
- Take a verified, restorable backup and run migration only on a copy of a production-like database.
- Inventory row counts, duplicate business identifiers, orphaned FKs, existing branch IDs, and all writes that create each record family.
- Define a deterministic legacy owner and default branch mapping. If the operator cannot attest to a single legacy business/branch, stop and require an explicit mapping file; never guess.
- Record before/after row counts and checksums for critical financial tables.

### Phase 1 — additive schema
- Add Business and Branch persistence tables and seed one legacy business/branch only after explicit mapping.
- Add nullable BusinessId/BranchId columns to legacy tables, composite unique parent keys, and supporting indexes.
- Do not yet drop old indexes or make ownership columns non-null.
- Introduce typed execution scope and an actor provider with explicit Staff, System, and LegacyUnattributed classifications.

### Phase 2 — deterministic backfill
- Backfill all existing rows to the approved legacy business and branch according to the inventory above.
- Preserve IDs, invoice/order/payment numbers, timestamps, journal sources, and audit history.
- Detect and report unresolved references and conflicting records. Abort rather than silently repair financial data.
- Verify every child record resolves to a parent in the same business.

### Phase 3 — enforce ownership
- Add non-null constraints and composite foreign keys/unique indexes, including (BusinessId, Id) parent keys where needed.
- Replace global unique indexes with tenant-scoped equivalents only after duplicate preflight proves safe.
- Ensure order, reservation, invoice, journal, product, and payment writes all use one scoped transaction.
- Make every query and command explicitly scoped; use query filters only as defense-in-depth, never as the sole isolation boundary.

### Phase 4 — identity and authorization
- Persist staff users, password hashes using a maintained password-hashing implementation, sessions with expiry/revocation, lockout/rate limits, and role/permission assignments.
- Resolve tenant/branch scope from the authenticated identity and verify membership/active status on every request.
- Deny by default. Add authorization to fulfillment, payment settlement, reservation approval, settings, inventory and accounting writes.
- Audit the authenticated staff actor, tenant, branch, action, entity, timestamp and correlation ID. Background/system work uses a distinct system actor.
- Do not expose privileged endpoints until all authorization and isolation tests pass.

### Phase 5 — retire compatibility
- Remove legacy singleton settings and old global indexes only after all supported clients use the new schema.
- Document minimum supported database version, upgrade/rollback behavior and backup retention.
- Test clean install, every supported upgrade path, interrupted migration recovery, restore, and mixed desktop/Storefront concurrency.

## Mandatory tests

- Two businesses may use the same SKU, invoice number, account code, payment number and order number without collision; duplicates within one business are rejected.
- A row in business A cannot reference a branch, product, invoice, account, reservation resource or order from business B, including through direct SQL where FKs apply.
- Anonymous, disabled, expired and revoked staff identities cannot access protected endpoints.
- Staff with branch A membership cannot read or mutate branch B records by guessing IDs or changing request payloads.
- Cross-tenant reports, exports, search, CSV import, background jobs and audit queries are isolated.
- Duplicate fulfillment/payment retries remain idempotent within tenant scope.
- Migration preflight fails on ambiguous legacy mapping, duplicate keys or orphan rows and leaves the original database untouched.
- Upgrade/restore tests preserve financial totals, journal balance, inventory quantities and audit history.

## Release gate

No multi-tenant or public staff-management production claim is permitted until schema constraints, identity, authorization, tenant-aware audit, migration/restore and the complete integration test matrix pass on a production-like database. The current static web prototype remains non-production.

## Existing test harness and remaining coverage

The repository already has a consolidated xUnit project, `MG.BusinessPlatform.Domain.Tests`, which references Infrastructure and Storefront. It includes SQLite migration/persistence tests and a Storefront HTTP integration suite at `tests/MG.BusinessPlatform.Domain.Tests/Storefront/StorefrontOrderIntegrationTests.cs`. The tenant rollout should extend this existing test infrastructure rather than assume no HTTP integration harness exists.

The current public-order integration coverage does not establish staff authentication or tenant isolation. Add integration cases for:
- two businesses using the same SKU, account code and document numbers without cross-tenant collisions;
- cross-business and cross-branch IDOR attempts for every staff-facing resource;
- anonymous, disabled, expired and revoked staff sessions;
- role and branch membership boundaries for fulfillment, settlement and reservation state transitions;
- audit events attributed to the authenticated staff identity or an explicit system actor;
- tenant-aware composite FK failures using direct database writes;
- preflight failure on ambiguous legacy ownership, duplicate keys and orphan references, with the original database remaining unchanged.

Keep disposable SQLite databases and real HTTP host execution in the acceptance suite. A dedicated test project is optional; coverage and isolation guarantees are mandatory.

## Decisions that must be settled before schema implementation

The following target decisions are recommended to avoid mixing business ownership with branch-level operations. They are design constraints for the next implementation phase, not claims about the current schema.

### Deployment boundary

- A normal locally hosted installation represents one Business and one or more Branch records. The local operator is not allowed to select an arbitrary BusinessId to gain access to another tenant.
- A shared-host deployment may contain multiple businesses in one database only after the full ownership constraints, authenticated execution scope, tenant-aware uniqueness and cross-tenant integration tests are implemented.
- Both deployment modes use the same persisted ownership model. Local/single-business mode is not a bypass around ownership validation; it is a trusted installation bootstrap that binds the local operator to its provisioned business.
- Storefront public host resolution must map an allow-listed hostname to exactly one active Business and its explicitly published storefront settings. A singleton StoreSettings lookup is not valid for a multi-business host.

### Catalog versus branch inventory

- Treat product identity and commercial description as business-owned catalog data: SKU, name, description, base selling price, currency and availability policy belong to Business.
- Treat on-hand quantity and stock movements as branch-owned operational data. Introduce a branch inventory balance (or equivalent branch-scoped stock ledger projection) keyed by (BusinessId, BranchId, ProductId); do not use one shared StoreProduct.StockQuantity value as the authoritative quantity for multiple branches.
- Purchase receiving, web-order reservation/issue, POS sale, adjustment and transfer must update the branch stock ledger/balance in the same transaction as their source document. Branch transfers require paired outbound/inbound movements and an idempotent transfer identity.
- If the product's purchase cost differs by receipt, preserve cost layers or a documented costing method (for example weighted average) at the branch level. Do not silently overwrite one global UnitCost when branches have different receipt costs.
- A business may deliberately choose a branch-specific assortment or price override, but that is an explicit branch-product policy/override, not duplicated product identities by default.

### Operational ownership

- Devices, game stations and reservation resources belong to exactly one Branch; their parent Branch must belong to the same Business.
- Game sessions, reservations, orders, invoices, payments, stock movements and operational audit events carry BusinessId and, where activity occurs at a branch, BranchId.
- Customers and suppliers are Business-owned by default and may be visible to authorized branches of that business. Cross-business sharing is prohibited unless a separate, explicit data-sharing feature is designed and audited.
- Ledger accounts and chart-of-account configuration are Business-owned. Journal entries carry BusinessId and optional BranchId; every journal line must reference an account in the same Business.
- Store settings are Business-owned. Branch-specific settings (receipt header, terminal configuration, opening hours and branch contact details) must be stored separately from business-wide legal identity and accounting defaults.

### Migration consequence

Before generating EF migrations, update the inventory table above to distinguish business-owned catalog from branch-owned stock, and enumerate every write path that currently mutates StoreProduct.StockQuantity. The migration must preserve legacy quantities as opening balances for the explicitly mapped legacy branch, reconcile quantity totals against stock movements, and reject ambiguous or inconsistent legacy state rather than fabricate a distribution across branches. Tests must prove same-business branches cannot read each other's stock and cross-business products cannot be referenced by orders, purchase lines or stock movements.

## Verified schema facts from the current main branch

This section records concrete findings from `BusinessDbContext` and the existing SQLite migrations. It is an inventory, not a proposed claim that isolation already exists.

| Area | Verified current state | Required migration direction |
| --- | --- | --- |
| Store settings | `StoreSettings` is constrained to singleton `Id = 1`. | Replace singleton assumption with Business-owned settings and separately scoped branch settings; map existing row explicitly. |
| Product catalog/stock | `StoreProducts` has one global unique SKU and one `StockQuantity`; the initial migration creates no BusinessId or BranchId. | Business-scoped SKU uniqueness; branch inventory balance and branch-scoped movements. Preserve old stock as one approved branch opening balance. |
| Stock movements | `StockMovements` records ProductId and time but no BusinessId or BranchId; no FK enforces product ownership. | Add ownership columns, composite product FK and branch-aware idempotent movement/source identity. |
| Customer orders | Order number and idempotency key are globally unique; order and order-line records do not carry ownership scope in the current mapping. | Scope order keys by business and attach order lines to products through same-business composite ownership. |
| Accounting | Account code, invoice number, payment number and journal entry number are globally unique. Journal source uniqueness is global on (SourceType, SourceId); journal lines reference accounts by AccountId only. | Business-scoped unique indexes; composite same-business FKs for invoices, payments, journal entries and accounts. Keep branch optional only for business-level postings. |
| Purchasing | Purchase invoice has BranchId, but supplier/product relationships are single-column references and the invoice number is global. | Enforce BranchId -> BusinessId and ensure supplier, product and invoice all share the same BusinessId. Scope invoice numbering by business (and optionally branch according to numbering policy). |
| Reservations | Reservation resource has BranchId; reservation has ResourceId but no BusinessId/BranchId. Reservation number and idempotency key are global. | Validate resource branch ownership, persist BusinessId/BranchId on reservation, and scope idempotency and number uniqueness to business. |
| Game sessions | Session has BranchId; one-open-session filtered unique index is currently keyed only by DeviceId. | Enforce device ownership by branch/business; use tenant-aware uniqueness without permitting two open sessions on the same physical device. |
| Audit | Audit rows currently identify actor from OS process identity and contain no BusinessId, BranchId or validated StaffUserId. | Introduce request-scoped authenticated actor and tenant scope; retain an explicit system actor for background/desktop maintenance and classify legacy events. |

### Migration preflight queries and invariants

Before adding non-null ownership columns or rebuilding indexes, a preflight command must produce a deterministic report and stop without modifying the database when any invariant fails:

1. Count businesses and branches; require an explicit operator-approved mapping for every legacy row. Do not infer the target branch merely from a current UI selection.
2. Check every non-null BranchId against an existing Branch and verify the branch's BusinessId. A branch with no business or an orphan reference is a hard failure.
3. Check all existing globally unique identifiers before replacing them with tenant-scoped indexes. Existing global uniqueness can be retained as a stricter interim constraint until the tenant migration is complete.
4. For each product, compare StoreProduct.StockQuantity with the sum of signed stock movements under the existing movement semantics. If there is no opening movement or totals disagree, require a reviewed reconciliation entry; do not silently generate balancing stock.
5. Check reservation resource references, game device references, invoice/order links, journal source links, journal-line account links, payment/invoice links, purchase/supplier links and purchase-line/product links for orphan or ambiguous ownership.
6. Produce counts and stable IDs only (no customer PII) in the preflight report; store the report hash and migration version in the backup/upgrade log.
7. Run the preflight against a restored copy of the actual production database before the migration is eligible for release. A successful empty-database migration is not sufficient evidence.

### Rollout safety

- Expand first with nullable ownership columns and supporting non-unique indexes; keep old constraints in place.
- Backfill from a reviewed, immutable mapping file/manifest; record the mapping version and operator approval. Abort on unmapped or multiply mapped rows.
- Validate ownership joins and stock reconciliation before enforcing NOT NULL, composite unique indexes and composite foreign keys.
- Constrain only after a second backup and restore verification. Keep a tested rollback/restore path; do not rely on reversing destructive SQLite table rebuilds as the sole recovery strategy.
- Do not enable shared-host mode or expose staff mutation endpoints while any ownership, identity, authorization or audit invariant remains unverified.
