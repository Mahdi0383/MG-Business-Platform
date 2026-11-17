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
| StoreProduct, stock movements | No ownership columns | BusinessId + BranchId; define product catalog vs per-branch stock explicitly |
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
