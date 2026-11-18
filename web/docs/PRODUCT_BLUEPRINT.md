# MG Business Web — Product Blueprint

## Product decision

MG Business Web is a standalone, browser-first business management product. It must be installable as a Progressive Web App (PWA), work on desktop/tablet/mobile, and must not require the Windows desktop application to be installed or running. It may share business concepts and compatible domain rules with other MG products, but it owns its web runtime, API, authentication, database schema, deployment lifecycle, and release process.

The browser is a client, never the authority for money, stock, permissions, licensing, or order state. All authoritative operations are validated and committed by the server.

## Product principles

1. Human-first workflows: common tasks are obvious, short, keyboard accessible, touch friendly, and recover gracefully.
2. One business, one source of truth: all users, branches, channels, and devices operate on server-owned records.
3. Modular by entitlement: modules can be enabled per tenant without hiding shared core behavior or breaking data integrity.
4. Secure by default: tenant and branch scope are enforced on every server request; deny by default; MFA-ready; auditable sensitive actions.
5. Reliable operations: idempotent writes, explicit transaction boundaries, durable background jobs, backups, restore drills, observability.
6. Progressive enhancement: core workflows remain usable on narrow screens and intermittent connections; offline writes use a durable outbox and conflict-aware replay.
7. Accessible and international: Persian RTL and English LTR are first-class, not afterthoughts; keyboard navigation, semantic controls, readable contrast.
8. Honest readiness: demo data and mock integrations are visibly marked; a feature is complete only after acceptance tests and operational verification.

## Product surfaces

- Public tenant storefront: configurable catalog, services, opening hours, online ordering, booking, customer account and loyalty.
- Authenticated business workspace: dashboard, sales/POS, orders, catalog, inventory, purchasing, customers, suppliers, accounting, staff, reports, settings.
- Platform administration: tenant lifecycle, plans, entitlements, support diagnostics and platform audit; isolated from tenant workspaces.
- PWA shell: installability, update lifecycle, push-ready notification architecture and carefully scoped offline support.

## Initial industry packs

Shared core is delivered first. Industry packs are independent feature modules:
- Retail and wholesale: variants, barcodes, price lists, warehouses, purchase/sales returns.
- Cafe and restaurant: tables, tabs, modifiers, recipes, kitchen display/queue, split/merge/transfer checks, service charges and waste.
- Game cafe: device/room registry, tariffs, sessions, pause/resume, per-player billing, reservations and settlement.
- Services: appointments, resources, staff schedules, service catalog and deposits.
- General business: CRM, projects/jobs, recurring invoices, expenses and approvals.

A module must declare its navigation, permissions, migrations, events, reports, API contract and tests.

## Target architecture

- Frontend: React + TypeScript + Vite, typed API client, route-level lazy loading, design system, RTL/LTR localization, PWA.
- Backend: ASP.NET Core Web API on supported .NET LTS, modular monolith initially (not premature microservices), OpenAPI contract, background worker.
- Data: PostgreSQL as authoritative multi-tenant database; Redis only when justified for distributed cache/queues; object storage for documents and backups.
- Identity: standards-based OIDC-compatible identity, secure HttpOnly cookies for browser sessions, CSRF protection, short-lived access tokens only where required, MFA-ready.
- Tenancy: tenant context derived from authenticated membership, never trusted from a client-supplied tenant ID alone; row-level and service-layer authorization.
- Deployment: containerized API, worker and PostgreSQL; reverse proxy with TLS; environment-based secrets; migrations as controlled release steps.
- Integrations: provider adapters and signed webhooks; local hardware bridge only for capabilities browsers cannot safely access (receipt printers, serial/USB terminals).

## Core bounded contexts

Identity & Access; Tenant & Branch; Licensing & Plans; Catalog & Pricing; Inventory; Sales & POS; Orders & Fulfillment; Purchasing; Customers & CRM; Accounting; Workforce; Scheduling & Reservations; Industry Packs; Notifications; Reporting; Files; Audit & Compliance.

## Non-negotiable data rules

- Money uses decimal fixed precision and explicit currency; never JavaScript floating-point as the financial authority.
- Posted journals are balanced, immutable records; corrections use reversals/adjustments.
- Stock changes are ledger movements; available stock is derived under concurrency-safe rules.
- Every write that may be retried supports idempotency keys.
- All timestamps are stored in UTC and rendered in tenant timezone.
- Every business record is tenant-scoped; branch-scoped records additionally carry branch scope.
- Audit records include actor, tenant, action, target, timestamp, correlation ID and safe change metadata.
- Deletion of financially relevant records is replaced by void/archive/reversal workflows.

## Quality gates

A release is not production-ready until:
- Frontend unit/component/accessibility tests pass.
- Backend unit/integration/API contract tests pass against PostgreSQL.
- Tenant isolation and role/branch authorization tests pass, including negative cross-tenant tests.
- Accounting, inventory concurrency, idempotency and order lifecycle scenarios reconcile.
- Dependency, secret, SAST and container scans pass with reviewed exceptions.
- Backup restore, migration rollback/forward strategy and disaster recovery are rehearsed.
- Representative load, latency and browser/device tests pass.
- Real payment, email/SMS, printer and domain integrations are tested in sandbox and then explicitly enabled.
- Operator runbooks, privacy/retention policy, release notes and known limitations are published.

## Delivery sequence

1. Product foundation: requirements, UX flows, domain glossary, threat model, architecture decisions, CI and reproducible builds.
2. Platform core: tenant onboarding, identity, users/roles, branch model, audit, settings, localization and module entitlements.
3. Business core: catalog, customers, suppliers, inventory ledger, POS/orders, payments and receipts.
4. Accounting: chart of accounts, journals, AR/AP, cash/bank, reconciliation, periods, statements and correction workflows.
5. Industry packs: retail, cafe/restaurant, game cafe, services.
6. Public channels: storefront, customer accounts, online order, booking, loyalty and notifications.
7. Enterprise operations: multi-branch, imports/exports, approvals, integrations, reporting and platform administration.
8. Hardening and launch: security review, load tests, recovery drills, deployment automation, documentation and pilot acceptance.

## Current implementation boundary

The existing `web/` folder is a visual/interaction prototype using browser localStorage. It is useful as a UI exploration, but it is not a backend, a secure multi-user system, or an authoritative business application. Existing demo flows must remain clearly labeled and must not be used for real customer, payment, accounting or inventory data until replaced by server-backed workflows.
