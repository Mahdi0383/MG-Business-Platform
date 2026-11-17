# MG Business Web — Backend Contract and Production Boundary

**Status:** design contract; implementation and security verification are not yet complete.

This document defines the minimum server contract required before the browser application can process real customer, inventory, payment, café, game-net, or accounting data. The current React/Vite application remains a demonstration until these controls exist.

## 1. Non-negotiable invariants

1. The server is authoritative for identity, tenant/store scope, permissions, prices, taxes, discounts, stock, balances, timers, and accounting postings.
2. Every business record is scoped by an immutable tenant ID and, where applicable, a store ID. The server derives these scopes from the authenticated principal; it never trusts tenant/store IDs supplied by the browser.
3. Every mutation is authorized on the server, validated at the boundary, audited, and committed transactionally.
4. Money is represented as integer minor units with an explicit currency code. Floating-point values are forbidden for persisted financial amounts.
5. Posted accounting entries are immutable. Corrections use linked reversal/correction entries; they do not rewrite posted history.
6. Requests that may be retried use idempotency keys. Replaying the same key and same payload returns the original result; reusing it with a different payload is rejected.
7. Offline browser storage is a queue/cache only. It is not an authoritative ledger, inventory balance, customer credit balance, or payment record.
8. No card PAN, CVV, PIN, terminal secret, signing private key, or server credential is stored in browser storage or returned by an API.

## 2. Identity, tenant isolation, and authorization

- Use ASP.NET Core Identity or an equivalent maintained identity provider with secure, short-lived access tokens and refresh-token rotation/revocation.
- Prefer same-site, HttpOnly, Secure cookies for a same-origin web deployment; protect state-changing cookie-authenticated requests against CSRF.
- Enforce MFA for tenant owners and privileged administrators.
- Roles are tenant-scoped and permissions are granular (for example `sales.read`, `sales.create`, `inventory.adjust`, `accounting.post`, `users.manage`, `licenses.manage`).
- Every query and command must apply tenant/store scope before loading or mutating a record. Add negative tests proving tenant A cannot read, update, export, or infer tenant B records by changing IDs.
- Never rely on hidden UI controls for authorization. Return 401 for unauthenticated requests and 403 for authenticated but unauthorized requests.
- Audit actor, tenant, store, action, target, timestamp, correlation ID, and safe before/after values for sensitive changes. Redact secrets and payment credentials.

## 3. API conventions

- Version routes under `/api/v1`; use JSON, UTC timestamps, opaque identifiers, pagination, stable error codes, and correlation IDs.
- Validate request DTOs server-side and return structured validation errors without stack traces.
- Apply request-size limits, rate limits, origin/host validation, HTTPS-only transport, and restrictive CORS.
- Use optimistic concurrency (ETags or row versions) for edits that can conflict.
- Require an `Idempotency-Key` for order submission, payment capture/confirmation, inventory adjustments, timer settlement, and accounting posting.
- Expose readiness/liveness separately. Public health endpoints reveal no configuration, tenant data, dependency credentials, or internal network topology.

### Initial endpoint groups

| Area | Routes (illustrative) | Required behavior |
| --- | --- | --- |
| Session | `POST /session/login`, `POST /session/refresh`, `POST /session/logout`, `GET /session/me` | Secure session lifecycle; revoke refresh token on logout |
| Tenant/store | `GET /tenants/current`, `GET /stores` | Scope comes from principal; membership checked server-side |
| Catalog | `GET/POST /products`, `PATCH /products/{id}` | Validate SKU, price, tax, active state, and store scope |
| Customers | `GET/POST /customers`, `PATCH /customers/{id}` | Normalize contact data; protect customer balances |
| Sales | `POST /orders`, `GET /orders/{id}`, `POST /orders/{id}/void` | Atomic order, stock, payment reference, and audit |
| Inventory | `GET /inventory`, `POST /inventory/adjustments` | Append-only stock movements; no direct balance overwrite |
| Accounting | `GET /ledger/accounts`, `POST /journal-entries`, `POST /journal-entries/{id}/post` | Balanced double-entry validation; immutable posted entries |
| Café | `GET/POST /cafe/tickets`, `POST /cafe/tickets/{id}/complete` | Kitchen state transitions and linked sale |
| Game Net | `GET/POST /gaming/sessions`, `POST /gaming/sessions/{id}/stop` | Server timestamps, tariff snapshot, idempotent settlement |
| Licensing | `GET /license/entitlements` | Signed entitlement validation; never expose private signing key |
| Audit | `GET /audit-events` | Permission-gated, paginated, tamper-evident audit history |

These route names are a contract proposal, not a claim that endpoints already exist.

## 4. Transaction boundaries

### Sale checkout
Within one database transaction:
1. Validate tenant/store membership and cashier permission.
2. Load current product prices, tax rules, discounts, and stock using server-side data.
3. Recalculate every line and total on the server; ignore browser-calculated totals.
4. Reserve/decrement stock through append-only stock movements.
5. Create the order and immutable line snapshots.
6. Record payment state/reference without storing sensitive card data.
7. Create balanced accounting postings according to configured accounting policy.
8. Write audit and idempotency records.
9. Commit all changes together or roll all of them back.

External payment-terminal operations require an explicit pending/confirmed/failed state machine and reconciliation path; a browser success toast is never proof of payment.

### Game session settlement
- Use server-generated start/stop timestamps and an immutable tariff/rate snapshot.
- Define rounding and minimum-charge policy explicitly.
- Prevent double settlement with a unique session settlement and idempotency key.
- A device transfer must close or transition the prior allocation atomically and preserve the full event history.

### Accounting
- Journal debits must equal credits in the configured currency for every posted journal.
- Validate account status, posting permission, open period, and tenant/store scope.
- Posted journals cannot be edited or deleted; corrections are new linked entries.
- Sales, refunds, inventory cost, expenses, customer credit, and payment clearing must reconcile to source records.

## 5. Offline and synchronization model

- Assign each client a stable installation ID and each queued command a globally unique operation ID.
- Queue only explicit commands with schema version, creation time, tenant/store binding, and idempotency key.
- On reconnect, the server re-authenticates the user and rechecks current permissions before accepting each command.
- Server responses include canonical entity version and conflict details. The client must not silently overwrite a newer server version.
- Conflicts involving stock, money, customer balances, timers, or posted journals require server-side resolution; never use last-write-wins for financial or inventory state.
- Expired sessions, revoked permissions, changed tenant membership, duplicate operations, and unsupported schema versions must fail safely and remain visible for operator resolution.

## 6. Data model minimum

Core records should carry tenant/store scope, created/updated timestamps, and concurrency/version metadata as applicable:

- Tenant, Store, User, Membership, Role, Permission, RolePermission
- Product, ProductVariant, AddOn, PriceList, TaxRule
- Customer, Supplier
- Order, OrderLine, Payment, Refund
- StockMovement, StockReservation, StockCount
- LedgerAccount, FiscalPeriod, JournalEntry, JournalLine
- CafeTicket, CafeTicketLine
- GamingDevice, GamingSession, GamingRate, GamingSessionEvent
- License, LicenseEntitlement
- AuditEvent, IdempotencyRecord, SyncOperation

Use foreign keys and unique constraints to enforce tenant-safe relationships. Add indexes for tenant/store plus common lookup keys. Define retention, backup, restore, and privacy/export policies before production onboarding.

## 7. Required automated security and correctness tests

- Cross-tenant read/write/export denial for every resource group.
- Role/permission matrix tests for all mutation endpoints.
- CSRF, origin, host-header, rate-limit, oversized-body, and malformed-input tests.
- Duplicate idempotency replay and mismatched-payload rejection.
- Concurrent checkout against limited stock; stock never becomes negative unless an explicit authorized policy permits it.
- Crash/failure injection between transaction steps; no partial order, stock, payment, or journal state.
- Journal imbalance rejection and posted-journal immutability.
- Duplicate game-session stop/settlement and tariff snapshot correctness.
- Offline replay after permission revocation, tenant switch, schema change, and concurrent server edit.
- Backup/restore and migration tests against generated multi-tenant data.
- Dependency audit, static analysis, and secret scanning in CI.

## 8. Production acceptance gates

Do not enable real customer or financial operations until all of the following are evidenced:

- Authenticated API deployed behind HTTPS with tenant isolation and authorization tests.
- Persistent database migrations, backup/restore drill, and recovery objectives documented.
- Server-authoritative sales, stock, payment reconciliation, and double-entry accounting implemented and tested.
- Café tickets and game sessions settle into the same accounting model without duplicate or missing postings.
- Offline sync conflict/retry behavior is deterministic and tested.
- Audit, monitoring, alerting, rate limits, and operational support procedures are active.
- Real hardware/payment integration is tested on supported devices and never stores cardholder secrets.
- Security review and production data migration/import rehearsal completed.

Until then, the web UI must visibly remain in demo mode and must not imply that browser-local data is production-safe.
