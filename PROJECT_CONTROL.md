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

### Provisioning lease hardening update — 2026-11-18

- PostgreSQL integration exposed an actual ambiguity in the claim statement's `RETURNING id` projection. It was corrected by qualifying every returned column with `job.`; the failed run is retained as evidence and is not represented as passing.
- Expanded real-PostgreSQL coverage to four independent scenarios: concurrent workers claim distinct jobs; expired leases are reclaimed and stale owners cannot renew; permanent failures move to dead-letter and release leases; successful completion activates only a pending business and is idempotently rejected on a repeat call.
- Added `DeadLetterExhaustedAsync` reconciliation for abandoned expired leases and pending/retry items at the attempt limit, plus a regression test. The future hosted worker must invoke this method periodically.
- Updated backend README and provisioning runbook to match implementation. No actual tenant DB creation, secret-manager integration, hosted worker, tenant API or production deployment has been implemented.
- Latest PR #154 head: `9e3ae213ed6d0d0a6549174362bab6c68ef67e8a`. Exact-head GitHub Actions run `44377815900` was in progress at the time of this update; earlier failed run `44377122537` failed on the now-fixed ambiguous SQL projection. Do not claim the latest tests pass until run 44377815900 (or a later exact-head run) completes successfully.


- Exact-head CI is now green for PR #154 head `9e3ae213ed6d0d0a6549174362bab6c68ef67e8a` (run `44377815900`): backend Release build, all backend tests including the PostgreSQL 17 integration suite, frontend dependency install/audit, frontend unit tests, production bundle build and artifact upload all succeeded. Pages deployment steps were correctly skipped for this PR. This does not validate later commits; rerun CI after any subsequent change.

### Tenant secret storage adapter — 2026-11-18

- Added `ITenantSecretStore` and an optional HashiCorp Vault KV v2 implementation to PR #154. The adapter requires an HTTPS origin, validates mount/path segments, sends the token only in the `X-Vault-Token` header, supports store/read/delete, and keeps secret values out of the central registry. The options object is not a record so its generated string representation cannot accidentally include the token.
- Added HTTP-stub tests for KV v2 writes, reads and path traversal rejection. Added optional `MG_VAULT_*` deployment variables and documented that the adapter has not been tested against a live Vault instance. A narrowly scoped Vault policy and real deployment credentials remain unconfigured.
- Current PR #154 head: `0ebd3e01e460f114067743b1b6472886a1c02221`; workflow run `44378484810` was still initializing at the time of this update. Previous exact-head run `44377815900` passed all backend and frontend checks before the Vault adapter was added; that result does not validate the new adapter.


### Opt-in provisioning worker skeleton — 2026-11-18

- Draft PR #154 now contains `ITenantProvisioningHandler`, `TenantProvisioningException` and `TenantProvisioningWorker`. The worker is registered only when `Provisioning:WorkerEnabled=true`; default is false. Startup fails closed unless a concrete handler is registered and a worker ID is configured. It resolves the handler before claiming a job, uses two-minute leases with 30-second renewals on an independent scope, invokes dead-letter reconciliation, and stops/does not complete work after lease loss. Provider exception messages are not logged.
- Added tests for safe provisioning error codes and Vault HTTPS-origin/token configuration validation. Worker remains disabled because no concrete tenant database provisioner is implemented or registered. No tenant DBs are created by this code.
- PR #154 current head: `ca3e37414885444b8d04e4825a7b4425556ad122`; exact-head workflow run `44379348342` was pending at ledger update. Earlier run `44377815900` passed before Vault and worker additions and is not evidence for this head. PR #105 remains open/Draft at `8c574cd1d625566785f0a5a7e8236ad339c399ff`; PR #154 remains open/Draft and mergeable.
- Next: validate exact-head CI; add worker orchestration tests with a deterministic fake handler/lease boundary; then implement a concrete PostgreSQL tenant provisioner with per-tenant least-privilege roles, Vault secret write/readback, idempotent database/schema creation, and migration verification. Keep worker disabled until that end-to-end path is tested against disposable infrastructure.


- Follow-up on PR #154: added the opt-in `TenantProvisioningWorker` skeleton, handler/error contracts, heartbeat failure handling, Vault/worker configuration tests, explicit disabled-by-default Compose environment, and corrected backend readiness/runbook language. Updated PR #154 description to reflect the real implementation boundary. Latest code head `c9481dc92aa8cefdf5b21620ffe4af8212f3383d`; exact-head workflow `44379601198` is in progress (PostgreSQL service initialization); no pass/fail result yet. Worker is not enabled because no concrete provisioner/migration runner is registered. PR #154 remains Draft.

### Tenant provisioning orchestration — 2026-11-18 (continued)

- Exact-head CI for prior worker/Vault slice `c9481dc92aa8cefdf5b21620ffe4af8212f3383d` completed successfully: backend Release build, backend tests, dependency audit, frontend unit tests and production bundle all passed; Pages deploy steps were correctly skipped for a PR.
- Added `ITenantDatabaseProvisioner` contract and `TenantProvisioningHandler`: immutable tenant UUID-derived database and Vault reference; durable idempotency key passed to provisioner; Vault secret reuse on retry; secret write/readback with fixed-time comparison; independent tenant/schema verification before the handler returns. Handler never activates a tenant; activation remains atomic in the lease service after successful handler completion.
- Business registry now uses `tenants/{tenant-uuid}/postgres`, avoiding slug changes/collisions in secret paths. Added PostgreSQL-backed handler tests for successful provisioning, retry secret reuse, wrong-database rejection and tenant lifecycle-state rejection. Existing lease tests updated to UUID references.
- Latest PR #154 head `40f4335c1334aabc6cc9b8b0a1383604b4679aa1`; exact-head workflow run `44380221187` is pending/in progress. No validation claim for this newest code until that run completes.
- Important blocker remains: no concrete `ITenantDatabaseProvisioner` implementation or tenant schema migration runner is registered. No direct privileged PostgreSQL administration credentials are placed in the API. Worker remains disabled. Next work is to implement/test an isolated provisioning agent or equally constrained provisioning adapter with idempotent database/user creation, tenant identity marker, migration version verification and Vault least-privilege policy; only then wire handler into DI and enable worker in a disposable environment.

- Follow-up hardening: added central migration `004_uuid_tenant_secret_references.sql` for pending tenants only; active secret references remain untouched and require a controlled migration. Updated manual migration order and PR #154 description. Latest exact PR #154 head is `ca788d574949e00898a45155f414a37cbff146ef`; workflow run `44380428096` is still in progress during checkout. Do not report the new orchestration slice as CI-verified until this run completes.

- CI failure was caught and corrected on PR #154: exact head `ca788d574949e00898a45155f414a37cbff146ef` failed backend compilation with two errors (tenant UUID referenced before local initialization in registry creation; nullable password dereference in connection-string validation). Fixed by generating the tenant UUID before constructing the registry entity and using a null-safe password check. Corrective head `6b111185d5702e0072ebad4951a2f739c6cfa6af`; exact-head workflow `44380656003` is pending/in progress. The failed run is not counted as passing validation.

- Validation checkpoint: exact PR #154 head `19288bf109783f3c11ebb648b71c8cfb0a32c952` completed CI successfully (backend Release build, backend tests including PostgreSQL integration, frontend dependency audit, frontend unit tests and production bundle; Pages deploy skipped as expected for a PR). The run covered the UUID fixture fix and Vault not-found/delete tests.
- Added a Vault KV v2 create-only write (`options.cas=0`) to prevent a stale/concurrent provisioning attempt from overwriting an already-created tenant secret, plus an assertion for CAS behavior. Latest code head `81d8ef13f1035785881604c1f2413847c9af45b2`; a fresh exact-head CI run has not appeared yet. Do not extend the previous green result to this later change.
- Provisioner implementation remains intentionally absent; only the interface/orchestration is present. Worker stays disabled until isolated provisioning-agent + migrations are implemented and verified.

- Additional Vault concurrency hardening: `VaultKv2TenantSecretStore.StoreAsync` now sends KV v2 `options.cas=0`, making first-write create-only and preventing a stale worker from overwriting a secret created after its initial read. Tests assert CAS=0, explicit 404=>null reads, and metadata-endpoint deletion behavior. Runbook explains retry behavior on CAS conflict. Current PR #154 head is `5dd104879d78c862b8d7548d705cc48c018a427d`; no workflow run/check suite has yet been created for this head. The preceding orchestration+Vault tests at `19288bf109783f3c11ebb648b71c8cfb0a32c952` passed CI; that result does not validate later CAS changes.

- Further security hardening on PR #154: tenant PostgreSQL connection strings now reject encryption-only `SslMode=Require` and require certificate validation (`VerifyCA` or `VerifyFull`); tests explicitly reject the weaker mode. Runbook requires hostname/CA validation and prohibits trusting arbitrary server certificates. PR description updated with accurate validation boundary. Latest head `d1b3ed7d473d936cce549f125b162fc6e82ea10c`; no exact-head workflow run/check suite has appeared. Last green CI remains `19288bf109783f3c11ebb648b71c8cfb0a32c952`, before CAS and TLS hardening. Keep PR Draft and worker disabled.

- CI update: exact PR #154 head `d1b3ed7d473d936cce549f125b162fc6e82ea10c` completed workflow run `44382514630` successfully (backend build/tests, frontend audit/tests/production bundle; deploy skipped for PR). This validates the Vault CAS and PostgreSQL TLS certificate-verification hardening.
- Added `backend/tenant-database/migrations/001_tenant_core_baseline.sql`: isolated `mg` schema, immutable tenant identity marker, checksum ledger, settings, external-identity users (no local password storage), roles/permissions, customers, audit events and outbox. Migration runner contract requires transaction-local tenant UUID and exact artifact SHA-256, and a separate non-owner runtime role without DDL privileges.
- Added `TenantDatabaseBaselineMigrationTests`: creates a disposable PostgreSQL database, applies the migration, verifies the exact tenant UUID and checksum, and asserts the tenant identity trigger rejects mutation. Latest head `9fe072c522b4f3774f3aa0fd7ce20153e3ef0116`; no exact-head CI run/check suite has appeared yet. Do not extend the previous green result to the new migration/test.
- PR #154 remains Draft. Concrete isolated provisioner/agent, runtime-role grants, migration runner registration and end-to-end provisioning are still outstanding; worker remains disabled.

- Added dedicated workflow `.github/workflows/tenant-provisioning-tests.yml` to gate PostgreSQL-backed provisioning and tenant-schema tests independently of the full web bundle. Run `44383591699` is executing on workflow commit `831eda1ff92f63d46697098603d355feeefa1d4c`; the full web workflow `44383509631` is executing on schema/test commit `9fe072c522b4f3774f3aa0fd7ce20153e3ef0116`. The full workflow has already passed backend Release build and backend tests for `9fe072c`; frontend dependency install/build checks are still running. Do not call the current head fully green until both runs complete.

- Validation completed successfully: full web workflow `44383509631` passed on code/test SHA `9fe072c522b4f3774f3aa0fd7ce20153e3ef0116` (backend Release build and tests, frontend dependency audit/tests and production bundle). Dedicated tenant provisioning workflow `44383591699` passed on workflow SHA `831eda1ff92f63d46697098603d355feeefa1d4c`; it ran the PostgreSQL-backed provisioning suite including creation of a disposable tenant database, baseline migration application, exact checksum verification and immutable tenant identity rejection. Latest PR #154 head is `831eda1ff92f63d46697098603d355feeefa1d4c`; its only delta over the full web-tested code SHA is the dedicated CI workflow file. Both workflows are green for the application/test content and dedicated migration test.
- PR #154 description now records the exact workflow IDs/SHAs and the remaining production boundary. It remains Draft; no concrete isolated provisioner/agent or production migration runner is registered, so no real tenant provisioning is available and the worker remains disabled.

- Security review found that Npgsql `TrustServerCertificate=true` can bypass TLS certificate validation even when an accepted SSL mode is configured. `TenantProvisioningHandler` now explicitly rejects that option, with a regression test. Runbook already prohibits it. Validation runs are active for code commit `95455ca38601d13c07faaf64df43edd01d4a5e84` and test commit `107f2415a299d12e184ecf38ca819354036285c3`: targeted PostgreSQL workflows `44384075965` / `44384086558` and full web workflow `44384075976`. Await exact-head results before calling this hardening validated.

- PostgreSQL TLS hardening follow-up: Npgsql 10 marks `TrustServerCertificate` as obsolete and explicitly says it has no effect. The attempted property access caused a warnings-as-errors CI failure; it and the ineffective test were removed. Strict `SslMode=VerifyCA/VerifyFull` validation remains. The runbook now accurately describes the deprecated parameter as a no-op rather than a bypass.
- Final validation passed on exact application/test SHA `d1fef285609cf022ece5bb430c7d8ad2a86aae4a`: full web workflow `44384473246` (backend Release build/tests, frontend audit/tests/bundle) and dedicated PostgreSQL workflow `44384473274` (tenant schema migration integration test). Current branch head `e52d333ac94a7de18450cd6d7565046618acb3d1` includes only the runbook clarification after that tested SHA; no application or test code changed after the green runs. PR #154 remains Draft; isolated provisioning agent and production migration runner remain outstanding, worker disabled.

- Added the isolated-agent HTTP adapter to the API: it sends only tenant UUID, durable idempotency key and deterministic database name over HTTPS with a mounted PKCS#12 mTLS client certificate; no cluster-administration credential enters the API. Agent status codes map to safe retryable/permanent errors. The API independently verifies tenant marker/schema baseline and rejects runtime roles with superuser, CREATEDB, CREATEROLE, REPLICATION, BYPASSRLS, database/schema CREATE, or database ownership.
- Agent endpoint options fail closed unless HTTPS address + valid client certificate/private key are supplied. Docker/env examples now expose the optional settings. Added tests for request contract, deterministic database naming, safe status mapping, and valid/invalid mTLS options. Detailed acceptance contract added at `backend/docs/PROVISIONING_AGENT_CONTRACT.md` and linked from runbook.
- Dedicated PostgreSQL workflow runs `44386056696` and `44386045476` passed on code/test SHA `21c7f2f07402752c1606fd1cd23f69be938bc06d`, including API build and 53 tests. The full web workflow on that SHA passed backend build/tests, dependency audit and frontend tests but was cancelled during production bundle after a subsequent documentation-only commit; the earlier full web workflow `44384473246` passed production bundle on prior code SHA `d1fef285609cf022ece5bb430c7d8ad2a86aae4a`. Re-run full web workflow for final release confidence.
- Current agent service itself is still not implemented/deployed. PR #154 remains Draft and worker remains disabled; do not mark tenant provisioning production-ready.

- Added a PostgreSQL-backed runtime-credential verification test. It applies the tenant baseline to a disposable DB, creates a non-owner app role with only CONNECT/USAGE/SELECT, verifies that role succeeds for the correct tenant and fails for the wrong tenant, then proves verification rejects CREATEDB and database ownership. Dedicated workflow `44387171140` passed on exact test SHA `817f7ce5220ea24f3f6d7ee34c3e77fd0271f696`.
- Full web workflow `44386337326` and dedicated provisioning workflow `44386337234` passed on SHA `b9fa09675edf980f67c7fbaf23e677a38905485f`; full workflow included production bundle. Later changes are test-only and docs-only; no application source changed after that full-web-tested SHA.
- PR #154 body updated with the mTLS adapter, no-redirect client handler, agent contract and exact validation SHAs. PR remains Draft; isolated agent service and production activation remain incomplete.

- Latest exact PR #154 head `ec8eeed851e4b2e976791590492dd1b596ab0410` is green in both workflows: full web run `44388047919` passed backend Release build/tests, frontend dependency audit/tests and production bundle; dedicated PostgreSQL run `44388047969` passed tenant migration, immutable marker, runtime-role privilege verification, mTLS adapter and configuration tests.
- mTLS client certificate validation now requires a valid private key, non-CA Basic Constraints, Client Authentication EKU and Digital Signature key usage. Regression tests reject server-auth-only and CA certificates. API's mTLS handler disables redirects and cookies.
- PR #154 is still Draft and mergeable at this checkpoint. Remaining critical blocker is the separately deployed isolated provisioning agent implementation (mTLS server identity allowlist, dedicated restricted DB control role, durable idempotency/reconciliation, tenant role creation, migration application and audit). Do not enable worker or claim production provisioning until those end-to-end gates pass.

- Tenant baseline hardening: migration 001 now enforces append-only behavior for tenant identity, migration ledger and audit events through database triggers. The PostgreSQL integration test applies the migration, verifies exact tenant UUID/checksum, and confirms UPDATE attempts against all three immutable records are rejected. Exact current PR #154 head `b21b70e34f8888b84b82c5ec0fd136a36648a123` is green in both workflows: full web `44388998050` (backend Release build/tests, frontend audit/tests/production bundle) and dedicated PostgreSQL `44388997945` (provisioning/migration/runtime-role tests).
- PR #154 remains Draft; the API mTLS adapter is implemented, but the separate agent service and its cluster-role orchestration are not. Worker remains disabled; no production tenant database has been provisioned.

- Strengthened `HttpTenantDatabaseProvisioner.VerifyAsync`: the runtime credential must have no UPDATE or DELETE privileges on `mg.tenant_identity`, `mg.schema_migrations` or `mg.audit_events`, in addition to being a non-owner role without superuser/CREATEDB/CREATEROLE/REPLICATION/BYPASSRLS/database CREATE/schema CREATE. PostgreSQL integration test grants audit UPDATE and DELETE in turn and confirms verification fails; after revoking, verification succeeds. Dedicated workflow `44389935004` passed on exact test SHA `52abaa9e3bb4050e612d09690c3d39f4d8f8a9f6`.
- Full web workflow `44389671045` and dedicated provisioning workflow `44389671096` passed on code SHA `0767bbc9f498948a458e14399f46a947b6f27434`; latest subsequent change is test-only. PR #154 remains Draft; isolated agent deployment remains the production blocker.

- Runtime tenant credential verification was hardened further: the app role must have schema USAGE but no schema CREATE, must not own its database, must be LOGIN, must have no inherited PostgreSQL role memberships, and must have no INSERT/UPDATE/DELETE/TRUNCATE rights on tenant identity, migration ledger or audit events. PostgreSQL integration tests explicitly grant each prohibited capability and assert verification fails, then revoke and assert recovery. Dedicated workflow `44392739002` passed on exact test SHA `5bd4587433388c3b39ad04109acd40df5147cbdc`.
- Full web workflow `44392750302` passed on the same SHA: backend Release build/tests, dependency audit, frontend tests and production bundle. GitHub Pages deploy was skipped as expected for a PR. Current PR #154 head is `54856bd4f44eafbad4effaf0e463919d57cb9f84`; only the provisioning-agent contract documentation changed after the fully tested code SHA.
- PR #154 description now records the latest exact CI runs and security gates. It remains Draft and mergeable; the isolated provisioning agent service is not yet implemented/deployed, so the worker remains disabled and production provisioning is not ready.

- Extended tenant runtime-role verifier to reject REFERENCES and TRIGGER privileges on immutable tenant identity, migration ledger and audit tables. PostgreSQL test grants TRIGGER on audit events and confirms verifier rejects it; after revoke, verifier accepts the role again. Dedicated PostgreSQL workflow `44393410005` passed on code/test SHA `7d0a88bfa2d57ee43186c5815c1c9b11882cfc82`.
- Current PR #154 HEAD `20b5203bf10d0aa236adbd38f91fe8a0239dbd28` has both workflows green: full web workflow `44393468411` (attempt 2) passed backend Release build/tests, dependency audit, frontend tests and production bundle; dedicated PostgreSQL workflow `44393468334` passed provisioning/tenant schema/runtime privilege integration tests. Pages deploy steps were skipped for the PR as expected.
- PR #154 body refreshed with exact current SHA and validation results. Still Draft: no isolated agent implementation/deployment, real PostgreSQL control role, live Vault policy, crash-recovery/e2e provisioning or operational backup/restore validation. Worker remains disabled.

- Identified and closed a queue correctness gap: owner-ID-only leases did not fence a stale attempt if the same worker ID was reused after lease expiry. Added unique database-generated `lease_token` per claim, mapped it in EF, and require the exact token for renew/success/failure. Migration 005 backfills tokens for live leases, requeues legacy `Running` rows with no lease, and enforces consistency between Running status, owner, expiry and token. Added PostgreSQL tests proving a stale same-ID attempt cannot renew, fail or activate a job after reclaim, and an upgrade test for migration 005.
- Exact validation on current PR #154 HEAD `a8476f1b7437d58ea1555949b2e9c8e4e42df30e`: full web workflow `44394925978` passed backend Release build/tests, dependency audit, frontend tests and production bundle; dedicated PostgreSQL workflow `44394925819` passed lease-fencing, migration-upgrade, provisioning and tenant runtime privilege tests. Pages deployment steps were skipped as expected for a PR.
- PR #154 description now includes migration 005 and exact CI. It remains Draft; isolated agent service, restricted DB control role, live Vault policy, end-to-end idempotency/crash recovery and operational backup/restore are still release blockers. Worker remains disabled.

- Queue lease correctness hardened with per-claim UUID fencing: every claim generates a new token; heartbeat, failure and completion require worker ID + exact token + unexpired lease. This prevents stale attempts from mutating a reclaimed job even if the worker ID is reused. Migration 005 backfills tokens for existing leased jobs, requeues legacy Running rows with no lease and enforces Running/lease-state consistency. Added PostgreSQL integration coverage for stale same-ID worker rejection and migration upgrade behavior.
- Current PR #154 HEAD `a8476f1b7437d58ea1555949b2e9c8e4e42df30e`: full web workflow `44394925978` passed backend Release build/tests, dependency audit, frontend tests and production bundle; dedicated PostgreSQL workflow `44394925819` passed migration 005, fencing, provisioning and tenant privilege integration tests. Pages deployment was skipped for PR as expected.
- PR #154 remains Draft and worker disabled pending isolated agent implementation/deployment, restricted PostgreSQL control role, live Vault policy, end-to-end idempotency/crash recovery and operational backup/restore validation.

- Extended migration 005 PostgreSQL integration coverage: existing leased Running rows receive fencing tokens; lease-less legacy Running rows move to Retry with `LEASE_MISSING_ON_UPGRADE`; Pending rows remain lease-free; the new constraints reject Running-without-lease and non-Running-with-lease. Dedicated PostgreSQL workflow `44395531042` passed on current PR #154 head `1df72ef7ff4caa9ab7794550aecc9b4c597f1cdb`.
- Full web workflow `44394925978` passed on production-code SHA `a8476f1b7437d58ea1555949b2e9c8e4e42df30e` (backend Release build/tests, dependency audit, frontend tests and production bundle). The only subsequent feature-branch change was additional migration assertions in tests; dedicated PostgreSQL CI passed on the latest head.
- PR #154 description records the fencing migration and exact CI. It remains Draft; isolated agent service/deployment, restricted PostgreSQL control role, live Vault policy, end-to-end crash recovery and operational backup/restore are still open gates. Worker remains disabled.

- Moved all lease expiry decisions and retry/activation timestamps to PostgreSQL `now()` rather than worker-host clocks: renewal, dead-letter reconciliation, retry backoff and business activation now use database time. This prevents skewed worker clocks from extending or prematurely expiring leases. Full web workflow `44396020460` and dedicated PostgreSQL workflow `44396020096` both passed on exact feature HEAD `26ed1b3c1c1305d7a1737de728fef14af28e3808`.
- PR #154 description refreshed with fencing, migration 005, DB-clock semantics and exact CI. It remains Draft; isolated provisioning agent, restricted PostgreSQL control role, live Vault policy, end-to-end recovery and operational backup/restore are still open release gates.

- Hardened provisioning agent client configuration: endpoint must be a clean HTTPS origin without userinfo, path, query or fragment. Added tests for HTTP, credentials, path, query and fragment rejection. Dedicated PostgreSQL workflow `44396929611` and full web workflow `44396929385` both passed on current PR #154 HEAD `66664decb72bce7a1ee6a10b6584988b7220937e`; full web includes backend Release build/tests, dependency audit, frontend tests and production bundle.
- Added an explicit durable agent credential replay contract: per-tenant advisory locking, unique idempotency ledger, AEAD-encrypted connection-string persistence before any database side effects, versioned key rotation, durable phase reconciliation and same-credential replay after a lost API response. The agent service itself remains unimplemented; no secret or database control credential was added to the API.
- PR #154 body refreshed with exact CI and current production boundary. Remains Draft; worker disabled pending agent service, restricted DB role, live Vault policy and end-to-end recovery/restore tests.

- Hardened OIDC issuer/audience configuration in PR #153: issuer must be HTTPS and cannot contain userinfo, query or fragment; audience is trimmed then rejected if empty, over 200 characters or contains embedded whitespace/control characters. Added negative tests while preserving normalized valid audience behavior. Full web workflow `44397691956` passed on exact PR #153 HEAD `3797903ef303d47f83dd3594d5c84172afa1e83e` (backend Release build/tests, dependency audit, frontend tests and production bundle).
- PR #153 remains Draft pending a real OIDC provider configuration, MFA/lifecycle/granular creator authorization and end-to-end token validation. PR #154 is stacked on PR #153 and remains Draft; re-check mergeability after base branch synchronization.

- Added standalone `Mg.Provisioning.Agent.Core` with an AES-256-GCM tenant credential protector. It binds tenant UUID, durable idempotency key, deterministic DB name and key ID as authenticated associated data; generates random nonce/tag; supports versioned key rings for rotation; rejects tampered/context-substituted envelopes; and zeroes plaintext and key buffers. Added tests for round-trip, tenant/idempotency/database binding, tampering, key rotation and invalid key material. This core is not yet wired into a deployed agent service or persistent operation ledger.
- Full web workflow `44398281036` and dedicated PostgreSQL workflow `44398280937` passed on feature SHA `4c272743d2ae0a64b073e427c91b7baec2076cd8`; full workflow included backend Release build/tests, dependency audit, frontend tests and production bundle.
- PR #154 updated with the encryption core and remaining production boundary; remains Draft. Agent API, mTLS server identity allowlist, durable ledger, restricted DB provisioning role, migrations runner, Vault live policy and end-to-end recovery are still open.

- Implemented the first durable isolated-agent operation ledger in `backend/src/Mg.Provisioning.Agent.Core`: transaction-scoped PostgreSQL advisory lock per tenant; unique tenant/idempotency/database constraints; AES-256-GCM encrypted credential persistence before provisioning side effects; same-credential replay after retries/lost responses; conflict rejection for tenant/key reuse; and strict one-step phase progression (`Prepared → DatabaseCreated → SchemaApplied → Verified`).
- Agent credential persistence now validates exact UUID-derived database identity, nonempty host/user/password and PostgreSQL `VerifyCA`/`VerifyFull` before storing. The checked-in isolated agent database migration is executed by PostgreSQL integration tests. Tests cover concurrent duplicate requests (single credential generation), replay, idempotency conflicts, failed preparation retry, invalid DB/TLS rejection, phase monotonicity, AAD/tamper protection and key rotation.
- Exact current PR #154 head `850f6f0cfd935cb73f3ca2a33cd5af9244694be0`: web workflow `44401550987` passed (backend Release build/tests, frontend audit/tests, production bundle); dedicated PostgreSQL workflow `44401551012` passed all 82 tests with 0 failures. Pages deployment steps skipped as expected on PR.
- PR #154 remains Draft and worker disabled. The agent service and its mTLS identity policy, restricted PostgreSQL control role, real database/role provisioner, migration runner, durable reconciliation after external side effects, live secret-manager key delivery/rotation and backup/restore acceptance are still unimplemented.

- Added `AgentProvisioningCoordinator` and `ITenantResourceProvisioner` contract to the isolated agent core. The coordinator holds a PostgreSQL session advisory lock for the full tenant operation, serializes requests per tenant, executes idempotent resource/role creation → schema application → independent runtime verification, and advances durable phases only after each stage succeeds. A restart resumes from the last committed phase; a side effect completed just before a crash is safely reconciled by repeating the idempotent stage. Concurrent same-tenant requests fail fast as busy.
- Store now takes transaction-scoped advisory locks on both tenant UUID and idempotency UUID, preventing cross-tenant idempotency-key races before unique insert. Added PostgreSQL tests for coordinator completion/replay, crash recovery after migration interruption, and concurrent request exclusion.
- Exact current PR #154 head `a17a1d9d81ca9100b95e301943e1099b0555b6c6`: full web workflow `44402340616` passed (backend Release build/tests, dependency audit, frontend tests, production bundle); dedicated PostgreSQL workflow `44402327834` passed 80 tests, 0 failures. Pages deployment skipped for PR.
- The concrete resource provisioner, schema migration runner, mTLS HTTP service, production agent control role and crash-injection tests against real PostgreSQL side effects are still open. Keep API worker disabled; PR #154 remains Draft.

- Added `TenantSchemaMigrationArtifact`: it loads only the fixed packaged tenant core baseline SQL, validates UTF-8, size and required immutable identity/migration-ledger statements, calculates SHA-256 from the exact artifact bytes and returns defensive copies. The coordinator and provisioner now receive the artifact itself; no request/config-supplied checksum can substitute different SQL.
- Exact PR #154 head `0b05091146843940b998f6693eb4fd7c43e9a6fa`: dedicated PostgreSQL workflow `44402819240` passed; full web workflow `44402819146` passed (backend Release build/tests, frontend dependency audit/tests, production bundle). Pages deployment skipped on PR. Both runs validate the exact same SHA.
- Concrete PostgreSQL role/database provisioner and migration executor remain unimplemented; mTLS server and deployment/key management also remain open. PR #154 remains Draft and API worker remains disabled.

- Created separate `backend/src/Mg.Provisioning.Agent.Api` HTTPS host. It requires a valid server PKCS#12 certificate, explicit SHA-1 thumbprint allowlist, client-certificate chain/revocation validation, client-auth EKU, digital-signature usage, non-CA status and validity window. Request surface is limited to liveness/readiness and the internal provisioning endpoint; POST enforces strict JSON fields, 4 KiB body cap, UUID-derived DB name, per-client rate limit and safe error mapping. No handler is registered, so readiness and provisioning return 503 by design; this is a secure boundary skeleton, not a working provisioner.
- Added API host options tests; all 97 tests pass. Exact PR #154 HEAD `ce5a873819311b18c9eafe189c231cff13c69de7`: dedicated PostgreSQL workflow `44403863745` success; full web workflow `44403860535` success including backend Release build, isolated agent API build, backend tests, frontend audit/tests and production bundle. Pages deployment skipped for PR.
- Remaining production blockers: concrete restricted PostgreSQL role/database provisioner, idempotent migration executor using the packaged artifact, live secret-manager key delivery, agent handler registration, mTLS deployment trust/revocation infrastructure, crash/recovery and backup/restore drills. Keep worker disabled and PR #154 Draft.

- Implemented `NpgsqlTenantResourceProvisioner` in the isolated agent core. It requires strict-TLS control credentials and a non-superuser CREATEDB/CREATEROLE control role; creates deterministic UUID-derived NOLOGIN owner and restricted runtime roles; adds immutable tenant markers to roles and database; transactionally creates role markers/membership; revokes public DB access; and grants CONNECT only to control/runtime identities.
- Added crash-safe database creation marker recovery: a database left unmarked after CREATE DATABASE is adoptable only if its owner role matches and it is provably empty. Existing non-empty unmarked or mismarked DBs fail closed. The migration runner uses the exact packaged artifact checksum, applies baseline as owner, verifies any existing ledger before retry and grants explicit runtime rights. Runtime verification checks exact tenant/checksum, db/schema owner, role flags/membership, no immutable-table mutation rights, no app-owned tables/functions and customer CRUD.
- Added strict constructor/input tests for the PostgreSQL provisioner and deterministic app role validation in both agent and API orchestration. Exact PR #154 HEAD `888e7d8ba6109ad049318db861dc96960b274cae`: dedicated PostgreSQL workflow `44406335703` passed 106 tests with 0 failures; full web workflow `44406335904` passed backend Release build/tests, agent API build, dependency audit, frontend tests and production bundle.
- Remaining blocker: Npgsql provisioner is not wired into an `IAgentProvisioningHandler`; no live TLS-enabled PostgreSQL integration suite/crash injection or live secret-manager key delivery/rotation. Agent API remains not-ready/503 by design; central worker stays disabled and PR #154 Draft.

## 11.1. Latest provisioning implementation snapshot — 2026-11-19

The historical entries below remain as an audit trail; statements that the agent handler or concrete provisioner are absent are superseded by this snapshot.

- The isolated `backend/src/Mg.Provisioning.Agent.Api` HTTPS host now conditionally registers `NpgsqlAgentProvisioningHandler` when its strict-TLS control connection, packaged tenant baseline and complete AES-256 key ring are configured. The service requires client certificates, validates chain/revocation, EKU, key usage, validity and explicit thumbprint allowlist; TLS is restricted to 1.2/1.3. Readiness checks the live control DB and ledger grants; credential responses are marked `no-store`.
- `NpgsqlTenantResourceProvisioner` now creates deterministic UUID-derived tenant databases, owner and application roles, immutable role/database markers, the packaged baseline migration and explicit runtime grants. It independently verifies tenant UUID, migration version/checksum, database/schema ownership, role attributes/membership options, immutable table protections and customer CRUD.
- The agent generates distinct random owner/application passwords. Both connection strings are persisted only as one authenticated AES-GCM encrypted credential bundle in the agent operation ledger. Only the application connection string is returned over mTLS to the Platform API; the owner credential remains inside the isolated agent for database marking, migrations and grants.
- PostgreSQL 17 automatically creates an immutable bootstrap ADMIN membership for a CREATEROLE identity on roles it creates (INHERIT FALSE, SET FALSE). The agent accounts for this platform behavior and adds a separate grantor-scoped owner-role membership with ADMIN FALSE, INHERIT FALSE, SET TRUE. The runtime application role receives no SET or INHERIT path. The CREATEROLE control identity remains highly privileged and must be isolated to the agent, never placed in the Platform API.
- Dedicated TLS-enabled PostgreSQL workflow `44415013454` passed on exact PR #154 feature HEAD `61a3ad1c39f979fec6f7efc91a106d5a4f613b1c`: 116 tests passed, 0 failed, 0 skipped. Full web workflow `44415013446` passed on the same exact SHA: backend Release build/tests, isolated agent API build, frontend dependency audit/tests and production bundle all succeeded; Pages deployment was skipped as expected for a pull request.
- Remaining release gates: live production secret-manager delivery and key rotation, crash-injection at each DDL/ledger/Vault boundary, full backup/restore and tenant-isolation acceptance, deployed mTLS certificate/revocation operations, observability/alerting and end-to-end staging. The central provisioning worker remains disabled; PR #154 remains Draft and must not be merged or deployed before these gates pass.

## 11.2 Latest engineering continuation — 2026-11-21

### Live repository state

- Default branch `main` is at `88ea40799eda5c457c6c742eff26f2e8868f7f8a`. The repository currently has 13 open pull requests; no PR was merged during this continuation. Re-fetch all refs before any future synchronization because these are live snapshots.
- PR #159, `ci/quality-gates-for-stacked-prs-20261121`, remains Draft on exact head `495d400b0ae0572032059f663191d65e797e43f2`. All four newly enabled pull-request quality workflows completed successfully on that exact SHA: .NET CI/Windows Release run `44882937558`, CodeQL `44882937661`, formatting `44882937577`, and dependency audit `44882937631`. The change removes only the pull-request target-branch filters; push and scheduled triggers remain unchanged. It has not been merged, so other PRs do not inherit these trigger changes yet.
- PR #155, `security/harden-agent-control-role-readiness`, remains Draft at `6282c3795b48a6d478457698cb8dbff395782d61`. Exact-head PostgreSQL provisioning integration and web/backend CI runs `44920148658` and `44920148668` both succeeded: 136 PostgreSQL tests, 136 backend tests, 20 frontend tests, backend Release and isolated agent API builds, dependency audit and production bundle. Pages deployment was skipped for the PR. Provisioning worker remains disabled.
- PR #156, `security/scope-release-publish-permissions`, remains Draft at exact head `84a729f0592797347f2a5533dd926aa62524c3df`. It now has a strict reusable post-transfer artifact verifier, negative fixture tests (tampering, empty/truncated/multi-line/path-traversal manifests, missing artifacts and unexpected manifests), and a PR-only synthetic cross-job upload/download contract test. Release workflow run `44921237408` passed both fixture and cross-job transfer verification; build/publish were correctly skipped for the PR. Exact-head .NET CI run `44921237401` passed 300/300 tests, Release build with 0 warnings/0 errors, Windows package/installer creation and package verification. The log confirms pinned Caddy v2.11.9 was downloaded and SHA-256 verified. Formatting run `44921237441` and NuGet audit run `44921237517` passed. CodeQL run `44921237437` also passed. All five applicable workflows are now green on the exact head `84a729f0592797347f2a5533dd926aa62524c3df`; actual main-branch release publication remains unexecuted and gated.
- PR #156 exposed a separate packaging reliability failure: the Caddy interactive download service returned HTTP 401 in run `44920891729`. The packaging script now uses the official Caddy 2.11.9 GitHub release asset, pins its SHA-256 (`ad93112d335ee687100c4c9f11bced16ca590377976b7fe7883882b9e797e472`), verifies it before extraction and records the pinned version. The exact current head must prove this on Windows CI.
- PR #158, `security/close-agent-control-readiness-tocu`, remains Draft at `702864c3ef6c498179d9da216040dbb7f8773aec`. It contains the reusable live readiness verifier and TOCTOU regression tests, but its branch is materially stale relative to PR #155: comparison of #155 head `6282c379...` to #158 head `702864c...` reports 79 commits behind and 8 unique commits ahead. The 79-commit parent delta changes 10 files; the 8-commit child delta changes six. Five overlap and need manual semantic reconciliation: `NpgsqlAgentProvisioningHandler.cs`, `AgentProvisioningOperationStore.cs`, `NpgsqlTenantResourceProvisioner.cs`, `AgentProvisioningOperationStorePostgresTests.cs`, and `NpgsqlTenantResourceProvisionerPostgresTests.cs`. Preserve later parent ACL/maintenance-lock protections when integrating the centralized verifier. Its earlier 116-test result is not full-stack validation.
- PR #155 itself is also not synchronized with its current PR #154 base. Comparing #155 head `6282c379...` with PR #154 base head `0977e9f5...` shows 8 base-only commits absent from #155 (and 174 commits unique to the #155 branch). Those 8 commits touch five files; three overlap with #155 changes and require semantic reconciliation: `PROVISIONING_AGENT_OPERATIONS_ACCEPTANCE.md`, `Mg.Platform.Api/Program.cs`, and `AgentProvisioningOperationStorePostgresTests.cs`. The parent-only project-file and lease-test changes must also be carried forward. Do not merge either security PR or rewrite/force-push these branches. The next stack task is to produce a reviewed, non-destructive synchronization plan and validate a candidate that contains both the current parent commits and the security hardening.
- Issue #157 remains open. Its remaining acceptance evidence includes real process-kill crash injection at external DDL/ledger/secret side-effect boundaries, recovery verification, production secret-manager/key-rotation and backup/restore rehearsal, deployed mTLS issuance/revocation/rotation, monitoring/alerting and privileged-administrator operational controls. Keep `Provisioning:WorkerEnabled=false`.

### Current release and progress boundary

- No production deployment or release publication was performed. PRs #152–#159 remain open/Draft where noted; no unverified changes were merged.
- The Windows release pipeline's artifact-transfer contract is now testable on PRs, but the actual publish job remains unexecuted because it is intentionally gated to an approved `main` release or manual dispatch on `main`. Do not describe release publication as end-to-end verified.
- Whole-product progress remains **35%**. CI hardening and provisioning security work do not make the POS, accounting, cafe/game, tenant-facing workflows, operational deployment or hardware acceptance complete.

### Next task

1. Finish exact-head CI for PR #156 and correct any new failure without treating earlier SHA results as current.
2. Resolve the PR #154 → #155 → #158 branch divergence with a non-destructive integration candidate; preserve all commits, inspect conflicts and rerun the complete quality gates. Do not merge or force-push.
3. Continue the remaining Issue #157 crash/recovery and production operational acceptance work. Keep the provisioning worker disabled until all production gates pass.

## 11.3 Provisioning stack integration candidate — 2026-11-21

- Created Draft PR #160: integration/provisioning-agent-security-stack-20261121 → feature/mg-provisioning-job-leases.
- Candidate merge commit: d0b962a46378fd152d0181e1fb3af7c37ff108d5, with two parents:
  - current lease parent 0977e9f5ba03ebbd9b07b24e77fd54dc43efe71c;
  - security hardening PR #155 head 6282c3795b48a6d478457698cb8dbff395782d61.
- This is a new integration candidate; no existing branch was force-pushed, rebased, or rewritten. Original PR #155 and #154 remain open/Draft.
- Three overlapping files were reconciled against merge base 519100d1df6b7ec2c668335eabacf72e73997d51 with no textual conflicts:
  - operations acceptance checklist: preserved the parent worker-heartbeat/fault-injection gates and the security branch's stricter control-role/PUBLIC ACL requirements;
  - Platform API Program.cs: preserved the parent's shared Creator OIDC registration and the security branch's certificate revocation check;
  - AgentProvisioningOperationStorePostgresTests.cs: preserved both parent cancellation recovery and security control-plane maintenance-lock coverage.
- The two parent-only changes to the test project dependency and lease tests remain inherited from the first parent. The 21 expected security/provisioning files appear in the candidate diff.
- Exact-head CI on merge commit d0b962a46378fd152d0181e1fb3af7c37ff108d5:
  - PostgreSQL 17 integration run 44922593856: 136 passed, 0 failed, 0 skipped.
  - Web/backend run 44922593816: backend Release build passed; isolated agent API build 0 warnings / 0 errors; backend tests 136 passed; frontend tests 20 passed; dependency audit passed; production bundle succeeded; Pages deployment skipped as expected for a PR.
- Formatting and CodeQL have not yet run on this merge candidate. It is not eligible to merge until those checks, code review, and the remaining production acceptance gates pass.
- PR #158's centralized readiness verifier was audited during the continuation. Its initial SQL had an incorrect aclexplode privilege label (TEMP rather than PostgreSQL's TEMPORARY) and omitted PUBLIC grants on the mg_agent schema and operation ledger. The PR #158 branch now contains a correction and regression tests for PUBLIC schema USAGE and ledger SELECT grants at commit 8661aeaca8ca3aa5bd4110a4ebd2f9c303ac0457. This corrected child branch has not yet been integrated into #160 or validated by CI. Do not consume its older provisioner snapshot wholesale: it removes the later fault-injection hooks and must be semantically reconciled with the current #160 provisioner before integration.
- Issue #157 remains open. Real child-process termination and restart/recovery at every external side-effect boundary, production secret-manager rotation/backup restore, deployed mTLS lifecycle, monitoring and administrator operational controls remain outstanding. Keep Provisioning:WorkerEnabled=false.

## 11.4 Real process-kill crash/recovery tests — 2026-11-21

- Draft PR #161: test/provisioning-process-kill-recovery → integration/provisioning-agent-security-stack-20261121.
- Exact head: 09cbda662eba7ea37517ee7ba1ab18f358947ec8.
- Added a dedicated crash-worker executable. Integration tests start it as a separate OS process, wait until it reaches a named external side-effect boundary, forcibly terminate the entire process tree, inspect durable PostgreSQL state, and retry through a fresh provisioner instance. This is process termination, not callback-thrown exceptions or graceful cancellation.
- Eight hard-kill boundaries are covered:
  1. after owner role creation (inside the role transaction);
  2. after application role creation (inside the role transaction);
  3. after owner membership setup (inside the role transaction);
  4. after CREATE DATABASE but before the immutable marker;
  5. after marker write but before database access configuration;
  6. after owner readiness but before tenant baseline DDL;
  7. after tenant baseline DDL but before runtime grants;
  8. after runtime grants.
- Added an explicit post-runtime-grants injection hook so the last external privilege mutation can be tested.
- First exact-head run exposed three test assertion failures caused by querying schema mg before baseline creation. The implementation behavior was not the failure; the test now only queries runtime role grants after the schema exists.
- Exact-head PostgreSQL 17 run 44923819539: **144 passed, 0 failed, 0 skipped**.
- Exact-head backend/web run 44923819583: Release backend build passed; isolated agent API build 0 warnings/0 errors; backend tests 144 passed; frontend tests 20 passed; dependency audit passed; production bundle succeeded; Pages deployment skipped for PR.
- Issue #157 received the evidence and remains open. These tests cover tenant-resource DDL/privilege side effects, not every ledger mutation. Remaining process-kill coverage must include operation-ledger insert, credential rewrap, phase updates, and any secret-manager boundary. Production secret-manager/key rotation and backup/restore, deployed mTLS lifecycle, monitoring/alerting and privileged administrator controls also remain outstanding.
- Keep PR #161 Draft. No merge/deploy was performed. Keep Provisioning:WorkerEnabled=false.

### Next continuation tasks

1. Add real child-process kill/restart coverage for durable operation-ledger writes (prepared credential insert, credential rewrap, phase transitions) and prove idempotent recovery after each boundary.
2. Reconcile PR #158's centralized readiness verifier into the current #160 stack only after preserving the newer fault-injection seams and all PUBLIC database/schema/ledger ACL checks. Its TEMPORARY ACL correction has not yet passed CI on the corrected branch.
3. Run formatting and CodeQL on the #160/#161 stacked candidate after the parent PR quality-gate workflow change is available; keep all PRs Draft.
4. Complete production secret-manager, backup/restore, mTLS, monitoring and administrator operational acceptance. Worker remains disabled.

## 11.5 Granular database/runtime ACL process-kill recovery — 2026-11-21

- PR #161: `test/provisioning-process-kill-recovery` → `integration/provisioning-agent-security-stack-20261121`; remains Draft/open/unmerged.
- Current PR head: `25d66a17e7f83096cfd74c3960ec5e7cd1a4b0dd`. Latest code/test SHA: `5ad6256b7e5ca9268b35f9a608ea65b8e26b6f3c`; subsequent commit updates only the acceptance documentation.
- A dedicated .NET crash-worker executable runs in a separate OS process. Integration tests wait for explicit boundary signals, forcibly terminate the process tree, inspect persisted PostgreSQL state and retry through fresh provisioner/store contexts. These are hard process-kill tests, not injected exceptions or graceful cancellation.
- The tenant provisioner now exposes individual test seams after each database access ACL/CONNECT mutation and after each tenant runtime ACL mutation. The multi-command grant batch was split into independently committed idempotent DDL statements so a crash can occur between them.
- 23 hard-kill boundaries are covered:
  1. owner role creation before role-transaction commit;
  2. application role creation before role-transaction commit;
  3. owner membership before role-transaction commit;
  4. CREATE DATABASE before immutable marker;
  5. PUBLIC database ACL revoke before marker;
  6. marker before database access configuration;
  7. PUBLIC database ACL re-revoke during access configuration;
  8. application-role CONNECT grant;
  9. control-role CONNECT grant;
  10. completed database access configuration;
  11. owner readiness before tenant baseline;
  12. tenant baseline before runtime grants;
  13. PUBLIC schema privilege revoke;
  14. PUBLIC table privilege revoke;
  15. PUBLIC function privilege revoke;
  16. default PUBLIC function EXECUTE revoke;
  17. runtime schema USAGE grant;
  18. runtime mutable-table privileges grant;
  19. runtime read-only-table privileges grant;
  20. prepared credential INSERT before transaction commit;
  21. credential rewrap UPDATE before transaction commit;
  22. operation phase UPDATE before transaction commit;
  23. runtime-grant completion before coordinator phase advancement.
- Database access cases inspect PUBLIC ACL and application/control CONNECT state. Runtime ACL cases independently verify intermediate runtime readiness. Every test retries and verifies final runtime readiness. Ledger tests verify transaction rollback and idempotent retry.
- Exact code/test PostgreSQL 17 run `44943851135`: **159 passed, 0 failed, 0 skipped**.
- Exact code/test full backend/web run `44943851128`: backend tests 159 passed; isolated agent API Release build 0 warnings/0 errors; frontend tests 20 passed; dependency audit and production bundle passed; Pages deployment skipped for PR.
- Current docs-only PR head PostgreSQL run `44944097189`: success.
- Updated PR #161 body and Issue #157 with this evidence. Issue #157 remains open. Production secret-manager/key rotation and backup/restore, deployed mTLS lifecycle, monitoring/alerting, privileged administrator operational controls, worker heartbeat/orchestration acceptance and staging remain outstanding.
- Keep PR #161 Draft and unmerged. No deployment. Keep `Provisioning:WorkerEnabled=false`.

### Next continuation tasks

1. Extend process-kill tests to the isolated secret-manager/Vault side effects and API/agent response-loss boundaries using a disposable test provider; prove no credential is lost or rotated on retry.
2. Add worker-level heartbeat/orchestration fault tests (lease renewal, heartbeat loss, shutdown, stale handler completion) rather than relying only on lease-store fencing tests.
3. Finish production mTLS certificate issuance/revocation/rotation, secret-manager policy/key rotation, backup/restore drills, monitoring/alerting and staging evidence.
4. Review PR #158/#160/#161 stack dependency and the quality-gate PR #159 before any human-approved integration. Do not merge, rebase, retarget or deploy without authorization.
5. Keep worker disabled until every mandatory operations acceptance checkbox has real deployment evidence.

## 11.6 Worker heartbeat/orchestration acceptance — 2026-11-21

- Draft PR #165: `test/provisioning-worker-heartbeat-orchestration-release` → `test/provisioning-process-kill-recovery`; current head `ba732b31c882227dbf5bb00db00f27d36c728946`. Earlier PRs #162, #163 and #164 were closed as superseded; no merge/deploy.
- Refactored `TenantProvisioningWorker` to accept internal test-only timing values and expose one internal processing iteration. Public DI constructor retains production defaults: lease 2 minutes, poll 5 seconds, renewal 30 seconds.
- PostgreSQL integration tests exercise actual worker orchestration for:
  1. handler held beyond renewal interval; lease expiry advances while the same fencing token/owner remains;
  2. reclaimed lease causes renewal to return false, cancels handler, and prevents stale completion/failure;
  3. heartbeat database failure cancels handler, leaves claim uncompleted, and lease expiry/reclaim produces a new token;
  4. host shutdown cancels handler and heartbeat stops extending the lease;
  5. uncooperative handler ignores cancellation and returns after reclaim; stale worker cannot complete or activate business;
  6. completion racing with lease expiry cannot activate business;
  7. generic handler exception text is not persisted; only safe fallback code `PROVISIONING_FAILED` is stored;
  8. eight repeated heartbeat/completion races after expiry never activate business;
  9. eight repeated valid-lease heartbeat/completion races complete and activate under the current claim only.
- Worker logs are captured and asserted to include safe exception types but not secret-bearing exception messages, connection strings or connection details.
- Exact code/test SHA `1c87f546646dc71337e71ceb99ab773b4415568d`: PostgreSQL 17 integration run `44945795410`, **168 passed, 0 failed, 0 skipped**; full backend/web run `44945795465`, backend 168 passed, isolated agent API Release build 0 warnings/0 errors, frontend 20 passed, dependency audit and production bundle passed; Pages deployment skipped for PR.
- Current PR head `ba732b31c882227dbf5bb00db00f27d36c728946` changes only the operations acceptance checklist after the green code/test SHA.
- All seven worker heartbeat/orchestration acceptance checks have automated test evidence. This is not production deployment evidence. Production secret-manager/Vault policy and key rotation/backup-restore, deployed mTLS lifecycle, monitoring/alerting, privileged administrator controls and staging remain open.
- Keep PR #165 Draft and unmerged. No deployment. Keep `Provisioning:WorkerEnabled=false`.

### Next continuation tasks

1. Add process-kill and response-loss coverage around Vault/secret-manager write, readback and CAS update; prove retries preserve the exact application credential and never rotate it unexpectedly.
2. Add repeated stress tests for concurrent duplicate provisioning requests and multi-tenant isolation while control-plane maintenance locks are acquired/released.
3. Complete production secret-manager/Vault policy, key rotation and backup/restore drills; deployed mTLS issuance/revocation/rotation; monitoring/alerting; privileged administrator operational controls and staging.
4. Review PR #159 quality gates and the full #153→#154→#155/#158→#160→#161→#165 dependency stack before any human-approved integration. Do not merge, retarget, rebase or deploy without authorization.
5. Keep `Provisioning:WorkerEnabled=false` until all mandatory operations acceptance gates have dated production evidence.

## 11.7 Vault response-loss and CAS recovery tests — 2026-11-21

- Draft PR #166: `test/vault-secret-response-loss-recovery` → `test/provisioning-worker-heartbeat-orchestration-release`; current head `bda9dbcd28c421d791c5ecfa38370a047f10daf6`.
- Added tenant-handler tests where the Vault KV v2 write commits but the HTTP response is lost, and where the write succeeds but the readback response is lost. Retrying reads the exact committed connection string, does not provision a second credential and does not write a second secret.
- Added a stateful KV v2 HTTP test server enforcing `cas=0`. Concurrent create-only writes allow exactly one winner, reject the conflicting write and preserve the winning value. A lost POST response is recovered by GET; an explicit replacement attempt cannot overwrite the original secret.
- Exact code/test SHA `3792d120244661283a5800e607f1b57c1e0f3bf6`: PostgreSQL 17 integration run `44946279568`, **172 passed, 0 failed, 0 skipped**; full backend/web run `44946279558`, backend 172 passed, isolated agent API Release build 0 warnings/0 errors, frontend 20 passed, dependency audit and production bundle passed; Pages deployment skipped for PR.
- Current PR head `bda9dbcd28c421d791c5ecfa38370a047f10daf6` changes only the operations acceptance documentation after the green code/test SHA.
- This is deterministic adapter/handler test evidence, not a deployed Vault process-kill test. Real Vault token policy, production integration, key rotation, backup/restore, deployed mTLS lifecycle, monitoring/alerting, privileged administrator controls and staging remain open. Keep worker disabled.

### Next continuation tasks

1. Add real secret-manager/Vault deployment acceptance: scoped policy review, short-lived token lifecycle, TLS validation, health/readiness, audit evidence and no-secret logging.
2. Run real key-rotation and backup/restore drills for both the platform Vault-held tenant connection secrets and the agent AES-GCM operation ledger key ring.
3. Add true process-kill and end-to-end API-agent response-loss tests using a disposable deployed Vault and isolated mTLS agent, proving credential replay across process restarts.
4. Complete deployed mTLS certificate issuance/revocation/rotation, monitoring/alerting, privileged administrator operational controls and staging evidence.
5. Review PR #159 quality gates and the full #153→#154→#155/#158→#160→#161→#165→#166 dependency stack before any human-approved integration. Do not merge, retarget, rebase or deploy without authorization.
6. Keep `Provisioning:WorkerEnabled=false` until all mandatory operations acceptance gates have dated production evidence.

## 11.7 Vault response-loss, CAS and tenant-concurrency tranche — 2026-11-22

- Draft PR #166: `test/vault-secret-response-loss-recovery` → `test/provisioning-worker-heartbeat-orchestration-release`; current head `47326dae63ca4397310257a88d6b8c2397670c88`. No merge/deploy.
- Added handler/PostgreSQL tests for a Vault KV v2 write that commits but loses its response and a successful write followed by lost readback response. A retry reuses the exact credential already stored, does not reprovision the database, and does not issue a second secret write.
- Added stateful KV v2 HTTP adapter tests enforcing `cas=0`: recover committed write via GET after response loss, reject replacement of an existing secret, and race concurrent create-only writes so exactly one credential wins.
- Added PostgreSQL coordinator test: tenant A remains blocked inside its external provisioning stage while tenant B independently enters its own stage, proving per-tenant locks do not serialize unrelated tenants. Existing tests cover eight concurrent duplicate requests producing one credential and reject cross-tenant idempotency-key reuse.
- Exact code/test SHA `62e05f99df264e55f2374270143b5f36b64aff38`: PostgreSQL 17 run `44957225349`, **173 passed, 0 failed, 0 skipped**; backend/web run `44957225288`, backend 173 passed, isolated agent API Release build 0 warnings/0 errors, frontend 20 passed, dependency audit 0 vulnerabilities and production bundle passed; Pages deployment skipped for PR.
- PR head `47326dae63ca4397310257a88d6b8c2397670c88` adds acceptance documentation only after the green code/test SHA.
- These deterministic Vault adapter/handler tests are not evidence from a deployed Vault cluster or actual Vault process termination. Real Vault token policies, process-kill at secret-manager boundaries, key rotation, backup/restore, deployed mTLS lifecycle, production metrics/alerts, privileged admin controls and staging remain open.
- Keep PR #166 Draft/unmerged; no deployment. Keep `Provisioning:WorkerEnabled=false`.

### Next continuation tasks

1. Build a disposable real-Vault test service in CI (or a separately provisioned isolated test environment) and add process termination/response-loss at Vault write, readback, token renewal/revocation and CAS boundaries. Do not substitute deterministic fake HTTP handlers for real Vault acceptance.
2. Add end-to-end Platform API duplicate-request tests across registry, durable queue, coordinator and tenant activation; verify same idempotency key replays the same result and concurrent cross-tenant key reuse fails before external side effects.
3. Complete secret-manager token policy, key rotation and backup/restore drills; deployed mTLS issuance/revocation/rotation; monitoring/alerting; privileged administrator controls and staging.
4. Review PR #159 quality gates and the full #153→#154→#155/#158→#160→#161→#165→#166 dependency stack before any human-approved integration. Do not merge, retarget, rebase or deploy without authorization.
5. Keep `Provisioning:WorkerEnabled=false` until every mandatory operations acceptance gate has dated production evidence.

## 11.8 Business registry and outbox concurrency tranche — 2026-11-22

- Added `backend/tests/Mg.Platform.Api.Tests/BusinessRegistryConcurrencyPostgresTests.cs` to Draft PR #166.
- PostgreSQL tests use independent EF Core DbContexts against disposable schema:
  1. 16 concurrent same-slug requests result in exactly one business + one provisioning job; 15 return conflict.
  2. 12 concurrent distinct slugs result in 12 businesses + 12 jobs, with 12 distinct idempotency keys.
  3. A PostgreSQL trigger forces outbox insert failure; EF's SaveChanges transaction rolls back the preceding business insert, leaving zero businesses and zero jobs.
- Exact code/test SHA `d04368366b4f12230b6091a09b705fb000b3d87f`: PostgreSQL 17 run `44957701458`, **176 passed, 0 failed, 0 skipped**; backend/web run `44957701527`, backend 176 passed, isolated agent API Release build 0 warnings/0 errors, frontend 20 passed, dependency audit and production bundle passed; Pages deployment skipped for PR.
- Current PR head `22b18f40a46e55d52832cbdac8c857f84c743773` adds acceptance docs only after the green code/test SHA.
- These are service-level tests, not HTTP authentication/endpoint or client response-loss replay. Keep those end-to-end acceptance gates open. No merge/deploy; worker remains disabled.

### Next continuation tasks

1. Add HTTP integration tests for `POST /api/v1/platform/businesses` through the real authorization and routing pipeline using signed test JWTs and disposable PostgreSQL. Verify same-slug concurrent requests return exactly one 201 and remaining 409s, and business/outbox rows are atomic.
2. Add a simulated client response-loss/retry test against the API route: after the first request commits but before the client receives the response, define and verify the intended idempotency contract. Current create-business contract has no client-supplied idempotency key, so do not assume a retry deduplicates; decide whether to add an explicit request idempotency key before claiming replay safety.
3. Add API-level negative tests for unauthenticated, wrong-role, malformed request, rate-limit and duplicate slug cases.
4. Continue real Vault process-kill/token-policy/key-rotation/backup-restore, deployed mTLS lifecycle, metrics/alerts, privileged admin controls and staging.
5. Keep `Provisioning:WorkerEnabled=false`; do not merge, retarget, rebase or deploy without explicit approval.



- 2026-11-18: Master requirements, architecture, continuation rules, implementation snapshot and production release gates consolidated into this repository governance document.\n- 2026-11-18: Recorded Draft PR #153 for Creator OIDC authentication hardening; its exact-head MG Web workflow subsequently passed.\n- 2026-11-18: Recorded Draft PR #154 adding durable provisioning lease and idempotency schema; exact-head CI was queued. Initial implementation status is based on PR #105 snapshot; revalidate before further work.
- Future sessions must append dated entries with PR/commit, concrete changes, tests/results, blockers and next task. Never erase historical verification; mark superseded evidence clearly.


- 2026-11-18: Master requirements, architecture, continuation rules, implementation snapshot and production release gates consolidated into this repository governance document.\n- 2026-11-18: Recorded Draft PR #153 for Creator OIDC authentication hardening; its exact-head MG Web workflow subsequently passed.\n- 2026-11-18: Recorded Draft PR #154 adding durable provisioning lease and idempotency schema; exact-head CI was queued. Initial implementation status is based on PR #105 snapshot; revalidate before further work.
- Future sessions must append dated entries with PR/commit, concrete changes, tests/results, blockers and next task. Never erase historical verification; mark superseded evidence clearly.


## 11.9 Platform business HTTP endpoint integration — 2026-11-22

- Added `backend/tests/Mg.Platform.Api.Tests/PlatformBusinessEndpointsPostgresTests.cs` to Draft PR #166. Tests use ASP.NET Core TestServer, the real `PlatformEndpoints.MapPlatformEndpoints` route group, real `mg-creator` authorization policy and rate-limiter middleware, with disposable PostgreSQL.
- 16 simultaneous POSTs to `/api/v1/platform/businesses/` for the same slug produce exactly one HTTP 201 and fifteen HTTP 409 responses; database ends with one business and one outbox job.
- Negative endpoint cases: no auth => 401; authenticated non-platform role => 403; invalid slug => 400; all leave registry/outbox empty.
- Authentication in this endpoint integration uses a test-only scheme to inject claims. It tests the real authorization policy and route middleware but does not prove JWT signature/JWKS/issuer/audience behavior. Production OIDC identity-provider staging remains open.
- Exact code/test SHA `89cb2ceca29a41563d56e83ec2c18b34a9910428`: PostgreSQL 17 run `44958193288`, **178 passed, 0 failed, 0 skipped**; backend/web run `44958193261`, backend 178 passed, isolated agent API Release build 0 warnings/0 errors, frontend 20 passed, dependency audit and production bundle passed; Pages deployment skipped for PR.
- Current PR head `1793e7debe0d051b6ba9bfde7fbc8be1f972c421` adds acceptance documentation only after the green code/test SHA.
- The create-business request has no client-supplied idempotency key. Do not claim retry deduplication after a committed request's lost HTTP response; design/implement an explicit request idempotency contract and test it end-to-end.
- No merge/deploy. Keep `Provisioning:WorkerEnabled=false`.

### Next continuation tasks

1. Design client idempotency for business creation: decide scope, key format, request fingerprint semantics, retention, conflict behavior, response replay representation and concurrent cross-key/tenant behavior. Record the decision in `docs/DECISIONS.md` before implementation.
2. Implement durable API idempotency records atomically with business + outbox writes; same key/same payload must replay original 201 response, same key/different payload must return deterministic conflict, and same key cannot be reused by another creator/tenant scope.
3. Add TestServer + PostgreSQL response-loss tests: let the first request commit, simulate connection drop before response delivery, retry with the same idempotency key and verify the same business/job/response; also cover simultaneous identical requests and changed-payload key reuse.
4. Replace test-only auth in a separate OIDC integration suite with signed test JWTs and a local test issuer/JWKS endpoint to validate signature, issuer, audience, expiry and role claims through the real JwtBearer middleware.
5. Continue real Vault process-kill/token-policy/key-rotation/backup-restore, deployed mTLS lifecycle, metrics/alerts, privileged admin controls and staging.
6. Keep `Provisioning:WorkerEnabled=false`; do not merge, retarget, rebase or deploy without explicit approval.


## 11.10 Creator endpoint rate-limit verification — 2026-11-22

- Extended `PlatformBusinessEndpointsPostgresTests` with a 31-request concurrent burst from the same TestServer client/IP partition.
- The real fixed-window `creator-bootstrap` policy admits exactly 30 requests (HTTP 201) and rejects the 31st (HTTP 429); exactly 30 businesses and 30 outbox jobs are persisted.
- Exact code/test SHA `cd057ef7b35617466d70749211881854417226b5`: PostgreSQL 17 run `44958557297`, **179 passed, 0 failed, 0 skipped**; backend/web run `44958557395`, backend 179 passed, isolated agent API Release build 0 warnings/0 errors, frontend 20 passed, dependency audit and production bundle passed; Pages deployment skipped for PR.
- Current PR #166 head `2e9fd99d0dccfeb006be2db8acb7a8c3ed1ab259` updates acceptance evidence only after the green code/test SHA.
- This is test-server evidence for middleware behavior, not production ingress/load testing. OIDC/JWKS, API idempotency/replay, real Vault/mTLS, monitoring and staging remain open. Keep provisioning worker disabled.


## 11.11 Durable business registration idempotency — 2026-11-22

- Draft PR #166 implements ADR-009 in `docs/DECISIONS.md`, migration `backend/database/migrations/006_business_creation_idempotency.sql`, model `BusinessCreationRequest`, EF mapping and API contract `backend/docs/PLATFORM_API_CONTRACT.md`.
- `POST /api/v1/platform/businesses/` now requires a non-empty UUID `Idempotency-Key` and validated OIDC `iss` + `sub`. The DB stores SHA-256 hashes of creator identity and normalized name/slug, not raw identity or request body.
- The idempotency row, business row and provisioning outbox job are written atomically. Same key/same creator/same normalized payload replays HTTP 201 for the same business ID/Location and adds `Idempotency-Replayed: true`; changed payload or creator returns 409. Records are retained for the business lifetime.
- Concurrency behavior: 16 simultaneous same-key requests return one created resource and 15 replays. A race exposed that a same-key retry could see the slug after the winner committed but before its second key lookup; service now re-reads the idempotency record before treating the slug as an unrelated conflict.
- A TestServer middleware deliberately throws during response-body delivery after the database transaction commits. The test verifies transport failure, one durable business/job/key row, then same-key retry successfully recovers the same business ID without duplicate writes.
- Exact code/test SHA `2f1574ea91b7c33367bb6dae424aba2b8fe90462`: PostgreSQL 17 run `44959639617`, **187 passed, 0 failed, 0 skipped**; backend/web run `44959639818`, backend 187 passed, isolated agent API Release build 0 warnings/0 errors, frontend 20 passed, dependency audit and production bundle passed; Pages deployment skipped for PR.
- Current PR head `d9c8ded2ee5126b87053c9c4f723ee7f94fa4d73` adds acceptance docs only after the green code/test SHA.
- Production gates remain: controlled deployment of migration 006, real OIDC/JWKS issuer validation and staging, actual ingress/socket response-loss testing, real Vault token/process-kill/key rotation/backup-restore, deployed mTLS lifecycle, monitoring/metrics/alerts, privileged admin controls and staging. No merge/deploy; keep `Provisioning:WorkerEnabled=false`.

### Next continuation tasks

1. Add signed test JWTs and a local OIDC/JWKS test issuer to validate the real JwtBearer pipeline (signature, issuer, audience, expiry, role, subject) instead of relying only on test-auth handlers.
2. Add actual socket-level response disconnect through a local Kestrel host or reverse-proxy harness after the business/outbox/idempotency commit, then retry with the same key.
3. Add a safe deployment migration gate: API readiness must fail closed if migration 006 is absent or its checksum is unexpected; document rollback behavior for an API version requiring the new header.
4. Add production secret-manager and mTLS deployment rehearsals, key rotation/restore, metrics/alerts and staging evidence.
5. Keep `Provisioning:WorkerEnabled=false`; no merge/deploy without explicit authorization.


## 11.12 Migration 006 readiness gate and OpenAPI contract — 2026-11-22

- Added `PlatformSchemaReadinessHealthCheck` and registered it in `/health/ready`. It verifies `business_tenants`, `tenant_provisioning_jobs`, `business_creation_requests`, required idempotency/hash columns and the named primary key. Missing/incomplete migration 006 returns Unhealthy; the check never applies migrations automatically.
- Added PostgreSQL tests for healthy schema and fail-closed readiness after dropping `business_creation_requests`.
- Added OpenAPI metadata for required `Idempotency-Key` header and TestServer assertion that it is documented as required and carries UUID/retry guidance.
- Exact tested code head `6bf8eceba092ff8fba6e586a69e501d804cd472a`: PostgreSQL 17 run `44960616374`, **190 passed, 0 failed, 0 skipped**; backend/web run `44960616393`, backend 190 passed, isolated agent API Release build 0 warnings/0 errors, frontend 20 passed, dependency audit and production bundle passed; Pages deployment skipped for PR.
- Current PR #166 head `5818165f0fde6145bf849bfaad126656cef49bab` adds documentation only after the green code/test head.
- Migration 006 must be applied via controlled deployment before the new API receives traffic. This readiness gate prevents accidental early serving; it does not replace migration checksum/ordering verification or a production rollout rehearsal. Real OIDC/JWKS, socket/ingress response loss, Vault/mTLS, monitoring, privileged admin controls and staging remain open.
- Keep PR #166 Draft/unmerged; no deployment. Keep `Provisioning:WorkerEnabled=false`.


## 11.13 Exact-head revalidation for idempotent business creation — 2026-11-22

- Current Draft PR #166 head `910d343a0e0cbf9679a0f1981ab46bcc7fab646e`; no code changes after tested behavior head `6bf8eceba092ff8fba6e586a69e501d804cd472a`, only acceptance-evidence documentation.
- Exact current-head PostgreSQL 17 run `44960849679`: **190 passed, 0 failed, 0 skipped**.
- Exact current-head backend/web run `44960849725`: backend 190 passed, isolated agent API Release build 0 warnings/0 errors, frontend 20 passed, dependency audit and production bundle passed; Pages deployment skipped for PR.
- PR remains Draft/open/unmerged; no deployment. `Provisioning:WorkerEnabled=false` remains mandatory.


## 11.14 Latest exact-head validation — 2026-11-22

- Draft PR #166 current head `a1a64864f92b730716c072ca8f20e144f793f237`; acceptance documentation only after the validated implementation.
- Exact current-head PostgreSQL 17 run `44961023483`: **190 passed, 0 failed, 0 skipped**.
- Exact current-head backend/web run `44961023493`: backend 190 passed, isolated agent API Release build 0 warnings/0 errors, frontend 20 passed, dependency audit and production bundle passed; Pages deployment skipped for PR.
- PR remains open/Draft/unmerged. No deployment. Keep `Provisioning:WorkerEnabled=false`.

## 11.15 Current repository and release-gate checkpoint — 2026-11-22

### Main and repository controls
- Default branch remains `main` at `88ea40799eda5c457c6c742eff26f2e8868f7f8a`; no PR was merged and no release was created in this run.
- Main's last verified Windows CI remains 300/300 tests, Release build 0 warnings/0 errors, installer/package/checksum verification successful. Scheduled dependency audit run `44996001272` completed successfully.
- Active main ruleset `26519007` requires one approving human review, strict up-to-date `build-test-package`, non-fast-forward protection and branch deletion protection. Do not use the configured bypass.
- The repository has 19 open PRs after adding #167 and #168. Keep product and integration candidates Draft until their exact-head quality gates and review requirements are satisfied.

### CI and release automation
- PR #159 removes the main-only base filter from pull_request triggers for Windows build/test/package, CodeQL, formatting and NuGet audit. Exact head `495d400b0ae0572032059f663191d65e797e43f2`: Windows run `44882937558` passed 300/300 tests, 0 warnings/0 errors, both installers and source/portable archives, SHA-256 verification and artifact upload (artifact `14698398415`, digest `566c617f934f83170ba8f5cb7afa734c217f2bfeecd1303bae0e5c19dc0a813d)); CodeQL `44882937661`, formatting `44882937577` and NuGet audit `44882937631` all passed. PR #159 is Ready for Review but still requires one human approval; it is not merged.
- PR #156 splits Windows release build/verify from publish, leaves workflow permissions read-only and grants contents:write only to publish. It pins Caddy 2.11.9 and verifies the official asset SHA-256. Exact head `84a729f0592797347f2a5533dd926aa62524c3df`: Windows run `44921237401` passed 300/300 tests with 0 warnings/0 errors and uploaded artifact `14713313853`; CodeQL `44921237437`, NuGet audit `44921237517`, formatting `44921237441` and PR-only artifact transfer/checksum test `44921237408` passed. On the PR, real build-and-publish jobs were correctly skipped. No GitHub Release was created. PR #156 is Ready for Review; do not dispatch the real publish path until an approved main release candidate exists.
- The current main release workflow remains unexecuted for a real release candidate. Synthetic transfer tests are not a production publication test.

### Provisioning and migration candidates
- PR #160 current head `233f33ba4be6be3c23205475d5403a4445cd9e31` passed PostgreSQL 17 integration `44958816107` (138/138) and backend/web `44958816105` (backend 138, frontend 20, 0 build warnings/errors, npm audit 0 vulnerabilities, Vite production bundle). Only the two workflows defined on this stacked base ran.
- PR #161's base snapshot was older integration commit `d0b962a46378fd152d0181e1fb3af7c37ff108d5`, while current #160 advanced three commits. The current parent/child comparison is diverged (23 ahead / 3 behind). The three parent-only commits change `AgentProvisioningOperationStorePostgresTests.cs`, which is not in #161's changed-file set.
- To avoid force-push/rebase, Draft PR #168 was created from current #160 head `233f33ba4be6be3c23205475d5403a4445cd9e31` and overlays exactly the eight files changed by #161. Candidate head `343a6397049fbb243d166f83169a1d02abed8c1f`; CI runs `45000864366` (PostgreSQL provisioning) and `45000863625` (backend/web) were triggered and must be checked to completion on this exact SHA. Do not rely on the old #161 results for the combined tree.
- PR #166 exact current head `a1a64864f92b730716c072ca8f20e144f793f237` passed PostgreSQL 17 integration `44961221072` (190/190) and backend/web `44961221016` (backend 190, frontend 20, npm audit 0, production bundle). Earlier runs `44960300320` and `44960300347` failed because the OpenAPI test assumed an inline parameter and threw KeyNotFoundException on a referenced parameter. The test was corrected to resolve local OpenAPI references; the exact current head passes. PR #166 remains based on the older #165/#161 stack and must be synchronized after #168 is validated.
- PR #167 strengthens migration 006 readiness: all five required idempotency columns/types/not-null, primary key, business foreign key with RESTRICT, both hash checks and valid/ready lookup index. Six PostgreSQL failure-mode tests were added. Exact head `44df3b40437d27d8bf52b0da999aadec0ef6024f`: PostgreSQL run `45000201961` passed 196/196; backend/web `45000201930` passed 196 backend tests, 0 build warnings/errors, npm audit 0 vulnerabilities, Vite bundle and artifact `14745637016`. These are the two workflows available on the stacked base; Windows installer packaging, CodeQL, formatting and dedicated NuGet audit remain pending until #159 is merged and the base is synchronized. Keep #167 Draft.
- PR #121 still has an old 299/300 Windows failure. PR #150's exact head `9db1a36682f41de30161ed75eabe49f8a2283df1` passed 300/300 with formatting, CodeQL, NuGet audit and Windows packaging; #121 must be updated non-destructively and retested. Do not count #150's result as validation of #121.

### Production blockers remain
- No real OIDC/JWKS issuer validation/staging integration, socket-level response-loss/ingress test, production Vault process/response-loss/key-rotation rehearsal, mTLS deployment/rotation/revocation, monitoring/alerting or privileged administrator operational acceptance.
- No tenant production provisioning agent deployment or production worker enablement. Keep `Provisioning:WorkerEnabled=false`.
- No verified production tenant migration/upgrade/rollback, central/tenant backup restore drill, measured RPO/RTO, pilot acceptance or real hardware/network validation.
- Accounting, POS/inventory, cafe/game-net, licensing and customer-facing workflows remain incomplete end-to-end; do not infer product completeness from backend provisioning tests or green CI.

### Next continuation order
1. Verify exact-head completion of PR #168's two workflows; inspect any failing job logs before changes.
2. If #168 is green, build a non-destructive current-parent integration for PR #165 and then #166, preserving their exact changes and rerunning all applicable tests at each step.
3. Obtain the required human review for #159 and #156; do not merge or publish without approval.
4. After #159 is merged by an authorized reviewer, synchronize stacked bases and require Windows packaging, CodeQL, formatting and NuGet audit on each release candidate.
5. Refresh PR #121 with the #150 migration-test correction and rerun its exact-head full CI.
6. Continue the highest-priority production gates above; keep worker disabled and do not claim release readiness until all section 10 gates are evidenced.


## 11.16 Creator OIDC/JWKS validation tests on current worker stack — 2026-11-22

- Draft PR #170 now integrates the durable business-creation idempotency slice onto the current provisioning-worker stack and carries the repository governance files (AGENTS.md and this ledger) into the candidate branch.
- Current exact PR head before this documentation-only commit: 7eaa0a0852e0dc771aefa9361900a54e40682e82; base 19958889c7b337c77a56e6fe8b84b72dc3c210b6; 6 commits ahead, 0 behind; 20 changed implementation files; PR remains Draft/open/unmerged.
- Added CreatorOidcJwtBearerIntegrationTests: the real JwtBearer middleware is exercised against a local OIDC discovery document and RSA JWKS endpoint, without the test authentication handler. Tests verify RS256 signature acceptance, issuer, audience, lifetime, raw iss/sub claim preservation with MapInboundClaims=false, Creator role authorization, rejection of an unpublished signing key, and 403 for a non-Creator role.
- Exact-head PostgreSQL 17 run 45037892020: **198 passed, 0 failed, 0 skipped**.
- Exact-head backend/web run 45037891962: backend **198 passed**, frontend **20 passed**, Release builds for the platform backend and isolated agent API both **0 warnings / 0 errors**, dependency audit **0 vulnerabilities**, Vite production bundle succeeded; Pages deployment was correctly skipped for a pull request.
- Two intermediate attempts exposed test-harness issues and were corrected before the passing head: missing TestServer routing registration (cbf6ee5...) and an expired-token fixture whose NotBefore exceeded Expires (acc4faa...). The current exact-head runs above are the passing evidence.
- This local OIDC/JWKS fixture validates JwtBearer behavior but is **not** real identity-provider staging, TLS/issuer operations, key rotation or production OIDC acceptance. Socket-level response-loss testing, real Vault process-kill/response-loss/key-rotation rehearsal, deployed mTLS lifecycle, monitoring/alerts, controlled migration 006 deployment, backup/restore, and operator acceptance remain open.
- Keep Provisioning:WorkerEnabled=false; keep PR #170 Draft; do not merge, deploy or claim release readiness without the required human review and release gates.

### Next continuation order

1. Add a real Kestrel loopback/socket-level client-disconnect test after the business/outbox/idempotency transaction commits, then prove same-key retry recovers the original business without duplicate rows.
2. Add an issuer/JWKS rotation and key-refresh test, including unknown-key behavior and bounded metadata refresh, then validate against an actual staging identity provider when credentials/environment are available.
3. Continue production Vault process-kill/response-loss, mTLS lifecycle, monitoring/alerting, migration 006 deployment/rollback and backup/restore rehearsals.
4. Preserve the current worker-disabled setting and keep all integration/release candidates Draft until exact-head checks, human reviews and operational gates are complete.


## 11.17 Socket-level business creation response-loss recovery — 2026-11-22

- Added `BusinessCreationSocketResponseLossTests`, which runs the real platform endpoint on loopback Kestrel rather than TestServer. The response is held at `OnStarting` only after the endpoint has completed its database transaction; the client then cancels the request, and a retry with the same UUID idempotency key must return the original HTTP 201 resource with `Idempotency-Replayed: true`.
- The test verifies exactly one business, one provisioning outbox job and one idempotency record remain after the interrupted response and successful retry.
- Exact current code/test head `7403ada7be706c66214f39b3790a59b2cf3bfe6e` passed PostgreSQL 17 integration run `45038482705`: **199 passed, 0 failed, 0 skipped**.
- Exact current code/test head backend/web run `45038482702`: backend **199 passed**, frontend **20 passed**, platform backend and isolated provisioning-agent API Release builds **0 warnings / 0 errors**, dependency audit **0 vulnerabilities**, Vite production bundle succeeded; Pages deployment was skipped as expected for a PR.
- The first Kestrel harness attempt exposed a missing hosting namespace import; this was fixed in commit `1bd6510...` before the passing exact-head runs.
- This is a loopback Kestrel socket-disconnect test, not a public ingress/reverse-proxy test or production network acceptance. Actual identity-provider staging, signing-key rotation, Vault process-kill/response-loss/key rotation, deployed mTLS, monitoring/alerts, controlled migration/rollback, backup/restore and operator acceptance remain open.
- Keep `Provisioning:WorkerEnabled=false`; PR #170 remains Draft/open/unmerged. No deployment or production changes were made.

### Next continuation order

1. Add OIDC signing-key rotation and JWKS refresh coverage, including an initially unknown key, bounded metadata refresh and safe transition from old to new signing key.
2. Validate issuer/audience/role mapping against a real staging identity provider when a controlled staging issuer and credentials are available; do not substitute the local fake issuer for staging evidence.
3. Continue production Vault process-kill/response-loss/key-rotation, mTLS lifecycle, monitoring/alerting, migration 006 deployment/rollback and backup/restore rehearsals.
4. Preserve the worker-disabled setting and keep integration/release candidates Draft until exact-head checks, required human reviews and operational gates are complete.


## 11.18 OIDC JWKS refresh and signing-key rotation — 2026-11-22

- Extended `CreatorOidcJwtBearerIntegrationTests` with a mutable local JWKS issuer. The test first authenticates with the original RSA key, rotates the published JWKS to a new key ID, requests a configuration refresh using a shortened test-only refresh interval, and verifies a token signed by the new key is accepted and the JWKS endpoint is fetched again.
- Exact current code/test head `98f0363647429a88a30b9296351d5405caf6bd55` passed PostgreSQL 17 run `45039621909`: **200 passed, 0 failed, 0 skipped**.
- Exact current code/test head backend/web run `45039621902`: backend **200 passed**, frontend **20 passed**, platform backend and isolated provisioning-agent API Release builds **0 warnings / 0 errors**, dependency audit **0 vulnerabilities**, Vite production bundle succeeded; Pages deployment was skipped as expected for a PR.
- An exploratory assertion expecting the old key to become immediately unauthorized after a refresh failed: the old token was still accepted. That assertion was removed rather than claiming immediate revocation. The observed old-key retention/grace behavior is not yet explained or specified; key retirement, overlap windows, emergency revocation and cache invalidation need a dedicated design and deterministic test.
- This is a local OIDC/JWKS behavior test, not real identity-provider staging or production key-rotation acceptance. Keep `Provisioning:WorkerEnabled=false`; PR #170 remains Draft/open/unmerged. No deployment or production changes were made.

### Next continuation order

1. Investigate and specify old-key retention, normal rollover overlap, expiry/grace windows and emergency revocation. Add deterministic tests for both overlap and retirement; do not assume a refreshed JWKS immediately invalidates a previously cached key.
2. Validate issuer, audience, roles and signing-key lifecycle against a real staging identity provider when a controlled staging issuer and credentials are available.
3. Continue production Vault process-kill/response-loss/key-rotation, deployed mTLS lifecycle, monitoring/alerting, migration 006 deployment/rollback and backup/restore rehearsals.
4. Preserve the worker-disabled setting and keep integration/release candidates Draft until exact-head checks, required human reviews and operational gates are complete.


## 11.19 Emergency OIDC last-known-good fallback control — 2026-11-22

- Added `Authentication:UseLastKnownGoodConfiguration` to the Creator JwtBearer registration. It defaults to `true` for normal rollover resilience and is applied to the IdentityModel `BaseConfigurationManager` after JwtBearer post-configuration.
- Added the explicit safe default to API `appsettings.json` and documented the emergency operation in `docs/SECURITY.md`: for a confirmed signing-key compromise, set the value to `false` consistently on every API instance and recycle/restart those instances. This disables LKG configuration fallback; it is not instant global token revocation.
- Added integration coverage for both modes: normal rotation verifies LKG remains enabled by default and accepts the newly published signing key after JWKS refresh; emergency mode disables LKG and verifies that the retired signing key is rejected after the new JWKS is loaded.
- The current IdentityModel dependency's default LKG configuration lifetime is one hour unless overridden; the API does not currently override that lifetime. Verify this behavior on dependency upgrades and staging identity providers.
- Exact code/test head `86ac8d8a02873c2e95963c036bc076ee532c04d6` passed PostgreSQL 17 run `45040224427`: **201 passed, 0 failed, 0 skipped**.
- Exact code/test head backend/web run `45040224374`: backend **201 passed**, frontend **20 passed**, platform backend and isolated provisioning-agent API Release builds **0 warnings / 0 errors**, dependency audit **0 vulnerabilities**, Vite production bundle succeeded; Pages deployment was skipped as expected for a PR.
- This remains test-fixture evidence, not real identity-provider staging or a production incident drill. The emergency value must be applied consistently and verified after restart across every API instance. Keep `Provisioning:WorkerEnabled=false`; PR #170 remains Draft/open/unmerged. No deployment or production changes were made.

### Next continuation order

1. Validate normal rollover and emergency LKG-off behavior against a real staging identity provider, including multi-instance restart/config propagation and the provider's actual key-removal/overlap policy.
2. Define and rehearse the signing-key compromise runbook, including operator authorization, config propagation, instance recycle, telemetry/verification and rollback; do not promise instant global revocation.
3. Continue production Vault process-kill/response-loss/key-rotation, deployed mTLS lifecycle, monitoring/alerting, migration 006 deployment/rollback and backup/restore rehearsals.
4. Preserve the worker-disabled setting and keep integration/release candidates Draft until exact-head checks, required human reviews and operational gates are complete.


## 11.20 Fail-closed verification of the complete business-idempotency schema — 2026-11-22

- Ported the schema-readiness hardening from Draft PR #167 onto the current PR #170 integration branch without changing or rewriting PR #167 or its source branch.
- `PlatformSchemaReadinessHealthCheck` now verifies all five required idempotency columns with exact PostgreSQL types and NOT NULL constraints; the named primary key; the `business_id → business_tenants(id)` foreign key with `ON DELETE RESTRICT`; both creator/request SHA-256 format check constraints; and a valid/ready business lookup index. It remains fail-closed and never applies migrations.
- Expanded PostgreSQL readiness failure-mode coverage for absent table, missing business reference column, nullable business reference, missing foreign key, missing creation timestamp and missing creator-hash constraint, plus lookup-index absence. The healthy fixture explicitly creates the migration's required lookup index.
- Updated `backend/docs/PLATFORM_API_CONTRACT.md` so the documented readiness behavior matches the actual schema contract.
- Exact code/test head `78e58e6f1b1ace61e4de863eede62fdbe68217ad`:
  - PostgreSQL 17 integration run `45041267932`: **207 passed, 0 failed, 0 skipped**.
  - Backend/web run `45041267939`: backend **207 passed**, frontend **20 passed**, both platform backend and isolated provisioning-agent API Release builds **0 warnings / 0 errors**, dependency audit **0 vulnerabilities**, Vite production bundle succeeded; Pages deployment correctly skipped for a PR.
- This is automated disposable-PostgreSQL evidence, not a production migration rollout or restore/rollback rehearsal. Migration 006 still requires controlled deployment and verification before the API version that requires `Idempotency-Key` receives traffic.
- PR #170 remains Draft/open/unmerged. No production deployment was performed. Keep `Provisioning:WorkerEnabled=false`.

### Next continuation order

1. Re-run exact-head CI after this ledger-only update; never transfer the code-head evidence to a different commit without checking.
2. Continue the highest-priority unblocked provisioning acceptance work: real Vault process-kill/response-loss/token renewal/revocation/CAS testing and secret policy/key rotation.
3. Complete real staging OIDC/JWKS rollover and emergency signing-key incident rehearsal, deployed mTLS issuance/revocation/rotation, monitoring/alerting, controlled migration 006 deployment/rollback, and backup/restore with measured RPO/RTO.
4. Keep the worker disabled and integration PR Draft until all required human review and production gates are complete.


## 11.21 Real Vault KV v2 integration tests with TLS and least-privilege CI token — 2026-11-22

- Added a real disposable HashiCorp Vault server to both backend CI workflows, using the official Vault 2.2.2 linux/amd64 image pinned by digest `sha256:18c03dcc5589b1a67eb3c7bd74d6982747f0d835a285516b87e3992f085de5a4`.
- Vault runs only inside the isolated CI job in dev mode with TLS enabled and generated CA material. Tests validate the Vault server certificate chain against that CA while still enforcing the 127.0.0.1 hostname; no certificate-validation bypass is used by the test client.
- CI creates a short-lived, non-renewable orphan token with no default policy and a dedicated ACL restricted to `secret/data/ci/*` and `secret/metadata/ci/*`. The dynamic token is masked in Actions logs; the root token remains confined to disposable server setup.
- Added real-server tests for KV v2 create/read/delete, CAS=0 create-only protection against overwrite, denial outside the granted path (403), and committed-write response-loss recovery: the real Vault accepts the write, the test client discards the successful response, a fresh client reads the committed value, and a retry cannot overwrite it.
- Exact code/test head `5c5e7ec811bc8dcd110a27d85b223e0332805ccd` passed:
  - PostgreSQL 17 integration run `45042568210`: **211 passed, 0 failed, 0 skipped**.
  - Backend/web run `45042568184`: backend **211 passed**, frontend **20 passed**, platform backend and isolated provisioning-agent API Release builds **0 warnings / 0 errors**, dependency audit **0 vulnerabilities**, Vite production bundle succeeded; Pages deployment skipped as expected for a PR.
- Two test-harness corrections were made before this green head: CI now changes the generated CA file's permissions with runner privilege, and the test loads the CA PEM as a certificate-only PEM rather than asking .NET to locate a matching private key. The ephemeral Vault token is explicitly masked.
- This is real Vault API/TLS integration against an ephemeral in-memory dev server, not persistent-storage/HA, server-process-kill recovery, token renewal/revocation, production policy review, key rotation, or backup/restore acceptance. No production Vault was contacted.
- Keep `Provisioning:WorkerEnabled=false`; PR #170 remains Draft/open/unmerged. No production deployment or data changes were made.

### Next continuation order

1. Build a disposable persistent Vault integration environment (not dev-mode in-memory) and add real server-process termination/restart coverage around write commit, readback, CAS conflict and token renewal/revocation boundaries. Preserve state across restart and explicitly initialize/unseal the test server.
2. Validate production-style least-privilege policy, short-lived token renewal/revocation, audit-device evidence, TLS rotation and Vault backup/restore in a controlled staging environment.
3. Complete real OIDC/JWKS staging and emergency signing-key incident rehearsal, deployed mTLS issuance/revocation/rotation, monitoring/alerting, controlled migration 006 deployment/rollback and backup/restore with measured RPO/RTO.
4. Keep the worker disabled and integration PR Draft until all required human review and production gates are complete.

## 11.22 Exact PostgreSQL catalog validation for migration 006 readiness — 2026-11-23

- Follow-up review of the current PR #170 readiness implementation found a fail-closed gap: it checked the names and types of the migration 006 primary key, hash checks and lookup index, but not their complete definitions. A same-named primary key on the wrong column, a weak same-named CHECK, or an index with the wrong key columns could therefore be reported Healthy.
- Created Draft follow-up branch `fix/platform-schema-readiness-catalog-validation-20261123` from the exact PR #170 head `8d2f9716c3d18160330bfab49f2790397c618978`.
- Strengthened `PlatformSchemaReadinessHealthCheck` to verify the primary key definition, validated foreign/check constraints, expected SHA-256 CHECK expressions, and exact non-unique/non-partial/non-expression lookup-index keys/order plus PostgreSQL valid/ready state.
- Added disposable-PostgreSQL failure-mode tests for a same-named primary key on the wrong column, weak creator/request hash checks, an unvalidated hash constraint, and a same-named index on the wrong key.
- Updated the API contract to describe the exact catalog contract. No production schema was changed and no migration was applied to a live database.
- Exact code/test head `f320aff439c796f801dfc31ad32f5588ef5d0e3e` passed its own PR workflows:
  - PostgreSQL 17 integration [run 45137598361](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/45137598361): **216 passed, 0 failed, 0 skipped**.
  - Backend/web [run 45137598355](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/45137598355): backend **216 passed**, frontend **20 passed**, both Release builds **0 warnings / 0 errors**, npm audit **0 vulnerabilities**, Vite production bundle and artifact upload succeeded (artifact `14799919113`, SHA-256 `3bf356bfc754388de1a1c1f2842191a91b53459e18156ae0d114bcb69b8053a2`); Pages deployment was correctly skipped for a PR.
  - These are disposable PostgreSQL/Vault CI results, not a production migration rollout or operational acceptance. The following ledger-only commit is a new exact head and must be revalidated; do not transfer results to it without checking.
- Production blockers remain: controlled migration 006 deployment/rollback and checksum strategy; persistent Vault/process-kill/token/key-rotation and backup/restore; real OIDC/JWKS staging; deployed mTLS; monitoring/alerts; complete business workflows and real hardware/network/operator acceptance. Keep `Provisioning:WorkerEnabled=false`.

### Next continuation order

1. Run PostgreSQL 17 integration and backend/web CI on this exact follow-up head; inspect any failing job logs before changing code.
2. If green, verify the exact follow-up PR head and retain Draft status pending human review and the full release-gate chain.
3. Define the controlled migration 006 deployment/rollback/checksum procedure without deleting durable idempotency records during rollback.
4. Continue production Vault, OIDC/JWKS, mTLS, observability, backup/restore and end-to-end POS/accounting/cafe/game-net acceptance.



## 11.16 Adversarial validation of migration 006 hash constraints — 2026-11-24

- Follow-up to Draft PR #171 static review finding: the previous readiness predicate searched independently for the target column name, regex operator and expected pattern in `pg_get_constraintdef`. A same-named CHECK could satisfy those substring tests while applying the regex to a constant instead of the column.
- New candidate change replaces those substring checks with exact normalized-expression matching against PostgreSQL's deparsed CHECK expression via `pg_get_expr(conbin, conrelid)`. Parentheses/whitespace are normalized, then only the exact target-column regex expression is accepted (with known PostgreSQL text/bpchar/character cast renderings). This rejects extra predicates and regexes applied to constants.
- Added PostgreSQL regression cases for both creator identity hash and request fingerprint where the target column is merely NOT NULL and the valid regex is applied to a constant. Both must fail readiness closed.
- Candidate branch: `fix/platform-exact-hash-check-expressions`, based on exact PR #171 head `8b0564f15a9158b0523a06d892ac134c429dd106`. This is a non-destructive follow-up; no existing branch was rewritten.
- Validation status at creation: not yet run. Do not claim the new predicate or tests pass until the exact candidate SHA completes PostgreSQL 17 and backend/web workflows. Review generated PostgreSQL expression formatting if the strict predicate rejects the valid migration expression.
- No production migration, merge, deployment or worker activation. Keep `Provisioning:WorkerEnabled=false`.
- Next: inspect the candidate diff, run exact-head CI, fix any PostgreSQL catalog rendering mismatch, and obtain independent review before integration.



## 11.23 Exact-head validation completed for migration 006 expression hardening — 2026-11-24

- Candidate branch: `fix/platform-exact-hash-check-expressions`; exact application/test head: `5cb7190df56fdd50e58e1e6c359da787a13600ac`.
- PostgreSQL 17 integration [run 45263341344](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/45263341344): completed successfully on this exact SHA. The job completed the PostgreSQL-backed provisioning and tenant-schema integration test command and all teardown steps.
- MG Business Web backend/web [run 45263341410](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/45263341410): completed successfully on this exact SHA. Backend Release build, isolated provisioning-agent API build, backend unit tests, dependency audit, frontend unit tests, production bundle and build-artifact upload all succeeded. GitHub Pages deploy steps were skipped as expected for a pull request.
- The implementation diff was re-inspected: only `PROJECT_CONTROL.md`, `PlatformSchemaReadinessHealthCheck.cs` and `PlatformSchemaReadinessHealthCheckTests.cs` differ from PR #171's exact head. The two constant-operand regression tests are present.
- PR #172's validation description and Issue #82 engineering log were updated with these exact-head results. PR #172 remains Draft and unmerged.
- Still not evidenced on this stacked candidate: Windows installer/package CI, CodeQL, formatting and NuGet audit. PR #159 contains the branch-independent trigger correction and passed its own exact-head checks, but is not merged because its required independent human approval and main ruleset checks remain outstanding. Do not transfer PR #159's results to this candidate SHA.
- No production migration, deployment, merge or worker activation. Keep `Provisioning:WorkerEnabled=false`.

### Next continuation order

1. Obtain the required independent human review for PR #159; do not bypass the main ruleset or self-approve.
2. After an authorized merge of #159, synchronize the stacked base non-destructively and rerun Windows CI/package, CodeQL, formatting and NuGet audit on the exact candidate head.
3. Obtain independent review of PR #172 and its parent #171/#170 stack; preserve Draft/unmerged state until all gates and parent-stack requirements are satisfied.
4. Continue production-only gates that require authorized staging access: migration 006 deployment/rollback/checksum rehearsal, real OIDC/JWKS and emergency key-rotation drill, persistent Vault recovery/key rotation/backup, deployed mTLS lifecycle, monitoring/alerting and backup/restore with measured RPO/RTO.
5. Keep `Provisioning:WorkerEnabled=false`; do not claim MG Business Web production readiness while identity, tenant isolation, business POS/accounting modules, operations and pilot acceptance remain incomplete.


## 11.24 Adversarial rejection of extra predicates in migration 006 hash checks — 2026-11-24

- Draft PR #173 adds two PostgreSQL regression tests to the exact-expression readiness hardening from PR #172:
  - Creator hash CHECK: the expected `creator_identity_hash ~ '^[0-9a-f]{64}$'` expression combined with an additional `creator_identity_hash IS NOT NULL` predicate must report readiness Unhealthy.
  - Request fingerprint CHECK: the equivalent extra-predicate expression for `request_fingerprint` must report readiness Unhealthy.
- The change is test-only: `backend/tests/Mg.Platform.Api.Tests/PlatformSchemaReadinessHealthCheckTests.cs`, +26 lines, with no production code or migration modification. The reviewed PR patch contains exactly these two tests.
- Exact code/test SHA `11ff53f7a75afe728ceab4e93401c37b267c0a65` passed both available exact-head workflows:
  - PostgreSQL 17 tenant provisioning/schema integration run [45264611580](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/45264611580): workflow and `postgres-integration` job completed successfully; PostgreSQL TLS setup, disposable Vault startup, integration test command and teardown all succeeded.
  - MG Business Web backend/web run [45264611242](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/45264611242): backend Release build, isolated provisioning-agent API build, backend unit tests, dependency audit, frontend unit tests, production bundle and artifact upload all succeeded. GitHub Pages deploy steps were skipped as expected for a pull request.
- These results validate the two new regression tests and the available stacked backend/web + PostgreSQL workflows only. Windows installer/package CI, CodeQL, formatting and dedicated NuGet audit are not evidenced on this stacked candidate; PR #159 still requires independent human approval before its trigger correction can be integrated.
- PR #173 remains Draft/open/unmerged and stacked on Draft PR #172. No production migration, deployment, merge or worker activation occurred. Keep `Provisioning:WorkerEnabled=false`.
- Next: obtain the required independent review for PR #159; after an authorized merge, synchronize the candidate stack non-destructively and rerun the wider quality gates on exact heads. Continue controlled migration 006 deployment/rollback/checksum rehearsal, persistent Vault recovery/key rotation/backup, real OIDC/JWKS staging, deployed mTLS, observability, backup/restore and end-to-end business workflow acceptance. Do not claim production readiness.


## 11.25 Exact foreign-key semantics in migration 006 readiness — 2026-11-24

- Follow-up to PR #173's adversarial CHECK-expression tests. Static catalog review found the readiness query verified the FK columns, target table and `ON DELETE RESTRICT`, but did not verify the migration's default `ON UPDATE NO ACTION`, `MATCH SIMPLE`, and non-deferrable semantics. A same-named FK with `ON UPDATE CASCADE` or deferred enforcement could therefore pass readiness despite differing from migration 006.
- New Draft candidate branch `fix/platform-readiness-exact-fk-semantics`, based on exact PR #173 head `abac6998c4ffb3420b2d48efe0b0c13b7e6608af`.
- The readiness catalog predicate now requires `confupdtype = 'a'`, `confmatchtype = 's'`, `condeferrable = false`, and `condeferred = false`, in addition to the existing exact FK column mapping, `ON DELETE RESTRICT`, and validation state.
- Added two PostgreSQL adversarial tests: same-named FK with `ON UPDATE CASCADE`, and same-named FK declared `DEFERRABLE INITIALLY DEFERRED`; both must report readiness Unhealthy.
- Validation is pending on the new candidate SHA. No test/build/CI result is claimed yet. No migration, production database, existing branch or main branch was changed. Keep PR Draft/unmerged and `Provisioning:WorkerEnabled=false`.
- Next: run PostgreSQL 17 and backend/web workflows on the exact candidate head; inspect any failures and correct only evidence-backed issues. Wider Windows packaging, CodeQL, formatting and dedicated NuGet audit remain gated by the independent human approval required for PR #159.


## 11.26 Exact-head CI passed for migration 006 foreign-key semantics — 2026-11-24

- Draft PR #174 candidate head: `48e5be508b3b1712c4dedf2630b4f525fe04b598`, based on PR #173 exact head `abac6998c4ffb3420b2d48efe0b0c13b7e6608af`.
- PostgreSQL 17 tenant provisioning/schema integration workflow [45332317439](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/45332317439): completed successfully. The PostgreSQL integration job, disposable Vault setup/teardown and container cleanup all succeeded.
- MG Business Web backend/web workflow [45332317380](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/45332317380): completed successfully. Backend Release build, isolated provisioning-agent API build, backend unit tests, dependency audit, frontend unit tests, production bundle and build artifact upload succeeded. GitHub Pages deploy/upload steps were skipped as expected for a pull request.
- The tested change requires migration 006's FK semantics exactly: `ON DELETE RESTRICT`, default `ON UPDATE NO ACTION`, `MATCH SIMPLE`, validated and non-deferrable. Added PostgreSQL negative tests for `ON UPDATE CASCADE` and `DEFERRABLE INITIALLY DEFERRED`; both test cases are included in the successful PostgreSQL workflow.
- These are disposable CI checks only. They do not prove production migration rollout/rollback, backup/restore, Vault operations, real OIDC/JWKS, deployed mTLS, monitoring or operator acceptance.
- PR #174 remains Draft/open/unmerged. No production migration/deployment and no worker activation. Keep `Provisioning:WorkerEnabled=false`.
- Remaining integration gates: PR #159 still needs required independent human approval for its workflow-trigger correction; stacked Windows packaging, CodeQL, formatting and dedicated NuGet audit are not evidenced on this candidate. Parent PRs #170–#173 remain part of the dependency/review chain.
- Next: inspect the next highest-priority unblocked milestone in the live ledger and continue on a separate reviewable branch; do not merge this PR based only on the two green workflows.


## 11.27 Prevent temporary-schema shadowing in migration 006 readiness — 2026-11-24

- New Draft candidate branch `fix/platform-readiness-schema-qualified-catalog`, based on PR #174 ledger head `10592c0f1bf7eae4b906fbe870d09329d445369c`.
- Security review found the readiness query resolved registry, outbox, idempotency table and lookup-index names with unqualified `to_regclass`. PostgreSQL implicitly searches the session's temporary schema before ordinary schemas for relation names. A same-named temporary relation can therefore shadow the permanent migration object during readiness evaluation.
- The query now resolves each required object explicitly in `current_schema()`, preventing a session-local temporary table/index from satisfying migration readiness. The fixture's configured application schema remains supported; this does not hard-code `public`.
- Added a PostgreSQL regression test that drops the durable idempotency table, creates a structurally matching temporary table and lookup index, and requires readiness to remain Unhealthy.
- Validation is pending. No migration, production database, existing branch or main branch changed. PR #174's exact tested code head was `48e5be508b3b1712c4dedf2630b4f525fe04b598`; the later ledger-only head `10592c0f1bf7eae4b906fbe870d09329d445369c` has its own exact-head CI running. Do not transfer results between those commits.
- Keep all PRs Draft/unmerged and `Provisioning:WorkerEnabled=false`. Next: run PostgreSQL 17 and backend/web CI on this exact branch head; if the test demonstrates the issue, inspect the SQL/catalog behavior and preserve fail-closed semantics.


### Test-fixture session integrity note

The temporary-shadow regression creates the fake temporary relation and invokes the readiness health check on the same open Npgsql connection. This is intentional: PostgreSQL temporary relations are session-scoped, so creating them through a separate short-lived connection would not exercise name shadowing in the health-check session. The test drops the durable idempotency table first and asserts readiness remains Unhealthy.


### First exact-head CI failure and corrective commit — 2026-11-24

- Initial candidate head `216a22c43da88c2d626124e9889dc4ef995995c1` failed both available workflows before executing tests. The exact compiler errors were CS1061 for `DatabaseFacade.OpenConnectionAsync` and `GetDbConnection` in the new regression test; the test file lacked the `Microsoft.EntityFrameworkCore` namespace import that exposes these extension methods.
- Corrected by adding only `using Microsoft.EntityFrameworkCore;` to `PlatformSchemaReadinessHealthCheckTests.cs`.
- Corrective commit: `cb5b590517cfb20b3c30fdcb46ee259131d6e158`. This does not change production behavior or weaken/remove tests.
- The initial failed workflows are preserved as failure evidence: PostgreSQL 17 run `45332903219`; backend/web run `45332903556`. Do not report the candidate as validated until both workflows complete successfully on the corrective head.
- Next: inspect exact-head CI for `cb5b590517cfb20b3c30fdcb46ee259131d6e158`; fetch logs and correct any remaining test/SQL issue. PR #175 stays Draft/unmerged; `Provisioning:WorkerEnabled=false`.

### Temporary-schema shadow regression fixture correction — 2026-11-24

- Exact PR #175 head `9e103705c5785dc8eab43df69d12a4fc6993ed3e` failed both configured workflows: PostgreSQL 17 run [45333098359](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/45333098359) and backend/web run [45333098409](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/45333098409).
- Both failures reached the test suite and reported one failed test (`222 passed, 1 failed, 0 skipped`): `ReadinessFailsClosedWhenTemporaryTableShadowsMissingIdempotencyMigration`. PostgreSQL error `42P16: constraints on temporary tables may reference only temporary tables`.
- Root cause is the test fixture, not the production readiness query: its temporary idempotency table attempted to define a foreign key to the permanent `business_tenants` table, which PostgreSQL forbids.
- New non-destructive child branch `fix/platform-temp-shadow-test-fixture-20261124` starts from exact PR #175 head `9e103705c5785dc8eab43df69d12a4fc6993ed3e`. The corrected regression creates temporary shadow relations for `business_tenants`, `tenant_provisioning_jobs`, and `business_creation_requests`, with the temporary idempotency table referencing the temporary tenant table. It also explicitly asserts that unqualified relation/index lookup resolves to the `pg_temp` objects before running the readiness check.
- The health check must still return Unhealthy because the durable idempotency table is absent from `current_schema()`. This preserves the intended regression: a fully matching session-local temporary schema must not satisfy durable migration readiness.
- No production code, migration, production database, parent branch, or `main` was modified. No assertion was removed or weakened. Exact-head validation on the corrected candidate is pending; do not transfer results from earlier commits.
- Keep PRs Draft/unmerged and `Provisioning:WorkerEnabled=false`. Next: open a focused stacked PR, run PostgreSQL 17 and backend/web workflows on its exact head, inspect complete logs, then continue toward the wider Windows installer, CodeQL, formatting and NuGet gates after the required independent review of PR #159 and safe stack synchronization.

### Corrected temporary-schema fixture candidate — exact head pending CI — 2026-11-24

- Follow-up commit `a2b1e7779c96cddb39a2e3a47452a6729c573477` reconstructs the test file from the verified PR #175 base and replaces only the invalid temporary-table fixture. The final file remains 20 tests and 336 lines; the earlier candidate file was not used for validation and is superseded.
- The corrected fixture creates temporary `business_tenants`, `tenant_provisioning_jobs`, and `business_creation_requests` relations. Its FK references the temporary tenant relation, so PostgreSQL's temporary-table FK restriction is respected. An explicit catalog-resolution assertion proves all four unqualified names (three tables and the lookup index) resolve to `pg_temp` before the health check runs.
- Exact candidate head after the correction: `a2b1e7779c96cddb39a2e3a47452a6729c573477`. PostgreSQL 17 and backend/web CI are not yet evidenced for this SHA.
- Keep the candidate Draft/unmerged. Do not treat the superseded malformed test-file revision or the failing parent runs as validation. No production code or migration changed.


### Temporary-schema shadowing fix — exact parent-PR validation — 2026-11-24

- Draft PR #175 current code head `74ff1235eb797558de09a72af1ae7f5b50b46205` was validated in the parent PR context after the corrected fixture was safely fast-forwarded from child PR #176; no force-push or history rewrite was used.
- PostgreSQL 17 provisioning/schema workflow [45349468740](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/45349468740): completed successfully. The PostgreSQL integration job, disposable Vault lifecycle and container cleanup all succeeded; the PR body records **223 passed, 0 failed, 0 skipped**.
- MG Business Web workflow [45349468713](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/45349468713): completed successfully. Backend tests **223 passed, 0 failed, 0 skipped**; backend Release and isolated provisioning-agent API builds, dependency audit, frontend tests, production bundle and artifact upload succeeded. Pages deployment was skipped as expected for a PR.
- The regression explicitly confirms temporary tables/index shadow unqualified lookups in the same PostgreSQL session, while the readiness query requires durable objects in `current_schema()` and remains Unhealthy when the durable idempotency table is absent.
- These results validate disposable CI only. Windows installer packaging, CodeQL, formatting and dedicated NuGet audit remain unevidenced on this stacked candidate; PR #159 still requires independent human approval. Production migration/rollback, backup/restore, Vault/key lifecycle, OIDC/JWKS, deployed mTLS, observability and operator acceptance remain open.
- PR #175 remains Draft/open/unmerged. No production migration/deployment or worker activation. Keep `Provisioning:WorkerEnabled=false`.
- Next: continue with the highest-priority security gap in Issue #157 by creating a non-destructive integration candidate against the current parent stack; preserve PR #155's maintenance-lock, ACL and fault-injection protections. Do not rebase/force-push PR #158 or merge any candidate.


## 11.16 Exact-head validation — PR #175 — 2026-11-24

- Draft PR #175 current implementation and documentation HEAD `9a891509517687f4e5d4a18fc1123112289bed72` was validated by both workflows on that exact SHA.
- PostgreSQL 17 workflow `45358840912`: completed successfully; all jobs and steps passed.
- Backend/web workflow `45358840918`: completed successfully. Backend Release build, isolated provisioning-agent API Release build, backend unit tests, frontend unit tests, dependency audit, Vite production bundle and artifact upload all passed. GitHub Pages deployment steps were skipped as expected for a pull request.
- The run summary available from the workflow API does not expose test totals or artifact digest in this ledger entry; no numerical totals are inferred.
- PR #175 remains Draft/open/unmerged. No production migration, deployment or worker activation occurred. Keep `Provisioning:WorkerEnabled=false`.
- Next: continue the open security work tracked by issue #157 from the live parent branch, preserving all existing readiness, ACL, maintenance-lock and fault-injection protections; then run exact-head PostgreSQL and backend/web CI on the combined candidate.


## 11.18 Checksum-registered migration 006 rollout gate — 2026-11-24

### Verified provisioning/idempotency integration stack
- The current integration chain is non-destructive and remains Draft/unmerged: PR #168 (hard-kill recovery on the current security/lease parent) → PR #169 (worker heartbeat on #168) → PR #170 (Creator idempotency/OIDC/response-loss work on #169) → PRs #171–#175 (progressively exact migration 006 catalog/readiness hardening).
- PR #168 exact HEAD `343a6397049fbb243d166f83169a1d02abed8c1f`: PostgreSQL run `45000864366` passed **161/161**; backend/web run `45000863625` passed backend **161/161**, frontend **20**, both Release builds **0 warnings / 0 errors**, dependency audit **0 vulnerabilities**, and production bundle. Pages deployment skipped for PR.
- PR #169 exact HEAD `19958889c7b337c77a56e6fe8b84b72dc3c210b6`: PostgreSQL run `45001116162` passed **170/170**; backend/web run `45001116221` passed backend **170/170**, frontend **20**, both Release builds **0 warnings / 0 errors**, dependency audit **0 vulnerabilities**, and production bundle. Pages deployment skipped for PR.
- PR #170 exact HEAD `8d2f9716c3d18160330bfab49f2790397c618978`: PostgreSQL run `45042895356` passed **211/211**; backend/web run `45042895351` passed backend **211/211**, frontend **20**, both Release builds **0 warnings / 0 errors**, dependency audit **0 vulnerabilities**, and production bundle. Pages deployment skipped for PR.
- PRs #171–#175 then added progressively stronger migration 006 readiness semantics, exact hash expressions, adversarial extra-predicate rejection, exact foreign-key semantics and temporary-schema-shadow protection. Their latest exact-head workflows are green as recorded in the respective PRs. PR #175 exact HEAD `fd548d80947ca66cc8726df159f60493b525a6d2` passed PostgreSQL `45359326284` and backend/web `45359326129`.
- PR #159 still has no independent approving human review. Windows packaging, CodeQL, formatting and dedicated NuGet audit must be run on a synchronized release candidate after authorized review; do not infer those checks from the stacked backend/web workflows.

### New checksum gate candidate
- Draft child branch `fix/platform-migration-006-checksum-gate-20261124` is based on the exact PR #175 HEAD above. It adds:
  - `platform_schema_migrations` to migration 006, storing a migration ID, immutable SHA-256 checksum and application timestamp.
  - A reviewed SHA-256 manifest and `backend/scripts/apply-platform-migration-006.sh`, which checks the exact SQL artifact before execution, requires PostgreSQL TLS `verify-full`, preflights and rejects a conflicting existing checksum before SQL, applies with stop-on-error, inserts only when no checksum row exists, and reads back without overwriting conflicts. The PostgreSQL workflow also runs `bash -n` against the migration runner.
  - A runtime checksum constant and readiness validation of the ledger table's exact columns, primary key and validated lowercase-hex constraint, plus the exact durable migration ID/checksum.
  - A schema-qualified checksum read to prevent a session-local temporary relation from shadowing the durable ledger.
  - PostgreSQL tests for the reviewed SQL/manifest hash, missing ledger, missing record, mismatched checksum and temporary-ledger shadowing. Existing catalog semantics tests remain in place.
  - `backend/docs/PLATFORM_MIGRATION_RUNBOOK.md`, API contract/README updates, and ADR-010 documenting apply, verification and forward-only rollback behavior.
- Code/test and workflow HEAD `25420542cec00a3a6a70234b4bd2ddc710b03b34` includes the checksum gate, PostgreSQL regressions and shell syntax CI. Acceptance/runbook documentation was updated afterward. Exact current PR HEAD `a78cd92fccada699b78943492fb1a6f89b89a58a` passed PostgreSQL workflow `45366624334` (**228 passed, 0 failed, 0 skipped**) and backend/web workflow `45366624421` (backend **228 passed**, frontend **20 passed**, backend and isolated agent API Release builds **0 warnings / 0 errors**, dependency audit **0 vulnerabilities**, production bundle and artifact upload succeeded; shell syntax validation passed; Pages deployment skipped as expected for PR).
- API startup never applies migrations. Manual application without the runner's checksum registration leaves readiness Unhealthy. The runner does not overwrite an existing checksum; schema correction requires a separately reviewed forward migration. API rollback preserves migration 006 and all idempotency rows.
- No production database was touched and no migration was applied. Keep PR Draft/unmerged and `Provisioning:WorkerEnabled=false`.

### Next
1. Open/maintain a Draft child PR against PR #175 and run PostgreSQL 17 plus backend/web CI on the exact final PR HEAD; inspect logs for all new tests and build steps.
2. If green, add Windows packaging, CodeQL, formatting and NuGet checks only after PR #159 is independently reviewed and the stack can be safely synchronized.
3. Continue production OIDC/JWKS, real ingress/socket response-loss, Vault/key rotation/backup-restore, deployed mTLS, observability, tenant backup/restore and pilot acceptance. Do not mark production readiness until every release gate has evidence.


## 11.19 Provisioning worker low-cardinality observability — 2026-11-24

- Draft child branch `feat/provisioning-worker-observability-metrics-20261124` is based on exact PR #178 HEAD `196ecc3808224469bcbfc7440efcefd0df0ce1d4`; it must not merge independently of the migration-checksum/readiness stack.
- Added `TenantProvisioningWorkerMetrics` using `System.Diagnostics.Metrics`, meter `MG.Platform.Provisioning` v1.0.0:
  - counters: `mg.provisioning.jobs.claimed`, `mg.provisioning.jobs.attempts`, `mg.provisioning.lease.renewals`, `mg.provisioning.worker.iteration.failures`;
  - histogram: `mg.provisioning.jobs.duration` in seconds;
  - only bounded `outcome` tags are emitted. Tenant IDs, job IDs, worker IDs, exception messages, database names and credential data are not metric tags.
- Worker claim, attempt outcome/duration, lease-renewal success/loss/error and worker-loop failure paths are instrumented. No business logic, lease fencing, retry policy or activation boundary is weakened.
- Added a PostgreSQL-backed `MeterListener` regression that waits for an actual heartbeat metric, verifies claim/attempt/duration/renewal measurements and asserts the only tag key is `outcome`.
- Added `backend/docs/PROVISIONING_WORKER_OBSERVABILITY.md` and updated the operations acceptance checklist. The API does not yet configure an exporter or expose a metrics endpoint; approved collector deployment, dashboard, alert delivery, on-call ownership and production-host/staging validation remain open.
- Code/test HEAD `4c6f2288cf860c3d1d1a020721c539ddd461c439`; exact current PR HEAD `f544dfeac0b42dd195e949dc3f510ba01ce37ac6` passed PostgreSQL workflow `45368452176` (**229 passed, 0 failed, 0 skipped**) and backend/web workflow `45368452137` (backend **229 passed**, frontend **20 passed**, backend and isolated agent API Release builds **0 warnings / 0 errors**, dependency audit **0 vulnerabilities**, production bundle and artifact upload successful; Pages deployment skipped as expected for PR).
- Keep PR Draft/unmerged and `Provisioning:WorkerEnabled=false`. Instrumentation is not approval to deploy or enable provisioning.

### Next
1. Open a Draft child PR against PR #178 and run exact-head PostgreSQL and backend/web CI; inspect the metrics regression and all prior stack tests.
2. If green, add collector/exporter configuration only with an approved telemetry endpoint and credentials; then add staging alert-delivery and canary evidence.
3. Continue the remaining Vault/key lifecycle, backup/restore, deployed mTLS, real ingress response-loss, tenant restore and operator acceptance gates. No production-readiness claim until every gate is evidenced.


## 11.20 Exact-head revalidation and security integration handoff — 2026-11-26

- Re-fetched the live open-PR list and exact heads. PR #178 remains Draft/open/unmerged at `196ecc3808224469bcbfc7440efcefd0df0ce1d4`; PR #179 remains Draft/open/unmerged at `49681ccdd092b7b9736b258a6314d18780586b26`; PR #177 remains Draft/open/unmerged at `6fc2542c754f5c1d158f0d7fe8626c7860412ab7`. PR #179 is stacked on #178; #177 is separately stacked on #155.
- Live exact-head workflow API recheck: PR #178 PostgreSQL run `45366992992` and backend/web run `45366993127` both completed successfully on SHA `196ecc3808224469bcbfc7440efcefd0df0ce1d4`. PR #179 PostgreSQL run `45369242535` and backend/web run `45369242662` both completed successfully on SHA `49681ccdd092b7b9736b258a6314d18780586b26`. PR #177 PostgreSQL run `45361418780` and backend/web run `45361418776` both completed successfully on SHA `6fc2542c754f5c1d158f0d7fe8626c7860412ab7`. These are workflow-level success checks; do not infer Windows packaging, production deployment or staging acceptance from them.
- PR #179's final test-fixture cleanup commit is included in its current exact-head green result. Earlier ledger evidence for #179 at `f544dfeac0b42dd195e949dc3f510ba01ce37ac6` is superseded by current SHA `49681ccdd092b7b9736b258a6314d18780586b26`.
- Security handoff: PR #177's shared verifier is green on its own #155-based stack, but must not be copied wholesale over the newer #179 tree. The live #179 branch has newer provisioning handler/resource/store code. Next implementation must be a new non-destructive child branch based on current #179 HEAD, port the verifier into the current method boundaries, preserve all newer handler behavior, ACL/read-only diagnostics, transaction and maintenance-lock semantics, fault-injection seams, worker metrics and migration checksum gate, then add current-parent PostgreSQL regressions and run both workflows on the exact combined HEAD. Do not retarget, rebase, force-push or merge the existing #177/#179 branches.
- No merge, production migration, deployment or worker activation occurred. Keep `Provisioning:WorkerEnabled=false`; release status remains NOT VERIFIED.

### Next
1. Create a child integration branch from exact current PR #179 HEAD and inspect the current source/diff before making any edits; selectively port #177's shared verifier rather than replacing current files with its older snapshots.
2. Add tests against current parent behavior for membership drift at credential persistence, rewrap, phase advancement and tenant-resource mutation; preserve maintenance-lock and fault-injection coverage.
3. Run PostgreSQL 17 and backend/web workflows on the exact final integration SHA; inspect failures and record evidence in the same PR.
4. Continue operational blockers: approved metrics exporter/collector, dashboards/alerts/on-call, Vault token policy and key rotation, backup/restore, deployed mTLS, real ingress response-loss, tenant restore and staging/pilot acceptance.
5. Keep `Provisioning:WorkerEnabled=false`; no merge or deployment without explicit authorization and required human review.


## 11.21 Shared control-role verifier on the current provisioning stack — 2026-11-26

- Draft child PR #184, branch `integration/agent-shared-readiness-current-stack-20261126`, is based on PR #179 branch HEAD `e76673f85029ed467233f2166e1654848dd4d596`. It selectively ports the shared verifier from #177; no older source file was copied over the current stack wholesale.
- Added `AgentControlRoleReadinessVerifier` in Agent Core. The current API readiness method now delegates to it; the current tenant resource provisioner delegates its mutation-boundary role check to it; the operation store invokes the full verifier before credential persistence, credential rewrap, batch key rewrap and phase advancement.
- Preserved current handler/coordinator and provisioning code, current resource mutation flow, the provisioner's transaction and fault-injection seams, and the operation store's existing ACL read diagnostics and maintenance lock. Added a current-parent PostgreSQL regression that injects unexpected role membership after readiness and proves initial tenant resource mutation is refused with no database/tenant roles created.
- Current PR #184 implementation HEAD at initial PR creation: `7489e7759ab460190887a66115e1ce8827294b2e`. PostgreSQL 17 and backend/web workflows have not appeared for this head in the live Actions API yet; exact-head validation is PENDING. Do not claim this integration is green.
- Test coverage still needs explicit current-parent operation-store regressions for membership drift at credential persistence, credential rewrap and phase advancement; the production guards are added, but the #177 restricted-role fixture must be safely ported without replacing newer current-stack tests. Treat these as an active acceptance gap.
- No merge, deployment, migration or worker activation. Keep `Provisioning:WorkerEnabled=false`.

### Next
1. Port the restricted control-role test fixture and drift regressions into current `AgentProvisioningOperationStorePostgresTests` without discarding newer tests; verify cleanup of injected role membership on every failure path.
2. Check PR #184 exact-head workflows when they appear; inspect full logs and correct any failure without weakening assertions.
3. Add exact-head CI evidence to PR #184 and the ledger; retain Draft/unmerged status.
4. Continue production metrics collector/alerts, Vault policy/key lifecycle, backup/restore, deployed mTLS, real ingress response-loss and staging/pilot acceptance. Keep worker disabled.
