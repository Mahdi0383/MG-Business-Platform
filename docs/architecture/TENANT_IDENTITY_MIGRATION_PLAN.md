# MG Business Platform — Tenant Identity & Data-Isolation Migration Plan

**Status:** Architecture proposal; not an implementation or a production-readiness claim.  
**Baseline inspected:** `main` at `aef7fe11373ba05a0c2f9cf4d039a0adc04f31c8` (2026-11-17).  
**Tracking issue:** #120.

## 1. Verified baseline

- `Business` and `Branch` domain records exist in `MG.BusinessPlatform.Domain/Organizations/Business.cs`.
- `BusinessDbContext` currently persists operational records but has no `Businesses`, `Branches`, `ApplicationUsers`, or `BusinessMemberships` sets.
- Some records already carry a `BranchId` (notably game sessions, reservation resources and purchase invoices); many other business records have no tenant or branch owner.
- Storefront configures SQLite, licensing, forwarded headers and rate limiting, but has no authentication/authorization registration or operator routes protected by authorization policies.
- Audit events currently identify the local Windows account and machine. This is not a trustworthy web-user identity.
- Public storefront routes must remain distinct from authenticated operator routes.

## 2. Target ownership model

Use a stable `BusinessId` as the tenant boundary and a `BranchId` for branch-scoped operations.

### Identity and membership

- **BusinessRecord**: tenant root; legal/display names, default currency, time-zone ID, active/suspended state, created/updated timestamps.
- **BranchRecord**: belongs to exactly one business; unique (BusinessId, Code), active state and contact/address fields.
- **ApplicationUserRecord**: normalized unique login/email, password hash managed by a proven identity framework, security stamp, lockout and disabled state. Do not store plaintext passwords or custom password hashes.
- **BusinessMembershipRecord**: (BusinessId, UserId), status, invitation/activation timestamps and optional default branch. A user may belong to multiple businesses.
- **RoleRecord / PermissionRecord / RolePermissionRecord / MembershipRoleRecord**: permissions are explicit and evaluated within the membership's business scope. Avoid global role names granting cross-tenant access.
- **UserSession / RefreshToken record** (if cookie/session design requires persistence): hashed token only, expiry, rotation/reuse detection, revocation and security-stamp linkage.

Identity records must use restrictive foreign keys and unique indexes. User IDs supplied in a request are never proof of identity or membership.

## 3. Operational data ownership inventory

Add non-null `BusinessId` to all tenant-owned operational records. Add `BranchId` where the operation is branch-specific; do not assume a branch GUID alone proves ownership.

| Existing record family | Tenant scope | Branch scope |
|---|---|---|
| StoreProduct, stock movements | Required | Required for branch-specific stock; define central warehouse semantics before rollout |
| Customers, suppliers | Required | Optional shared customer/supplier master; branch relationships must be explicit |
| Sales invoices/lines, payments, journal entries/lines, ledger accounts | Required | Required for branch reporting/posting |
| Purchase invoices/lines, supplier payments | Required | Required |
| Customer orders/lines | Required | Required for the fulfilling branch |
| Game sessions/players | Required | Required (already has BranchId) |
| Reservation resources/reservations | Required | Required through resource and reservation |
| Store settings, audit events | Required | Optional where meaningful |
| License/module entitlements and public hostname mapping | Required | Usually business-level; branch override only if explicitly supported |

Every dependent child row must inherit and validate the same owner as its parent. Prefer composite alternate keys and composite foreign keys such as (BusinessId, InvoiceId) -> SalesInvoices(BusinessId, Id), rather than relying only on application convention.

## 4. Safe single-store migration strategy

Do not add nullable tenant columns and start serving traffic. Migration must be deterministic and atomic.

1. **Preflight:** inventory every existing row and every branch reference; validate duplicate codes, orphaned branch IDs, missing products/parents, and ambiguous ownership. Produce a dry-run report.
2. **Bootstrap:** create exactly one initial Business and a default Branch with stable IDs recorded in migration/bootstrap metadata. Never generate a different tenant on every startup.
3. **Backfill:** assign BusinessId to every existing tenant-owned row. Map known BranchIds to the default business only after verifying each referenced branch is known; create a deterministic default branch for legacy rows only when the operator has explicitly approved that mapping.
4. **Abort on ambiguity:** if any row cannot be mapped safely, stop before schema constraints are tightened. Preserve a pre-migration backup and leave the old database usable.
5. **Constrain:** add NOT NULL, unique indexes, composite foreign keys and check constraints after successful backfill. Ensure migration and rollback tests cover populated databases, not only empty in-memory databases.
6. **Verify:** compare row counts and financial totals (invoice totals, payments, journal debit/credit, stock quantities/movements, open sessions and reservations) before and after migration.
7. **Enablement:** keep multi-tenant hosting and operator APIs disabled until isolation tests and authorization policies pass.

Migration must be restart-safe and transactional where SQLite permits. Backup restore must be tested, not merely backup file creation.

## 5. Request scope and authorization rules

- Authenticate using a maintained framework implementation (ASP.NET Core Identity or an equivalently reviewed OIDC provider); enforce secure cookie settings, CSRF protections for cookie-authenticated mutations, rate limits, lockout and session revocation.
- Resolve the active business from a verified membership and server-side selection; resolve branch from that membership's allowed branches. Never trust `BusinessId`, `BranchId`, role, permission or user ID in request JSON/query parameters as authority.
- Every operator endpoint requires an explicit policy. Public catalog/order/reservation endpoints are explicitly anonymous and are scoped by a verified hostname-to-business mapping plus that business's publication/license settings.
- Every query and write must apply tenant scope at the data-access boundary. Add save-time ownership validation and database constraints; UI filtering is not security.
- Return non-disclosing not-found/forbidden responses consistently to avoid confirming another tenant's record IDs.
- Audit the authenticated subject, business, branch, action, entity and correlation ID. Never record passwords, session tokens, payment card data or sensitive payload values.
- Do not put tenant data in shared caches without tenant-aware keys. Background jobs must carry an explicit, validated tenant scope.

## 6. Required test gates

### Migration and persistence
- Upgrade a representative populated legacy SQLite database; assert every row receives the intended tenant/branch.
- Abort on orphaned/ambiguous legacy data and verify original DB remains recoverable.
- Reject cross-business parent/child references at database level.
- Verify unique constraints are tenant-scoped where appropriate and global only where intended.
- Verify financial totals, stock balances, reservations and game sessions are unchanged after migration.

### Authentication and authorization
- Anonymous operator request is rejected.
- User from business A cannot read, mutate, export or infer records belonging to business B by changing route IDs, query parameters, request bodies or headers.
- Membership removal, user disablement, password reset/security-stamp change and session revocation take effect.
- Branch-restricted users cannot access another branch in the same business.
- Role/permission escalation and stale cached permissions are rejected.
- Public storefront host mapping cannot be manipulated to select an arbitrary tenant.

### Concurrency and operations
- Concurrent bootstrap/migration does not create duplicate default businesses or branches.
- Concurrent membership changes and revocation are deterministic.
- Backup/restore, audit retention, log redaction and tenant-aware cache behavior are exercised.

## 7. Implementation sequence

1. Introduce persistence records and model configuration for Business, Branch and membership/role primitives, with isolated unit/model tests. Do not yet expose operator routes.
2. Implement migration preflight/backfill and populated-database integration tests; require an explicit legacy mapping decision for ambiguous rows.
3. Add authenticated principal and membership resolution, secure session lifecycle and permission policies.
4. Add tenant/branch scope services and enforce them in each application service/repository; convert all record families and relationships.
5. Protect operator endpoints and add audit principal context.
6. Run all cross-tenant tests and security review; only then enable multi-tenant deployment.

## 8. Explicit release blockers

Until the above gates pass, the storefront is a public order-request prototype only. Do not enable live online payment, operator browser administration, multi-tenant customer data, or claim tenant isolation. Existing POS/desktop single-store functionality must remain compatible and must not share a SQLite database file over a network share.
