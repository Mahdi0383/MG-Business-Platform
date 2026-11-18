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
- Dedicated TLS-enabled PostgreSQL workflow `44412521381` passed on exact PR #154 feature HEAD `096907128c10296a74b277177e548dbe5bfea145`: 108 tests passed, 0 failed, 0 skipped. Full web workflow `44412531836` on the same SHA was still in progress when this note was written; do not treat it as passed until its final conclusion is success.
- Remaining release gates: live production secret-manager delivery and key rotation, crash-injection at each DDL/ledger/Vault boundary, full backup/restore and tenant-isolation acceptance, deployed mTLS certificate/revocation operations, observability/alerting and end-to-end staging. The central provisioning worker remains disabled; PR #154 remains Draft and must not be merged or deployed before these gates pass.

## 12. Change log

- 2026-11-18: Master requirements, architecture, continuation rules, implementation snapshot and production release gates consolidated into this repository governance document.\n- 2026-11-18: Recorded Draft PR #153 for Creator OIDC authentication hardening; its exact-head MG Web workflow subsequently passed.\n- 2026-11-18: Recorded Draft PR #154 adding durable provisioning lease and idempotency schema; exact-head CI was queued. Initial implementation status is based on PR #105 snapshot; revalidate before further work.
- Future sessions must append dated entries with PR/commit, concrete changes, tests/results, blockers and next task. Never erase historical verification; mark superseded evidence clearly.
