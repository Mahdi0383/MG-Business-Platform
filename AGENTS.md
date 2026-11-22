# Repository-wide instructions for AI and human contributors

These rules are mandatory for every future continuation, regardless of chat session, agent, model, or contributor. Read `PROJECT_CONTROL.md` before making changes. Then read the linked architecture, product, API, security, module and roadmap documents relevant to the task.

## Mission and authority
- Build MG Business Web into a complete, secure, reliable, production-operable, modular business platform for multiple industries.
- The target product is a **standalone web/PWA application**. It must not require the legacy Windows application to be installed, running, or online. Existing Windows code may be maintained separately, but it is not a runtime dependency of MG Business Web.
- The product has two distinct cores: private MG Creator Control Plane and isolated Business Operating Plane. Actor hierarchy: MG Creator → Business → Business Users → Business Customers.
- MG Creator is the platform owner control center; tenant owners administer only their own business. Customer identities/records are not staff memberships.
- The data authority is MG central control-plane database → one isolated authoritative database per business → per-user/device cache and durable offline outbox. Local browser storage is never authoritative for money, inventory, permissions, licenses, or final order state.

## Non-negotiable engineering rules
1. **Continue from repository state, not chat assumptions.** Inspect current branch, open PRs, latest commit, workflow results, working files and existing implementation before every change. Do not recreate or overwrite work blindly.
2. **Preserve user requirements.** Update `PROJECT_CONTROL.md` when a requirement is added, changed, implemented, deferred, or rejected. Record important architecture decisions in the relevant ADR/decision log. Do not silently drop requirements.
3. **Work incrementally.** One coherent, reviewable milestone per branch/PR. Never push unrelated changes into a protected branch or force-push/rewrite history. Respect branch protection and required reviews.
4. **Tests are part of the feature.** Add/update unit, integration, security, migration, architecture, frontend and end-to-end tests appropriate to each change. Run the narrow test first, then the relevant full suite/build/audits.
5. **Report evidence honestly.** Never say build/test/security scan/deployment/hardware validation passed unless the exact current commit has a completed successful result. Distinguish passed, failed, running, skipped and not run. A prototype, mock, localStorage demo, stub, or documented design is not a completed production feature.
6. **Never expose secrets.** No real passwords, tokens, private keys, connection strings, customer/financial data, database files, or production exports in Git. Use secret references and environment/secret-manager injection. Redact logs and artifacts.
7. **Security boundaries are server-enforced.** Authenticate and authorize every protected operation; verify tenant, branch, object ownership, role, permission, module entitlement and resource state on every request. Client-supplied IDs and local claims are selectors, never proof of access. Default deny.
8. **Financial correctness is mandatory.** Use decimal/fixed precision and explicit currency; server-calculated totals; immutable posted journals; balanced double-entry; append-only inventory movements; idempotent commands; explicit reversals/refunds; concurrency control. Never silently rewrite financial history.
9. **Tenant isolation is mandatory.** One authoritative database per business with isolated credentials and secret references. No cross-tenant query or connection selection based solely on client input. Cross-tenant analytics must be explicit, minimized and authorized.
10. **Offline is a replica model.** Use a durable per-device cache/outbox; server revalidates current permissions, prices, stock and business rules during replay. Conflicts are visible and recoverable. Do not cache sensitive API/business data in the static service worker.
11. **Localization and usability are first-class.** Persian/Farsi RTL and English LTR; responsive desktop/tablet/mobile; accessible keyboard/touch operation; consistent design system; user-facing strings localized; avoid browser-native alert/confirm/prompt for core workflows.
12. **Modular entitlements.** Module activation comes from server-verified tenant entitlements. Modules declare migrations, APIs, permissions, navigation, events, reports, dependencies and tests. Hiding a menu is not authorization.
13. **Operations are part of completion.** Include migrations, backup/restore, observability, alerting, runbooks, deployment/rollback, rate limits, retention, incident and recovery procedures.
14. Do not claim continuous/background work. A future session starts by reading this file and the project ledger and then performs the next verifiable task.

## Required continuation protocol
At the start of every `continue` request:
1. Read `AGENTS.md` and `PROJECT_CONTROL.md`.
2. Inspect repository default branch, active implementation branch and open PR #105 (or its successor); check exact HEAD and uncommitted changes.
3. Inspect latest GitHub Actions for that exact HEAD; fetch logs for failures. Do not rely on old commit results.
4. Read the current milestone and blockers in `PROJECT_CONTROL.md`; select the highest-priority unblocked task.
5. Implement real code/docs/tests, not merely a progress message. Keep the change scoped and reviewable.
6. Run available checks and create/update a branch and Draft PR. Keep the main product PR Draft until every release gate is met. Never merge without explicit repository authorization and all required checks/reviews.
7. Update the project ledger with commit, files, tests, evidence, blockers and next task. Update PR description to match reality.
8. If a task needs a credential, external account action, hardware, human policy decision, or production environment unavailable to the agent, record it as a clearly named blocker and continue with independent work. Never invent access or pretend it was done.

## Completion definition
A feature is complete only when implementation, authorization, persistence/migrations, error/retry behavior, localization/accessibility, tests, documentation and operational handling are done. The whole platform is ready only after all release gates in `PROJECT_CONTROL.md` and `web/docs/PRODUCT_BLUEPRINT.md` pass on the release candidate, including real deployment and operator acceptance. A green compile alone is not completion.
