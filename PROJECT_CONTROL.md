# MG Business Platform — Master Project Control, Requirements and Continuation Ledger

> **Living source of truth.** This file persists project intent and execution rules across chat sessions and contributors. Update it in the same PR whenever scope, status, architecture, acceptance criteria, or blockers change. Chat history is supporting context; this repository is the durable project record.

- Product: **MG Business Web**, standalone browser-first application/PWA.
- Repository: `Mahdi0383/MG-Business-Platform`.
- Current governance change: [Draft PR #152](https://github.com/Mahdi0383/MG-Business-Platform/pull/152), branch `docs/mg-project-governance-and-continuation`, based on implementation PR #105 HEAD `8c574cd1d625566785f0a5a7e8236ad339c399ff` at the time this governance branch was created.
- Main implementation PR: [#105 — Start MG Business Web platform foundation](https://github.com/Mahdi0383/MG-Business-Platform/pull/105), open and Draft at the recorded snapshot. Always re-fetch live status; do not assume this snapshot is current.
- This document is not a claim that the product is complete. It records the destination, verified baseline, acceptance gates and next work.

## 1. Mission

Build a professional, high-performance, secure, dependable, extensible and genuinely usable business operating platform for many business types. The system should be easy for ordinary operators, powerful for owners and accountants, and maintainable as the product grows. Prioritize correctness, reliability, security, data integrity and operational readiness over superficial feature counts.

The platform must support small single-device businesses through multi-user/multi-branch organizations, with responsive browser/PWA access on desktop, tablet and phone. Customer-facing online services are optional per business and controlled by entitlements.

### Product boundary
- MG Business Web is a **standalone web product**. No Windows desktop app dependency is allowed for startup, authentication, business data, licensing, online ordering, or normal operations.
- GitHub Pages is a static preview host only; it is not the production API/database/worker host.
- Legacy Windows application assets and workflows may inform domain requirements and remain separately maintained, but the new web product must have its own API, identity, database, deployment and lifecycle.

## 2. Mandatory product hierarchy and two cores

**MG Creator → Business → Business Users → Business Customers**

### Core A — MG Creator Control Plane
Private platform-owner console, inaccessible to ordinary tenant users:
- Global overview, platform health, capacity, releases, alerts and operations.
- Business onboarding, verification, activation, suspension, reactivation, archive and offboarding.
- Plans, subscriptions, signed license/entitlement policies, module catalog, quotas, limits and renewal/expiry behavior.
- Creator staff identities, granular platform roles, MFA, privileged-access grants and support-access approvals.
- Tenant database creation, migration orchestration, health, backup/restore, retention and lifecycle.
- Global integrations/provider settings, domain verification, feature flags, rollout controls.
- Immutable platform audit, security events, incident response and operational diagnostics.
- Tenant content access denied by default; exceptional support access must be purpose-bound, approved where required, time-limited, tenant-visible and audited.

### Core B — Business Operating Plane
Each tenant receives its own workspace:
- Business profile, branches, locations, timezone, currency, tax and fiscal settings.
- Staff/users, invitations, roles, permission matrix, branch scopes, sessions and device management.
- Products/services, variants, modifiers/add-ons, units, barcodes, price lists and taxes.
- POS, quotations, sales, invoices, receipts, payment allocation, returns/refunds, cash shifts and order fulfillment.
- Customers, CRM, suppliers, purchasing, expenses, inventory, warehouses, stocktakes and transfers.
- Full accounting: chart of accounts, journals, general ledger, trial balance, P&L, balance sheet, cash flow, AR/AP, bank/cash, periods, budgets and audit.
- Reports, dashboards, exports, notifications, attachments and business audit.
- Optional industry modules; module data remains tenant-owned and isolated.
- Optional public storefront/customer portal for published catalog/services, online order, booking/reservation, customer account, order history and loyalty/club.

Customer records and portal accounts are not staff accounts. A customer sees only public published content and their own authorized records.

## 3. Data and deployment architecture

**MG Central Database → one isolated authoritative Business Database per tenant → local user/device cache + offline command outbox**

### Central MG database stores only platform control-plane data
Global identity references, creator operators/roles, tenant registry/lifecycle, plan/subscription/license/entitlements, tenant database secret references, provisioning and migration metadata, platform health/backup metadata, platform configuration and platform audit. It must not become the normal store for tenant sales, inventory, CRM or accounting transactions.

### Each tenant database is authoritative for that business
Business/branch data, users and memberships, roles, catalog, prices, inventory ledger, sales/orders/payments, customers/suppliers, journals/ledger, industry modules, business audit, idempotency and sync records. Each tenant has separate database credentials/secret boundary and independent migration/backup/restore lifecycle. Multiple isolated DBs may share a PostgreSQL cluster initially; high-volume tenants can later move to dedicated clusters.

### Per-device local storage
Use IndexedDB (or an equivalent carefully selected store) for cache, preferences, offline drafts, durable outbox, sync cursors and conflict state. It is not another authoritative database. Every replay is revalidated by the server. Posted finance, permissions, final checkout and sensitive operations cannot be trusted from local state. Service worker caches only static shell/assets, never sensitive API data.

### Deployment
Containerized ASP.NET Core API and background worker, PostgreSQL, secret manager, TLS termination at a trusted edge/reverse proxy, backups, monitoring and alerting. Support a managed deployment and a documented self-hosted path when operationally safe. Do not expose database ports/files publicly. A custom domain requires valid DNS, TLS and routable ingress; CGNAT/firewall constraints must be stated honestly.

## 4. Functional scope and detailed user requirements

The following is the durable product backlog, not a claim that each item exists today.

### 4.1 Shared foundation
- Multi-tenant identity, creator MFA, business membership, invitations, granular RBAC/permission policies and branch scoping.
- License/plan/entitlement engine: module availability determined server-side; expiry, suspension, quotas, device/branch/user limits and renewal flows.
- Multi-currency-ready money model, explicit currency and timezone, localized dates/numbers, Persian RTL and English LTR.
- Audit trail for sensitive changes; correlation IDs; structured logs; idempotency for retryable commands.
- Document/file handling, exports/imports, backup/restore, notifications, settings and configurable workflows.
- Responsive accessible design system, fast navigation, searchable lists, keyboard shortcuts where appropriate, touch-friendly controls and consistent app-styled dialogs.
- Import/migration tools for supported existing data with preview, validation, duplicate handling, rollback and audit.

### 4.2 POS and sales
- Fast multi-line cart, product/customer search with useful suggestions (not requiring exact full-name entry), barcode-ready workflows and configurable price/tax/discount.
- Product modifiers/add-ons with operator-managed names/prices; add-on selection per cart line and correct addition to line total.
- Multiple payment methods and split/partial payments, cash/card/terminal/transfer/credit according to business policy.
- Invoice discount and line discount policies; preserve original price snapshots and audit adjustments.
- Returns, refunds, void/cancel permissions, receipts and thermal-printer abstraction (58 mm and configurable widths); validate actual hardware before claiming compatibility.
- Cashier settlement/shift close, payment-method reconciliation and daily sales dashboard including best-selling products.
- Server-side authoritative checkout, stock concurrency, idempotency and atomic financial posting.

### 4.3 Inventory, products and purchasing
- Product/service CRUD, categories, variants, units, SKU/barcode, images, multiple price tiers, multiple purchase costs and supplier-specific costs.
- Product suggestions during purchase invoice entry; same product may be received at different purchase prices with correct costing policy.
- Purchase invoice discounts, additional costs, supplier balances/payables, purchase returns.
- Multiple warehouses, stock movements, transfers, stock count/adjustment, reorder alerts, reservations and negative-stock policy.
- Append-only inventory ledger, costing method explicitly configured and reconciled to accounting.

### 4.4 Full accounting
- Double-entry chart of accounts and configurable account hierarchy.
- Journal entries, general ledger, trial balance, income statement/P&L, balance sheet, cash-flow statement, AR/AP, cash/bank, expenses, income, fixed assets/depreciation where in scope, tax, fiscal periods and closing.
- Sales, purchases, payments, inventory and industry transactions post through explicit accounting integration; no separate disconnected ledgers.
- Posted journals immutable; corrections via reversals. Enforce debits = credits, closed-period protection and audit.
- Decimal arithmetic, rounding policy, currency precision and reproducible financial reports.

### 4.5 Cafe / restaurant module
- Tables/areas, dine-in/takeaway/delivery, open tabs, table transfer, merge/split checks, split by item/person, partial payments and waiter/cashier workflow.
- Product recipes/BOM, ingredient stock deduction, modifiers/add-ons, kitchen tickets/queue and preparation statuses.
- Service charge, tax, discount, waste/spoilage, staff attribution, daily close and profitability/cost reports.
- QR self-ordering (not waiter-only): business-controlled public menu, customer cart and order submission, with configurable approval/payment workflow.
- Customer receipt/invoice and customer-facing online ordering when enabled.

### 4.6 Game cafe / Game Net module
- Device/room/console registry, device availability/status and tariff profiles.
- Start/stop/pause/resume/extend sessions; timer recovery after app/device/network restart.
- Billing by elapsed time, fixed packages, per-player pricing and configurable rounding/minimums.
- Open session for a selected customer and display customer name alongside device name in the device tile.
- Transfer session between devices/customers; change device and player count; switch free↔paid mode with permission and audit.
- Group sessions and player search with suggestions; do not require exact full-name typing.
- Reservations, settlement and links to POS/customer account/accounting.
- Accurate session history, operator actions and recovery tests.

### 4.7 Customer-facing capabilities
- Per-business optional storefront/domain, public profile, catalog/services, hours, availability and contact information.
- Online ordering and booking/reservation with capacity/time validation, overlap protection, idempotency and clear approval state.
- Customer account/club, order and booking history, loyalty points/rewards where enabled, consent and notification preferences.
- Customer may only view their own private records. Public pages expose only explicitly published information.

### 4.8 Other industry modules
Design the platform to add retail/wholesale, professional services/appointments, repair/work orders, salon/clinic-like scheduling where legally appropriate, education/classes, rental/assets, hospitality, projects/jobs, subscriptions and other verticals. Do not prematurely implement every vertical; establish a reusable module contract and prioritize by validated need. Sensitive regulated workflows require separate compliance review.

### 4.9 Online domain and hosting requirements
- Customer website can use a business-owned domain and point to the business storefront.
- Domain verification, DNS guidance, TLS certificate lifecycle, routing and health diagnostics must be explicit.
- A shop-owned computer may be supported as a self-hosted node only after secure tunnel/reverse-proxy design, automatic recovery, updates and backups are implemented and tested. Do not claim a domain can be served from behind CGNAT or blocked inbound ports without a relay/tunnel.
- Never expose database folder/port or administrative endpoints to the public internet.

### 4.10 Android and mobile
- The web product must be responsive and installable as a PWA where supported.
- A native Android wrapper/client is a later optional delivery, not a separate competing product or a prerequisite for web operation. It must use the same authenticated API and offline command model.
- Low-bandwidth constraints matter: avoid unnecessarily large downloads, optimize assets and support resumable/retryable sync.

## 5. Module contract

Every module must declare:
- Stable module ID, version, compatibility, dependencies and entitlement key.
- Navigation and UI entry points; permissions and branch scopes.
- Tenant database migrations and downgrade/upgrade strategy.
- API endpoints, request/response validation and idempotency behavior.
- Domain events and integration contracts with accounting, inventory, customers and reporting.
- Localization keys, accessibility and responsive behavior.
- Unit/integration/security/acceptance tests, documentation, metrics and operational runbook.
- Data export/retention and behavior when module is disabled or license expires.

Shared core capabilities (identity, money, audit, documents, notifications, permissions, catalog primitives) must not be duplicated inside vertical packs.

## 6. Non-functional requirements

- **Security:** OIDC/OAuth2-compatible identity, MFA for creators and configurable business admins, secure sessions/cookies, CSRF protection where applicable, strict CORS, rate limits, secret manager, key rotation, dependency scanning, SAST/CodeQL, threat modeling, tenant isolation tests and safe error messages.
- **Reliability:** database transactions, durable jobs/outbox, retry with jitter, dead-letter review, idempotency, health/readiness, graceful shutdown, backups, restore drills and documented RPO/RTO.
- **Performance:** measure representative workloads; paginate/search server-side; index query paths; avoid N+1; lazy-load modules; profile slow queries and bundle size. Do not make unsupported performance claims.
- **Usability:** simple flows for cashier/operator, advanced controls for owners/accountants, responsive UI, clear empty/error/loading/offline states and undo/correction paths where safe.
- **Accessibility:** semantic controls, keyboard navigation, focus management, contrast, scalable text and screen-reader labels.
- **Localization:** Persian RTL and English LTR including numbers, currency, dates, printing and mixed-direction identifiers.
- **Maintainability:** modular monolith initially; explicit module boundaries; typed contracts; migrations; ADRs; reproducible builds; no premature microservices.

## 7. Current implementation snapshot — verify live before acting

This snapshot is anchored to the PR #105 head observed when this governance work started; it may become stale immediately.

### Implemented foundation in the web branch (not production-complete)
- React/Vite prototype for Creator console and Business workspace.
- Hash routes `/#/creator` and `/#/`; responsive styling.
- PWA manifest/icon/static shell service worker; service worker excludes business/API data.
- ASP.NET Core 10 + PostgreSQL EF Core API skeleton.
- Central business registry GET/POST, liveness/readiness, OpenAPI.
- Bootstrap platform key (minimum 32 UTF-8 bytes, constant-time comparison) and per-IP rate limiting for initial creator registry slice. This is temporary bootstrap auth, not production identity.
- Central tenant registry and durable provisioning queue SQL migrations; registration and job enqueue saved together.
- Business slug normalization/validation.
- Tenant provisioning runbook; worker and actual tenant database creation are not implemented.
- Dockerfile and local Compose foundation.
- Unit tests for key authentication, slug normalization and EF model mappings.
- Frontend/backend CI and dependency checks have been added. At one earlier PR snapshot, several CI jobs were reported green; always verify the exact current SHA before relying on this.

### Explicitly incomplete release blockers
- Creator OIDC/MFA/roles, business authentication/membership/invitations/RBAC.
- Tenant provisioning worker, leases/idempotency, secret manager, actual database create/migrate/health verification and tenant connection routing.
- Business APIs and server-authoritative POS, inventory, purchasing, accounting and CRM.
- Real data replacing localStorage demo data.
- Security and tenant-isolation integration/penetration tests.
- Audit/event pipeline, backup/restore automation and drills.
- Customer portal/storefront, domain lifecycle, online ordering, bookings, loyalty.
- Durable offline command outbox, sync, conflict resolution and device revocation.
- Production infrastructure, monitoring, alerting, incident/recovery runbooks and pilot acceptance.
- Hardware validation for receipt printers/payment terminals/barcode devices and any platform-specific integration.
- Final localization/accessibility/performance validation across supported devices.

## 8. Delivery order and priority

Do not build isolated UI screens ahead of their domain/security foundation. Work in vertical slices.

1. **Governance and reproducibility:** repository instructions, project ledger, CI stability, dependency lockfiles, branch/PR discipline.
2. **Identity/security foundation:** OIDC-compatible authentication, creator MFA/RBAC, tenant user identity, membership/invitations, business RBAC/branch scope, security/audit events.
3. **Tenant provisioning:** schema for leases/idempotency, safe multi-worker claim, secret manager interface, PostgreSQL database provisioning, migrations, health checks, retry/dead-letter and activation gates.
4. **Tenant request routing:** trusted server-side tenant resolver, connection lifecycle/pooling, authorization pipeline, isolation integration tests.
5. **Core business vertical slice:** business setup → staff access → catalog/customer → stock → POS checkout → payment → journal/audit/report, all server-authoritative.
6. **Accounting/inventory completeness:** journals, reports, purchases, returns, reconciliation and invariants.
7. **Industry packs:** cafe/restaurant and game cafe first due established requirements; then retail/services and extensible packs.
8. **Customer portal:** storefront, online ordering, booking/reservation, customer account/club/loyalty.
9. **Offline/PWA:** IndexedDB, device registration, durable outbox, sync replay, conflicts, revocation and offline acceptance.
10. **Operations/production:** deploy, TLS/domain, secrets, backups, restore, monitoring, security/load tests, pilot and acceptance.

At each step, choose the highest-priority unblocked engineering task. Do not skip security/data correctness to make a UI demo appear complete.

## 9. Definition of done for every change

A change is not done until all applicable items are satisfied:
- Requirements and affected module boundaries documented.
- Server-side validation and authorization implemented where applicable.
- Database migration, rollback/forward strategy and data integrity addressed.
- Error handling, idempotency, concurrency and retry behavior addressed.
- Persian/English localization, RTL/LTR and responsive UX updated.
- Tests added and run; test evidence tied to exact commit.
- Dependency/security/format/lint/build checks pass as applicable.
- Documentation, API contract, runbook and examples updated.
- No secrets or personal/production data committed.
- Known limitations and any manual/hardware steps recorded.

## 10. Release gates — all must pass before production-ready

### Product and functional
- All enabled modules have complete end-to-end workflows and acceptance tests.
- Creator and tenant user journeys are distinct and permission-correct.
- Real multi-user/multi-branch flows work across desktop, tablet and phone.
- Sales, payments, stock and accounting reconcile on seeded and adversarial scenarios.
- Cafe/game workflows recover correctly from disconnect, crash and restart.
- Customer storefront/order/booking are secure and business-configurable.

### Security and data
- OIDC/session/MFA and role/permission checks complete.
- Cross-tenant and cross-branch isolation tests pass, including malicious object-ID substitution.
- Creator support access controls and audit work.
- Secret rotation, CSRF/CORS, rate limits, abuse protection and dependency scans pass.
- No critical/high unresolved exploitable findings without formally accepted exception.
- Privacy, retention, export and deletion policies documented and tested.

### Quality and operations
- CI green on exact release SHA; backend/frontend unit + integration + E2E tests pass.
- Migration from clean install and supported previous versions verified.
- Backup and restore drills pass for central and tenant databases; RPO/RTO measured.
- Monitoring, alerts, logs, traces, incident response, rollback and runbooks ready.
- Load/performance tests meet published targets for agreed workload.
- Production deployment, TLS, domain, health checks and recovery verified externally.
- Accessibility, Persian RTL/English LTR, browser/device compatibility and operator acceptance pass.
- Hardware integrations tested on real target devices where claimed supported.
- Release notes, known limitations, support process and data export path delivered.

**No release gate can be waived by a chat message or by a green compile alone.** Record any accepted exception, owner, risk and expiry explicitly.

## 11. Session continuation protocol

Every future request such as “ادامه بده / continue” means:
1. Re-read `AGENTS.md` and this ledger.
2. Fetch live PR #105/successor, exact head SHA, changed files, comments/reviews and workflow runs.
3. Inspect failures and logs for that SHA; fix blockers before building new features when appropriate.
4. Identify the next highest-priority unblocked item in section 8.
5. Implement code and tests in the repository, commit to a new or appropriate feature branch, and open/update a Draft PR. Respect protection rules.
6. Update this ledger's implementation snapshot, completed work, verification evidence, blockers and next action.
7. Keep PR draft until all release gates pass. Do not merge, deploy, spend money, alter production data, or change external accounts without explicit authorization.
8. When blocked by unavailable credentials, external service, hardware or human decision, document the precise blocker and continue independent tasks.
9. At the end of a session, report only truthful status and the current evidence. Do not imply the agent continues working after the response.

### Progress accounting
Progress percentages must reflect the whole product's verified production-readiness, not the amount of code or documents. Architecture notes and UI prototypes contribute only a small amount. Increase progress only when substantial end-to-end functionality, tests and release gates are verified. Do not use 100% until production deployment, operational recovery and user acceptance are complete.


## 13. Latest continuation update — 2026-11-18

- Governance PR: [#152](https://github.com/Mahdi0383/MG-Business-Platform/pull/152), still Draft and not merged at the time of this update. It contains `AGENTS.md`, this ledger and README links; the durable rules become part of the implementation branch only after the PR is reviewed and merged.
- Creator OIDC hardening: [Draft PR #153](https://github.com/Mahdi0383/MG-Business-Platform/pull/153), branch `feature/mg-creator-oidc-authentication`, currently targets `feature/mg-business-web-foundation`. It removes the shared static bootstrap key and requires HTTPS OIDC issuer, API audience and explicit `mg_platform_role` claims for Creator registry endpoints; it adds configuration validation tests and updates the API contract/runbook.
- Exact PR #153 head at this ledger update: `c152c1dd2430a74d0df752ff0cb6a7a974c6e189`. Only the MG Business Web workflow had appeared and was pending at the time of observation. Do not report this change as CI-verified until all applicable jobs complete successfully on the latest head.
- This OIDC slice is not complete identity: MFA, creator identity lifecycle, fine-grained creator permissions, privileged access grants/audit, business user identity/membership and tenant authorization remain blockers.
- Next engineering priority after PR #153 validation/review: implement a durable tenant provisioning worker with lease/claim semantics, idempotency and bounded retry/dead-letter behavior; do not create tenant databases until a secret-manager adapter and least-privilege PostgreSQL provisioning credentials are designed and tested.
- Latest verified base snapshot before PR #153: PR #105 head `8c574cd1d625566785f0a5a7e8236ad339c399ff` had five associated workflows completed successfully (MG Web build, .NET formatting, dependency audit, CodeQL, and .NET CI/Windows Release). This evidence applies only to that exact SHA, not subsequent commits.


## 14. Latest engineering continuation — 2026-11-18

- Creator OIDC PR #153 head `c152c1dd2430a74d0df752ff0cb6a7a974c6e189` completed its MG Business Web workflow successfully. This confirms the branch's backend build/tests and frontend checks in that workflow only; it does not certify an external OIDC provider integration or production identity configuration.
- Provisioning lease/idempotency PR: [Draft PR #154](https://github.com/Mahdi0383/MG-Business-Platform/pull/154), branch `feature/mg-provisioning-job-leases`, based on PR #153. Head `714a87922f93cb030a7a603991c1c3299f384961` at this update. It adds migration 003, a unique stable idempotency key, lease owner/expiry metadata, EF mappings, model tests and runbook updates. Its CI was queued when recorded; verify exact latest head and workflow result before relying on it.
- Migration sequence for the central platform database is now 001 → 002 → 003. Migration 003 must be applied before any worker is enabled.
- No provisioning worker, tenant database creator, secret-manager adapter or tenant migration runner has been implemented yet. Do not activate tenants or claim automatic tenant provisioning works.
- Next task: validate PR #154 CI and schema migration against a real disposable PostgreSQL instance; then implement atomic queue claiming with `FOR UPDATE SKIP LOCKED`, lease ownership checks, renewal/recovery, bounded retry and tests. Before external database creation, implement a secret-manager abstraction and provisioner credentials with least privilege. Do not create real customer databases until those controls are in place.


### Provisioning lease implementation update — 2026-11-18

- Draft PR #154 now includes the first atomic queue lease service in addition to migration 003: PostgreSQL `FOR UPDATE SKIP LOCKED` claim, owner-checked lease renewal, owner-checked completion, and bounded retry/dead-letter handling with safe error-code validation.
- Completion updates the queue row and business activation in one transaction; activation is rejected and rolled back unless the business is still `PendingProvisioning`.
- Queue claim uses a direct Npgsql command because a data-modifying CTE with `UPDATE ... RETURNING` cannot safely be composed as an EF `FromSql` subquery.
- Exact CI head `a7fd64bbaafbb60f244102dec67b433a8b905411`: backend build and backend unit tests passed; frontend dependency install/audit/tests/build were still running at this update.
- Important remaining gates: PostgreSQL concurrency/integration tests, abandoned max-attempt lease reconciliation, hosted worker, secret-manager adapter, least-privilege provisioning credentials, tenant DB creator and migration runner. Do not expose provisioning as operational or activate a tenant until the external provisioner has verified DB connectivity, schema version, baseline and isolation.


- PR #154 follow-up: removed the obsolete Security__BootstrapPlatformKey Compose setting and replaced it with required MG_OIDC_AUTHORITY / MG_OIDC_AUDIENCE inputs; added .env.web.example with placeholders only and refreshed backend deployment/migration documentation. Never use the example issuer as a real identity provider. This change is included in the current PR #154 head and must pass its new CI run.

- PR #154 added a PostgreSQL-backed concurrency integration test using an isolated schema and two independent EF contexts; the GitHub Actions workflow now starts an ephemeral PostgreSQL 17 service and supplies MG_TEST_POSTGRES. The latest test/workflow/documentation head is 1b5e83f24e2e6813d2a58df8f3b0464d20073251; its exact-head workflow was queued at this ledger update. Do not treat earlier successful CI for a different SHA as validation of this latest change.
## 12. Change log

- 2026-11-18: Master requirements, architecture, continuation rules, implementation snapshot and production release gates consolidated into this repository governance document.\n- 2026-11-18: Recorded Draft PR #153 for Creator OIDC authentication hardening; its exact-head MG Web workflow subsequently passed.\n- 2026-11-18: Recorded Draft PR #154 adding durable provisioning lease and idempotency schema; exact-head CI was queued. Initial implementation status is based on PR #105 snapshot; revalidate before further work.
- Future sessions must append dated entries with PR/commit, concrete changes, tests/results, blockers and next task. Never erase historical verification; mark superseded evidence clearly.
