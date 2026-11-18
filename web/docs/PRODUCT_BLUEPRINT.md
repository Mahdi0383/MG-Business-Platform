# MG Business Web — Product Blueprint

## Product decision

MG Business Web is a standalone, browser-first business platform. It is installable as a Progressive Web App (PWA), works on desktop/tablet/mobile, and does not require the Windows desktop application. It has two distinct application cores: the MG Creator Control Plane and the Business Operating Plane. Their authority, data, navigation, APIs and permissions are separated.

The browser is a client, never the authority for money, stock, permissions, licensing, or order state. All authoritative operations are validated and committed by the server.

## Product hierarchy

**MG Creator → Business → Business Users → Business Customers**

- The creator owns and governs the platform, businesses, plans, licenses, modules, provisioning and platform operations.
- Each business is an isolated tenant with its own workspace, branches, settings, users, customers and enabled modules.
- Business users are staff with explicit role-, permission- and branch-scoped access.
- Business customers belong to the business CRM and may optionally use that business's customer portal. Customer status never grants staff access.
- One global person identity may have memberships in multiple businesses and may separately be a customer of other businesses.

## Two application cores

### Core A — MG Creator Console

A separate owner-operated control center for platform-wide administration: business lifecycle, subscriptions/plans, licenses and module entitlements, creator staff and privileged access, tenant database provisioning/migrations, platform health, releases/feature flags, global integrations, backups/restore status, security events and platform audit. Platform support access to tenant content is denied by default and any exceptional access is time-limited, purpose-bound and audited.

### Core B — Business Workspace

An independently operated workspace for each business: profile and branches, staff/users/roles, catalog, POS/sales, orders, inventory, purchasing, customers/CRM, suppliers, accounting, workforce, reports, settings, business integrations and optional industry packs. Each business also has a customer-facing storefront/portal with access only to its published content and each customer's own records.

The creator console is not a business workspace; a business owner cannot access platform controls. The customer portal is a presentation surface of the business core, not an administrative tier.

## Data authority

**MG Central Database → one Business Central Database per business → per-user/per-device local replica**

- MG Central Database: global identity references, creator operators, tenant registry/lifecycle, plans/licenses/entitlements, tenant-to-database secret references, provisioning/schema/health/backup metadata, platform audit and global configuration. It does not store ordinary tenant sales, inventory, CRM or accounting transactions.
- Business Central Database: each business's authoritative profile, branches, staff membership/authorization, catalog, inventory ledger, sales, orders, payment references, customers, suppliers, accounting, industry data, business audit and sync/idempotency records. Databases are independently provisioned, migrated, backed up and restored.
- Device-local store: per-user/per-device cache, preferences, offline drafts and durable command outbox. It is a replica, not an authority or permanent database of record. Server revalidates permissions, prices, stock and business rules when commands sync. Conflicts are explicit; sensitive operations require server validation.

See [Two-Core and Data-Isolation Architecture](TWO_CORE_DATA_ARCHITECTURE.md) for the complete model, request routing, customer access, deployment and security gates.

## Product principles

1. Human-first workflows: common tasks are obvious, short, keyboard accessible, touch friendly, and recover gracefully.
2. One business, one source of truth: all users, branches, channels, and devices operate on server-owned records.
3. Modular by entitlement: modules can be enabled per tenant without hiding shared core behavior or breaking data integrity.
4. Secure by default: tenant and branch scope are enforced on every server request; deny by default; MFA-ready; auditable sensitive actions.
5. Reliable operations: idempotent writes, explicit transaction boundaries, durable background jobs, backups, restore drills, observability.
6. Progressive enhancement: core workflows remain usable on narrow screens and intermittent connections; offline writes use a durable outbox and conflict-aware replay.
7. Accessible and international: Persian RTL and English LTR are first-class; keyboard navigation, semantic controls, readable contrast.
8. Honest readiness: demo data and mock integrations are visibly marked; a feature is complete only after acceptance tests and operational verification.

## Product surfaces

- MG creator console: platform operations and tenant lifecycle.
- Business workspace: authenticated daily operations for each business.
- Public tenant storefront/customer portal: configurable catalog, services, opening hours, online ordering, booking, customer account and loyalty.
- PWA shell: initial manifest, install metadata and a service worker that caches only the static app shell/assets; API responses and business records are never cached by this worker. Installability/update lifecycle and carefully scoped offline command sync still require device testing and implementation.

## Initial industry packs

Shared core is delivered first. Industry packs are independent feature modules:
- Retail and wholesale: variants, barcodes, price lists, warehouses, purchase/sales returns.
- Cafe and restaurant: tables, tabs, modifiers, recipes, kitchen display/queue, split/merge/transfer checks, service charges and waste.
- Game cafe: device/room registry, tariffs, sessions, pause/resume, per-player billing, reservations and settlement.
- Services: appointments, resources, staff schedules, service catalog and deposits.
- General business: CRM, projects/jobs, recurring invoices, expenses and approvals.

A module declares its navigation, permissions, migrations, events, reports, API contract and tests.

## Target architecture

- Frontend: React + TypeScript + Vite, typed API client, route-level lazy loading, design system, RTL/LTR localization, PWA.
- Backend: ASP.NET Core Web API on supported .NET LTS, modular monolith with explicit control-plane/business-plane boundaries, OpenAPI contract and background workers.
- Data: PostgreSQL; MG central control-plane database plus isolated database per business; Redis only when justified; tenant-partitioned object storage.
- Identity: standards-based OIDC-compatible identity, secure HttpOnly browser sessions, CSRF protection and MFA-ready flows.
- Tenancy: tenant context derived from authenticated membership; server-side database routing and authorization; never trust client-supplied tenant IDs alone.
- Deployment: containerized APIs/workers/database, TLS, managed secrets, controlled migrations, backups and restore drills.
- Integrations: provider adapters and signed webhooks; secure local hardware bridge only for browser-inaccessible capabilities.

## Core bounded contexts

Platform Identity; Creator Access; Tenant Registry & Provisioning; Plans & Licensing; Module Entitlements; Business Identity & Membership; Branches; Catalog & Pricing; Inventory; Sales & POS; Orders & Fulfillment; Purchasing; Customers & CRM; Accounting; Workforce; Scheduling & Reservations; Industry Packs; Notifications; Reporting; Files; Audit & Compliance; Device Sync.

## Non-negotiable data rules

- Money uses decimal fixed precision and explicit currency; never JavaScript floating-point as financial authority.
- Posted journals are balanced, immutable records; corrections use reversals/adjustments.
- Stock changes are ledger movements; available stock is derived under concurrency-safe rules.
- Every retryable write supports idempotency keys.
- Store timestamps in UTC and render in tenant timezone.
- Every business record is tenant-scoped; branch-scoped records additionally carry branch scope.
- Audit records include actor, tenant, action, target, timestamp, correlation ID and safe change metadata.
- Financially relevant records are voided/archived/reversed, not hard-deleted.

## Quality gates

A release is not production-ready until frontend unit/component/accessibility tests and backend unit/integration/API contract tests pass against PostgreSQL; tenant isolation and role/branch tests include negative cross-tenant cases; accounting, inventory concurrency and idempotency reconcile; dependency/secret/SAST/container scans pass; per-tenant backup restore and migration strategy are rehearsed; representative load/browser/device tests pass; real integrations are sandbox-tested; and operator runbooks, privacy/retention policy, release notes and pilot acceptance are complete.

## Delivery sequence

1. Two-core domain boundaries, threat model, UX flows, glossary, architecture decisions, CI and reproducible builds.
2. MG control plane: creator identity/roles, tenant registry, plans/licenses/entitlements, provisioning, platform audit.
3. Business foundation: tenant DB lifecycle, owner onboarding, business profile, branches, users/roles/permissions, device registry and sync.
4. Business core: catalog, customers, suppliers, inventory ledger, POS/orders, payments and receipts.
5. Accounting: chart of accounts, journals, AR/AP, cash/bank, reconciliation, periods, statements and correction workflows.
6. Industry packs: retail, cafe/restaurant, game cafe, services.
7. Customer channels: storefront, customer accounts, online order, booking, loyalty and notifications.
8. Enterprise operations: multi-branch, imports/exports, approvals, integrations, reporting and advanced creator operations.
9. Hardening and launch: security review, load tests, recovery drills, deployment automation, documentation and pilot acceptance.

## Current implementation boundary

The existing `web/` folder is a visual/interaction prototype using browser localStorage. It is not a backend, secure multi-user system or authoritative business application. Existing demo flows must remain clearly labeled and must not be used for real customer, payment, accounting or inventory data until replaced by server-backed workflows.
