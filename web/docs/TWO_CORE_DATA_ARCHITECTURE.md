# MG Business Web — Two-Core and Data-Isolation Architecture

## 1. Product topology

MG Business Web has two separately governed application cores that share a platform foundation but have different responsibilities and security boundaries.

### Core A — MG Creator / Platform Control Plane

The creator console is the owner-operated control center for the MG platform. It is not a business workspace and must never be exposed to ordinary tenant users.

Responsibilities:
- Platform-wide overview: tenant count/status, service health, capacity, release status and operational alerts.
- Business lifecycle: approve/onboard, suspend/reactivate, archive and offboard businesses.
- Plans, subscriptions, licenses, feature entitlements, quotas and billing policy.
- Global module catalog, version compatibility, feature flags and staged rollouts.
- Platform identity administration, creator staff, support roles and privileged access policy.
- Tenant database provisioning, migration orchestration, health, backup/restore status and retention policy.
- Global integrations, provider credentials, domain verification and platform-wide security settings.
- Audit, security events, abuse handling, incident response and support diagnostics.

Creator permissions are split by capability (for example platform owner, operations, billing, support, security auditor). There is no universal shared admin password. Support access to tenant business content is denied by default; exceptional access requires explicit purpose, time limit, approval where configured, tenant-visible audit trail and automatic expiry.

### Core B — Business Operating Plane

Every approved business receives an isolated workspace and its own business data boundary. It is operated by that business's owner and staff, not by the MG creator console.

Responsibilities:
- Business profile, branches, work areas, settings, timezone, currency and tax configuration.
- Users, invitations, roles, permissions, branch scopes and device sessions.
- Shared business modules: catalog, sales/POS, orders, inventory, purchasing, customers, suppliers, accounting, staff, reports.
- Optional industry modules enabled by MG entitlements (retail, wholesale, cafe, restaurant, game cafe, services, and future packs).
- Public customer-facing site/portal, online orders, booking, loyalty and customer account experiences.
- Business-level integrations, operational audit, exports and backup/retention visibility.

The business owner controls their own staff and customers within the entitlements and policies set by MG. A business cannot read or change another business's records.

## 2. Required logical hierarchy

**MG Creator → Business → Business Users → Business Customers**

1. MG Creator owns and governs the platform.
2. A Business (tenant) is an independently configured organization with one or more branches.
3. Business Users are staff/accounts granted membership in that business, with role- and branch-scoped permissions.
4. Business Customers are the business's CRM/customer records. A customer may optionally activate a customer portal identity, but is not automatically a staff user and cannot access business administration.

A person may be a staff member in one business and a customer of another. Global identity is distinct from tenant membership and from a tenant's customer record. Never infer permissions from a person's email, phone number, or customer status.

## 3. Data hierarchy and authority

**MG Central Database → Business Central Database (one isolated database per business) → Device-local user data**

### MG Central Database — platform control-plane authority

Stores only platform-level records:
- Global identity references and authentication/session metadata.
- Platform operators, role assignments and privileged-access grants.
- Business/tenant registry, ownership and lifecycle state.
- Plan, subscription, license, module entitlement, quotas and billing metadata.
- Mapping from tenant ID to its database provisioning/connection secret reference (never plaintext secrets).
- Schema version, provisioning status, health/backup metadata and platform audit.
- Global integration/provider configuration and platform release/feature-flag metadata.

It is not the default repository for tenant sales, customer lists, inventory, accounting journals or other business transactions.

### Business Central Database — business operational authority

Each business gets a separate logical database and separate credentials/secret boundary. It stores that tenant's:
- Business profile, branches, staff memberships and business authorization data.
- Products, variants, price lists, stock ledger and warehouses.
- Sales, orders, payment references, returns and receipts.
- Customer and supplier records, consent/preferences and CRM activity.
- Accounting chart, journals, receivables/payables, cash/bank and financial periods.
- Industry-pack data, bookings, game sessions, tables/tabs, recipes and operational events.
- Business audit trail, document metadata and synchronization/idempotency records.

Tenant databases are provisioned, migrated, backed up and restored independently. Tenant registration and its provisioning work item are committed atomically to the MG central database; a durable queue is present, while the worker and actual database creation remain release blockers. A tenant ID is still included and checked in relevant records/events as defense in depth. Cross-tenant reporting uses explicit, minimized, aggregated pipelines—not direct access from one tenant database to another.

### Device-local data — local replica, never a competing authority

Each browser/device may hold a per-user, per-device local store (IndexedDB with an appropriate local-protection strategy) for:
- UI preferences, cached reference data and recently viewed records.
- Explicitly approved offline drafts and a durable outbox of pending commands.
- Sync cursor, device registration metadata and conflict-resolution state.

Device-local storage is not the authoritative database and must not be treated as a separate permanent database of record. Financial posting, final checkout, stock issue/receipt, permission changes and other sensitive operations require server validation. Offline operations are queued as commands with idempotency keys and replayed when online; the server revalidates current permissions, prices, stock and business rules. Conflicts are surfaced for review—never silently overwrite authoritative data. Sensitive fields/tokens are not cached unnecessarily; logout, revocation and device removal trigger scoped local data cleanup.

## 4. Request routing and isolation

1. User authenticates against the platform identity service.
2. The platform resolves the selected business membership and checks account/business status.
3. A trusted tenant resolver maps the authorized tenant ID to its database secret reference.
4. The API obtains a tenant-scoped connection from a controlled connection provider; clients never submit connection strings or choose database names.
5. Business authorization checks role, permission, branch scope, record ownership and module entitlement on every request.
6. The request is executed only against the selected business database; cross-tenant fallback is forbidden.
7. Security-relevant and financial actions are audited with actor, tenant, device/session, correlation ID and outcome.

Tenant IDs supplied in URLs/headers are selectors only, never proof of access. Database routing is server-side. Use parameterized queries, least-privilege database credentials, encrypted transport, secret-manager references, connection-pool isolation and safe migration orchestration.

## 5. Customer-facing surface

Customer pages are a separate presentation surface of the business core, not a fifth administrative authority:
- A customer enters through a business-specific verified domain/subdomain or published storefront.
- The server resolves the business storefront and exposes only published catalog, public content and allowed availability.
- Customer identity is optional; guest checkout/booking may be allowed by business policy.
- A customer can access only their own portal profile, orders, bookings, loyalty balance and consented communications for that business.
- A customer account never grants staff permissions. Customer records remain owned by the business database.

## 6. Deployment shape

Start as a modular monolith with clear control-plane and business-plane modules, not as premature microservices:
- MG Control Plane API + creator web application.
- Business API + business workspace/customer storefront application (separate route bundles and authorization policies; may share a frontend component system).
- Central PostgreSQL database for MG control-plane data.
- Tenant database provisioning service and one PostgreSQL database per business, on managed clusters/instances as scale and residency require.
- Worker(s) for provisioning, migrations, notifications, billing events, backups and durable outbox processing.
- Object storage partitioned by tenant with server-enforced access policies.
- Optional cache/queue only when justified by measured workload.

A shared PostgreSQL cluster may host many isolated tenant databases initially to control cost; high-risk or high-volume tenants can move to dedicated clusters without changing the logical ownership model. Database-per-business is the logical isolation boundary; physical cluster placement is an operational decision.

## 7. Non-negotiable security and recovery gates

- Creator console and business workspace have separate authorization policies, navigation, API scopes and audit categories.
- Platform operators cannot browse tenant content by default.
- Automated tests prove tenant A cannot read/write tenant B data through IDs, filters, exports, jobs, files, reports or websocket channels.
- Provisioning is idempotent and failure-safe; a business is not activated until database, migrations, owner membership and baseline settings are ready.
- Per-tenant backup and restore is tested; restore cannot overwrite another tenant.
- Device revocation and user offboarding invalidate sessions and pending access promptly.
- Data export, retention, deletion and legal-hold workflows are explicit and auditable.
- Central control-plane outage must not silently corrupt tenant data; define and test degraded-mode behavior for already-running business APIs.
