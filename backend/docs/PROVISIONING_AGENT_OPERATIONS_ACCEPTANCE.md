# MG Provisioning Agent — Operations Acceptance Checklist

Status: **release blocked** until every mandatory gate below has dated evidence. This checklist supplements the implementation contract and runbook; it does not authorize production enablement.

## 1. Isolation and identity
- [ ] Deploy the agent as a separate service on a private network. Do not route it through the customer-facing ingress.
- [ ] Restrict inbound traffic to the MG Platform API workload identity and the configured mTLS client-certificate allowlist.
- [ ] Verify the server certificate chain, revocation status, SAN, validity, Server Authentication EKU and Digital Signature usage from the API container.
- [ ] Verify the client certificate chain, validity, Client Authentication EKU, Digital Signature usage and exact allowlist match.
- [ ] Confirm redirects and cookies are disabled on both the API-to-agent and Vault token HTTP clients; require bounded connect/request timeouts so bearer tokens are never forwarded to redirect targets.
- [ ] Confirm request-size limits and per-certificate rate limits are active at the deployed ingress and application layers.

## 2. PostgreSQL separation and least privilege
- [ ] Verify the control role has no incoming role memberships and no outgoing membership except PostgreSQL 17's automatic bootstrap ADMIN edge to UUID-derived, marker-verified tenant owner/application roles (superuser grantor, ADMIN=true, INHERIT=false, SET=false) and the provisioner's exact SET-only memberships to marked tenant-owner roles (control-role grantor, ADMIN=false, INHERIT=false, SET=true). Readiness must fail closed for every other membership, including unmarked bootstrap targets.
- [ ] Verify each tenant runtime application role has no outgoing membership and only the unavoidable incoming bootstrap ADMIN edge from the isolated control identity (superuser grantor, ADMIN=true, INHERIT=false, SET=false); it must have no SET/INHERIT path.
- [ ] Verify PUBLIC has no EXECUTE on any function in `mg` and owner-scoped default privileges prevent future `mg` functions from becoming PUBLIC-executable.
- [ ] Confirm the agent re-runs the complete live readiness/least-privilege check immediately before every provisioning operation; a previously healthy process must not continue after control-role privilege drift.
- [ ] Confirm credential-rewrap maintenance also re-runs the live readiness/least-privilege check before mutating the protected provisioning ledger.
- [ ] Create a dedicated agent-control database. It must not be the platform registry database or any tenant database.
- [ ] Apply the agent ledger migration using a separate migration identity.
- [ ] Configure the runtime control identity as non-superuser, NOINHERIT, non-replication and NOBYPASSRLS, with only the reviewed CREATEDB/CREATEROLE capabilities.
- [ ] Revoke all database privileges on the agent control database from PUBLIC (including PostgreSQL's default CONNECT and TEMP); grant the runtime control identity only CONNECT. Verify PUBLIC has no CONNECT, CREATE or TEMP privilege.
- [ ] Ensure the runtime control identity does not own the control database, mg_agent schema or ledger table.
- [ ] Grant only CONNECT, schema USAGE and SELECT/INSERT/UPDATE on the operation ledger. Do not grant TEMP, DELETE, TRUNCATE, schema CREATE or object ownership.
- [ ] Use SSL Mode=VerifyCA or VerifyFull with a trusted CA. Require without certificate validation is not accepted.
- [ ] Capture the live /health/ready response and the database privilege audit as release evidence.

## 3. Credential protection and recovery
- [ ] Inject the AES-256 key ring from a secret manager; do not store keys in source control, container layers or ordinary deployment variables.
- [ ] Verify credential envelopes use authenticated encryption and bind tenant UUID, idempotency key, database name and key ID.
- [ ] Perform a key-rotation rehearsal: add a new key, make it current, rewrap all rows, verify the old-key row count reaches zero, then retire the old key.
- [ ] Verify backups cover the agent ledger and key-version metadata. Demonstrate restore using the corresponding retained key material.
- [ ] Confirm logs, traces, metrics and error responses contain no passwords, connection strings, access tokens, SQL parameter values or decrypted credential payloads.

### Vault KV v2 response-loss and create-only CAS evidence (partial)

Exact code/test SHA `62e05f99df264e55f2374270143b5f36b64aff38`:
- PostgreSQL 17 integration [run 44957225349](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44957225349): **173 passed, 0 failed, 0 skipped**.
- Backend/web [run 44957225288](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44957225288): backend tests **173 passed**; isolated agent API Release build **0 warnings / 0 errors**; frontend tests **20 passed**; dependency audit found **0 vulnerabilities**; production bundle passed; Pages deployment skipped for PR.

Automated tests now simulate Vault committing a KV v2 secret while the HTTP write response is lost, and separately simulate a lost readback response after the write. In both cases, a retry reads and reuses the exact stored tenant connection string, does not provision a second database credential, and does not issue a second secret write. A stateful KV v2 HTTP test server enforces `cas=0`: concurrent create-only writes allow one winner, reject the conflicting write, and preserve the winning credential on subsequent reads.

This is response-loss/CAS adapter evidence using a deterministic test server and PostgreSQL-backed handler fixture. It is not a production Vault deployment, a real Vault process-kill test, a key-rotation rehearsal or a backup/restore drill. Keep the secret-manager production acceptance gates open.

The handler-level tests also cover both write-commit/response-loss and readback-response-loss: a retry reuses the exact credential, does not reprovision and does not issue a second write. The HTTP adapter test enforces KV v2 `cas=0`, proves the first committed value survives a lost response, rejects replacement, and races two concurrent create-only writes so exactly one wins.

## 4. End-to-end fault injection
Run only against disposable PostgreSQL instances and isolated test certificates.
- [ ] Submit the same tenant/idempotency request concurrently; exactly one durable operation and one credential bundle must be created.
- [ ] Replay a completed request after simulating a lost HTTP response; the same application credential must be returned without repeating destructive side effects.
- [ ] Reuse an idempotency key for another tenant and reuse a tenant with a different key; both must be rejected.
- [ ] Terminate the agent immediately after database creation, role creation, schema application and privilege grants. A retry must reconcile safely at every boundary.
- [ ] Corrupt the tenant marker, migration checksum, owner, application role or runtime grants; independent verification must fail closed.
- [ ] Remove or rotate a required secret-manager key; readiness/provisioning must fail without exposing secret material.
- [ ] Expire/revoke the mTLS client certificate and verify the request is rejected before the handler runs.
- [ ] Interrupt the platform worker during provisioning; lease expiry and fencing must prevent a stale worker from completing a reclaimed job.

### Automated hard process-kill evidence (partial boundary coverage)

The disposable PostgreSQL 17 integration suite starts a separate crash-worker process, waits for a named side-effect boundary, forcibly kills the process tree, inspects persisted state and retries through a fresh process context. These are OS process-termination tests, not callback exceptions or graceful cancellation.

Exact-head evidence on `5ad6256b7e5ca9268b35f9a608ea65b8e26b6f3c`:
- PostgreSQL 17 provisioning integration [run 44943851135](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44943851135): **159 passed, 0 failed, 0 skipped**.
- Backend/web [run 44943851128](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44943851128): backend tests **159 passed**; isolated agent API Release build **0 warnings / 0 errors**; frontend tests **20 passed**; dependency audit and production bundle passed; Pages deployment skipped for PR.

The current hard-kill suite covers 23 named boundaries:

**Tenant role and database creation**
1. Owner role creation before role-transaction commit.
2. Application role creation before role-transaction commit.
3. Owner membership setup before role-transaction commit.
4. CREATE DATABASE before immutable marker.
5. PUBLIC database ACL revocation before marker.
6. Immutable marker written before access configuration.

**Database access grants**
7. PUBLIC database ACL re-revocation during access configuration.
8. Application-role CONNECT grant.
9. Control-role CONNECT grant.
10. Complete database access configuration before schema work.

**Tenant schema and runtime grants**
11. Owner readiness before tenant baseline DDL.
12. Tenant baseline committed before runtime grants.
13. PUBLIC schema privilege revocation.
14. PUBLIC table privilege revocation.
15. PUBLIC function privilege revocation.
16. Default PUBLIC function EXECUTE revocation.
17. Runtime schema USAGE grant.
18. Runtime mutable-table privileges grant.
19. Runtime read-only-table privileges grant (final runtime-ready boundary).

**Durable operation ledger**
20. Prepared credential INSERT before transaction commit.
21. Credential rewrap UPDATE before transaction commit.
22. Provisioning phase UPDATE before transaction commit.
23. Runtime grants completion before the coordinator advances its durable phase.

For database-access boundaries, tests inspect PUBLIC ACL and application/control CONNECT state. For runtime-grant boundaries, tests independently verify whether the runtime is ready at that exact intermediate state. Every case then retries and proves final runtime verification succeeds. Ledger cases verify transaction rollback leaves the previous durable state intact and a fresh attempt completes idempotently.

This remains partial acceptance evidence, not a claim that every secret-manager operation, every external platform side effect or every production deployment scenario has been exercised. Keep the master fault-injection checkbox above open until production secret-manager behavior, external integrations and operational gates are evidenced.

### Coordinator idempotency and tenant concurrency evidence

The PostgreSQL suite also verifies eight concurrent duplicate requests for one tenant/idempotency key generate and persist one credential, rejects cross-tenant idempotency-key reuse, and proves two different tenant provisioning operations can both enter their external provisioning stage concurrently while the first tenant remains blocked. This validates tenant-scoped locks do not accidentally serialize independent tenants. Exact latest PostgreSQL run: [44957225349](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44957225349), **173 passed, 0 failed, 0 skipped**.

The top-level end-to-end checkbox below remains open: it requires exercising the deployed Platform API request path and its durable registry/queue, not only the coordinator and operation-store integration tests.

### Platform business registry and outbox concurrency evidence (service-level)

Exact code/test SHA `d04368366b4f12230b6091a09b705fb000b3d87f`:
- PostgreSQL 17 integration [run 44957701458](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44957701458): **176 passed, 0 failed, 0 skipped**.
- Backend/web [run 44957701527](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44957701527): backend tests **176 passed**; isolated agent API Release build **0 warnings / 0 errors**; frontend tests **20 passed**; dependency audit and production bundle passed; Pages deployment skipped for PR.

New PostgreSQL integration tests exercise `BusinessRegistryService` with independent DbContexts:
- 16 simultaneous requests for the same slug produce exactly one business and one provisioning outbox job; the other 15 return a slug conflict.
- 12 simultaneous requests for distinct slugs produce 12 independent businesses and 12 outbox jobs with unique idempotency keys.
- A forced outbox insert failure rolls back the business row; no orphan registry record remains.

This validates service-level concurrency and the atomic business/outbox persistence invariant. The HTTP endpoint/authentication pipeline and true client response-loss/replay through the Platform API remain untested; keep the top-level end-to-end API acceptance gate open.

### Platform business HTTP endpoint integration evidence (test authentication scheme)

Exact code/test SHA `cd057ef7b35617466d70749211881854417226b5`:
- PostgreSQL 17 integration [run 44958557297](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44958557297): **179 passed, 0 failed, 0 skipped**.
- Backend/web [run 44958557395](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44958557395): backend tests **179 passed**; isolated agent API Release build **0 warnings / 0 errors**; frontend tests **20 passed**; dependency audit and production bundle passed; Pages deployment skipped for PR.

The tests host the real `PlatformEndpoints.MapPlatformEndpoints` route group through ASP.NET Core TestServer and PostgreSQL:
- 16 simultaneous `POST /api/v1/platform/businesses/` requests for the same slug return exactly one HTTP 201 and fifteen HTTP 409 responses, with exactly one business row and one durable provisioning job.
- Missing authentication returns HTTP 401; an authenticated principal with a non-platform role returns HTTP 403; invalid slug returns HTTP 400. None writes registry or queue rows.
- The route's real authorization policy and rate-limiting middleware are in the test pipeline; a test-only authentication handler supplies role claims. This does not test OIDC signature/JWKS validation or production issuer/audience behavior.
- A 31-request same-client burst verifies the configured fixed-window rate limiter allows exactly 30 requests (201) and rejects the 31st (429), with exactly 30 business/outbox pairs persisted.

The registry endpoint tests now cover route-level concurrency, role/validation boundaries and the client idempotency contract. The API requires a UUID `Idempotency-Key`; same-key/same-creator/same-normalized-payload retries replay the original business resource, while changed payload or creator identity returns HTTP 409. The exact evidence is recorded below. Production OIDC signature/JWKS validation and staging with the real identity provider remain open.

### Durable Creator API idempotency evidence

Migration `backend/database/migrations/006_business_creation_idempotency.sql` creates a globally unique UUID idempotency record bound to one-way SHA-256 hashes of the validated OIDC issuer+subject and normalized request. It is committed in the same EF Core transaction as the business row and provisioning outbox job. Records are retained for the business lifetime.

The `POST /api/v1/platform/businesses/` endpoint now requires a non-empty UUID `Idempotency-Key` header and validated `iss` + `sub` claims. Same key + same creator + same normalized name/slug replays HTTP 201 for the same business ID and Location, with `Idempotency-Replayed: true`. A changed payload or creator identity returns HTTP 409. The API contract is documented in `backend/docs/PLATFORM_API_CONTRACT.md`; ADR-009 records the decision.

Exact current PR head `910d343a0e0cbf9679a0f1981ab46bcc7fab646e` (acceptance documentation only after the validated implementation head):
- PostgreSQL 17 integration [run 44961023483](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44961023483): **190 passed, 0 failed, 0 skipped**.
- Backend/web [run 44961023493](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44961023493): backend tests **190 passed**; isolated agent API Release build **0 warnings / 0 errors**; frontend tests **20 passed**; dependency audit and production bundle passed; Pages deployment skipped for PR.

Coverage includes normalized same-key replay, changed-payload and changed-creator conflicts, 16 concurrent identical idempotency requests yielding one business/job and 15 replays, TestServer response-body failure after commit followed by successful same-key retry, missing/empty key rejection, and the prior 16-way slug race, 31-request rate-limit burst, 401/403/400 authorization/validation cases.

This is PostgreSQL/TestServer evidence, not production IdP/JWKS validation or an actual socket/ingress disconnect. A dedicated TestServer middleware throws during response-body delivery after the business/outbox/idempotency transaction commits; the client observes transport failure, then retries with the same key and receives the same business ID with `Idempotency-Replayed: true`. Clients must persist and reuse the key; no client may assume a request without the header will be accepted. Production OIDC, deployment of migration 006, response-loss fault injection through the production ingress, and staging remain release gates.

The API readiness endpoint now includes `PlatformSchemaReadinessHealthCheck`: it verifies the registry, outbox and business-creation idempotency table, required hash/key columns and the named primary-key constraint. Missing or incomplete migration 006 returns Unhealthy. PostgreSQL tests prove healthy readiness with the schema and fail-closed readiness after dropping the idempotency table.


### Creator business idempotency and response-loss replay evidence

Exact code/test SHA `0775d7c774a0ceed860b5966679fe8fe1050a584`:
- PostgreSQL 17 integration [run 44959390861](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44959390861): **186 passed, 0 failed, 0 skipped**.
- Backend/web [run 44959390734](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44959390734): backend tests **186 passed**; isolated agent API Release build **0 warnings / 0 errors**; frontend tests **20 passed**; dependency audit found **0 vulnerabilities**; production bundle passed; Pages deployment skipped for PR.

The business registry now persists the idempotency key, a one-way hash of the validated OIDC issuer/subject and a SHA-256 fingerprint of the normalized name/slug in the same PostgreSQL transaction as the business row and provisioning outbox job. The endpoint returns the same resource and `Idempotency-Replayed: true` for a matching retry; a key reused by another creator or with a changed payload returns HTTP 409. Tests cover missing/empty keys (400), sequential HTTP replay, changed-payload/changed-creator rejection, and 16 concurrent service requests using the same key. The concurrent test exposed and fixed a preflight race: when the winner commits between the initial key lookup and slug check, the loser now re-reads the idempotency record and replays rather than incorrectly returning a slug conflict. The separate 16-request same-slug/different-key HTTP race still produces one 201 and fifteen 409 responses.

HTTP integration uses a test-only authentication handler and PostgreSQL 17; it does not establish production OIDC/JWKS, issuer/audience, MFA or identity-provider staging. The central database migration `006_business_creation_idempotency.sql` must be applied before deploying the API version requiring the header. No deployment or worker enablement is implied.

### Worker lifecycle and heartbeat fault injection
Run these checks against a disposable PostgreSQL queue and a controllable fake provisioning handler before enabling the hosted worker:
- [x] Hold a handler operation open beyond one renewal interval and verify the same claim token is renewed before the lease expires. (PostgreSQL worker integration test; exact evidence recorded below.)
- [x] Force renewal to return false (lease reclaimed or token fenced); verify the handler cancellation token is cancelled and the worker never calls the success transition for that claim.
- [x] Make the heartbeat storage call fail or time out; verify the handler is cancelled, the claim is not marked succeeded/failed by the stale worker, and normal lease expiry/reclaim recovers the job.
- [x] Cancel host shutdown while the handler is blocked; verify cancellation propagates, heartbeat stops, and the worker does not mark the job successful during shutdown.
- [x] Make the handler ignore cancellation and return success after the lease is lost; verify the worker still refuses completion and the reclaimed claim remains protected by its new fencing token.
- [x] Race heartbeat renewal with handler completion repeatedly; verify completion succeeds only while the exact lease owner/token is still unexpired.
- [x] Verify heartbeat exceptions and handler exceptions are observable through sanitized logs without logging credentials, connection strings or exception messages.

The current PostgreSQL lease integration suite validates lease-store fencing and recovery. These worker-level orchestration checks are a separate acceptance gate; do not infer that they have passed from lease-store tests alone.

### Worker orchestration test evidence (partial)

Exact code/test head `1c87f546646dc71337e71ceb99ab773b4415568d`:
- PostgreSQL 17 integration [run 44945795410](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44945795410): **168 passed, 0 failed, 0 skipped**.
- Full backend/web [run 44945795465](https://github.com/Mahdi0383/MG-Business-Platform/actions/runs/44945795465): backend tests **168 passed**; isolated agent API Release build **0 warnings / 0 errors**; frontend tests **20 passed**; dependency audit and production bundle passed; Pages deployment skipped for PR.

The worker integration tests exercise the actual `TenantProvisioningWorker.ProcessOneAsync` against disposable PostgreSQL with short internal test intervals while preserving production defaults (2-minute lease, 5-second poll, 30-second renewal). They verify:
- successful heartbeat renewal while retaining the same fencing token;
- lease-loss cancellation and prevention of stale completion/failure;
- heartbeat database failure, cancellation, and subsequent lease reclaim with a new token;
- host shutdown cancellation and cessation of renewals;
- an uncooperative handler returning after lease loss cannot complete/activate the reclaimed job;
- eight repeated heartbeat/completion races after lease expiry never activate the business;
- eight repeated valid-lease heartbeat/completion races complete and activate only under the current claim;
- generic handler exception text is not persisted; only `PROVISIONING_FAILED` is stored;
- structured worker log messages include exception type but not secret-bearing exception text or connection details.

The seven worker lifecycle checks above now have automated PostgreSQL test evidence on code/test SHA `1c87f546646dc71337e71ceb99ab773b4415568d`. Repeated success and lease-expiry races (eight iterations each) and sanitized log assertions passed. Production-host/staging exercise and a dedicated metrics assertion remain open. This is test evidence only; it does not authorize enabling the worker.


## 5. Platform worker rollout
- [ ] Keep Provisioning:WorkerEnabled=false until all agent, database, secret-manager and fault-injection gates above pass.
- [ ] Verify the platform API uses only the mTLS agent client credential and has no PostgreSQL cluster-administration credential.
- [ ] Enable the worker for one isolated canary tenant first. Confirm the tenant remains Pending until schema and runtime verification succeed.
- [ ] Confirm permanent failures are dead-lettered, transient failures are retried with bounded attempts, and stale lease tokens cannot mutate jobs.
- [ ] Confirm the canary can be disabled and safely resumed without duplicate databases, roles, credentials or tenant activation.

## 6. Evidence required before production approval
Attach all of the following to the release record:
1. Commit SHA and successful backend build/test workflow URLs for the exact release candidate.
2. Successful PostgreSQL 17 integration workflow URL for the exact same SHA.
3. Sanitized output of certificate and database privilege verification.
4. Fault-injection results for every boundary in section 4.
5. Secret-manager rotation and backup/restore evidence.
6. Deployment topology, firewall rules, monitoring alerts, on-call owner and rollback procedure.
7. Explicit approval from the platform owner.

A green unit-test workflow alone is not production approval. A cancelled, skipped or stale workflow is not evidence of a passing release candidate. Until all gates are evidenced, keep automated provisioning disabled and do not mark tenants Active based only on registry creation.