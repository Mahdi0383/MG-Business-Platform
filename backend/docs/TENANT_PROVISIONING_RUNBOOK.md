# Tenant database provisioning — operational contract

## Current implementation boundary

Business registration creates two records in the MG central PostgreSQL database:

1. A `business_tenants` row in `PendingProvisioning`.
2. A `tenant_provisioning_jobs` row in `Pending`.

Both are added to the same EF Core unit of work and saved together. The relational provider wraps the multi-command save in a transaction. A tenant must never be reported as active merely because its registry row exists.

The queue schema, enqueue path, lease service and opt-in hosted worker are implemented. The platform API conditionally registers `TenantProvisioningHandler` and its mTLS `HttpTenantDatabaseProvisioner` when the isolated agent address and client certificate are configured. The separate `Mg.Provisioning.Agent.Api` host conditionally registers its PostgreSQL handler when its strict-TLS control connection, packaged tenant migration and complete AES-256 key ring are configured. That agent creates UUID-derived owner/application roles, creates tenant databases, applies the packaged baseline and independently verifies runtime access. The platform handler then writes the returned application credential to Vault, reads it back and verifies the tenant before allowing job completion. The implementation is not production-approved: the agent key ring is currently injected through application configuration rather than a dedicated secret-manager integration, and live production Vault/agent deployment, crash-injection, rotation and backup/restore acceptance remain open. Do not advertise provisioning as production-ready.

## Required worker state machine

```text
Pending ──claim──> Running ──success──> Succeeded
                       │
                       ├──transient failure──> Retry ──due──> Running
                       │
                       └──permanent / retry limit──> DeadLetter
```

Only a verified successful provisioning operation may move the business from `PendingProvisioning` (or `Provisioning`) to `Active`. The worker must not infer success from database existence alone. The optional Vault adapter must be configured with an HTTPS endpoint and a narrowly scoped token; root tokens are prohibited.

## Remaining worker and provisioner requirements

- Claim jobs with a short transaction and PostgreSQL `FOR UPDATE SKIP LOCKED` so multiple worker replicas cannot process the same row concurrently.
- Migration 003 adds `idempotency_key` (unique), `lease_owner`, and `lease_expires_at_utc`. Migration 005 adds a fresh `lease_token` fencing UUID on every claim. Renew, failure and success transitions must match both worker ID and the exact claim token; a stale attempt must never complete a reclaimed job, even if the same worker ID is reused.
- Make every operation idempotent: database create, role/user create, secret write, schema migration, baseline seed and health verification.
- Persist only secret-manager references in the central registry. Never log or store tenant passwords, connection strings, access tokens or raw secret values in job error fields.
- Use bounded exponential retry with jitter for transient errors; classify permanent configuration and authorization errors into `DeadLetter`.
- Record a safe error code and correlation ID, not raw exception text that may contain secrets.
- Verify connectivity, expected migration version, required baseline records and tenant isolation before activation.
- Keep tenant database creation and migrations separate from HTTP request handling. The API only enqueues durable work.
- Provide operator-visible retry, cancel and dead-letter review actions with an immutable audit event before production rollout.

## Lease service boundary

The API code now contains an atomic PostgreSQL lease-claim operation using `FOR UPDATE SKIP LOCKED`, each claim receives a unique database-generated fencing token, and lease renewal/failure/completion are guarded by worker ID, exact fencing token and unexpired lease, all expiry/backoff/activation timestamps use PostgreSQL server time rather than worker clocks, and retry uses bounded exponential backoff with jitter and safe error-code validation, and a completion operation that updates the job and business registry in one central-database transaction. Completion rolls back unless the business is eligible to transition from `PendingProvisioning` to `Active`. Completion is the final step only after an external provisioner has independently verified the tenant database, migrations and baseline. The opt-in hosted worker invokes claim, renewal, failure, completion and periodic dead-letter reconciliation only after a concrete `ITenantProvisioningHandler` is registered and the feature flag is enabled. It is disabled by default; startup fails closed if enabled without a handler. The claim operation skips jobs at the retry limit; transient failures reaching the retry limit and permanent failures are moved to `DeadLetter` by the lease service. `DeadLetterExhaustedAsync` reconciles pending/retry jobs at the attempt limit and expired running leases at the attempt limit into `DeadLetter` with `LEASE_EXHAUSTED`; the hosted worker invokes it periodically. PostgreSQL integration tests cover concurrent claims, lease recovery, stale-owner renewal rejection, same-worker-ID stale-attempt fencing (including rejected stale success/failure), dead-letter transitions and successful activation. These tests validate queue mechanics only; they do not prove production tenant isolation or operational readiness. The platform worker orchestrates through the separately deployed mTLS agent; it does not itself hold PostgreSQL cluster-administration credentials.

## Manual deployment sequence

Apply central migrations in order:

1. `database/migrations/001_create_business_registry.sql`
2. `database/migrations/002_create_tenant_provisioning_jobs.sql`
3. `database/migrations/003_add_provisioning_job_leases.sql` (required before enabling any worker)
4. `database/migrations/004_uuid_tenant_secret_references.sql` (moves pending tenant secret references to immutable UUID-based paths; active references are intentionally untouched)
5. `database/migrations/005_add_provisioning_lease_fencing_token.sql` (required before deploying code that uses per-claim lease tokens)

The API intentionally does not run schema migrations automatically at startup. Confirm both tables, unique slug constraint, queue status check and indexes before starting the API.

## Acceptance gates

- Duplicate slug requests remain safe under concurrent registration.
- A failed insert cannot leave an orphan business or orphan queue job.
- Two worker replicas cannot hold the same active lease; stale leases are recoverable after expiry, and a stale attempt cannot mutate a reclaimed job even if its worker ID is reused.
- A repeated command with the same idempotency key cannot create duplicate tenant resources.
- Worker restart recovers expired leases without losing work.
- Repeated delivery does not create duplicate databases, users, secrets or baseline records.
- No business becomes `Active` before all provisioning checks pass.
- Backup/restore drills include the central queue and tenant databases.

## Tenant secret storage

An optional `ITenantSecretStore` adapter targets HashiCorp Vault KV v2. Configure `Secrets:Vault:Address` as an HTTPS origin, `Secrets:Vault:Token` from deployment secret injection, and `Secrets:Vault:Mount` (defaults to `secret`). The API container receives these through the optional `MG_VAULT_*` environment variables. The central registry stores only the secret reference. The Vault token must be scoped to the tenant-secret prefix only; do not use a root token. No Vault server, token, policy or tenant database provisioning is configured by this repository. The adapter has not been exercised against a live Vault deployment.

## Hosted worker safety gate

The hosted queue worker is opt-in (`Provisioning:WorkerEnabled=false` by default). Enabling it fails closed during API startup unless the isolated-agent-backed `ITenantProvisioningHandler` is registered and `Provisioning:WorkerId` is set. The worker resolves the handler before claiming jobs, claims with a two-minute lease, renews every 30 seconds using an independent scope, stops work when lease renewal fails, and only marks success through the worker-ID + exact fencing-token + unexpired-lease checked service. Provider exception messages are not logged; only exception types and stable safe error codes are recorded. Keep the worker disabled until the isolated agent and Vault deployment have passed all operational acceptance gates.

## Provisioning orchestration contract

`TenantProvisioningHandler` now coordinates the tenant lifecycle without granting the API process direct PostgreSQL administration rights: it derives an immutable database name and Vault reference from the tenant UUID, calls `ITenantDatabaseProvisioner` with the durable job idempotency key, writes the returned connection string to Vault, reads it back using constant-time comparison, and requires an independent verification of tenant identity plus the expected schema baseline before returning. Existing Vault credentials are re-verified on retries rather than blindly overwritten. The central registry stores only `tenants/{tenant-uuid}/postgres` as the secret reference. The concrete `HttpTenantDatabaseProvisioner` calls the separately deployed mTLS agent; that agent's `NpgsqlTenantResourceProvisioner` creates the tenant database/roles and applies the packaged baseline. The platform handler is registered only when the agent endpoint and client-certificate settings are complete. Keep the worker disabled until the separate agent is deployed and all operational acceptance gates pass.

The provisioner implementation must make repeated requests with the same tenant UUID/idempotency key safe, must not log credentials, must provision the exact deterministic database name, and must verify an immutable tenant identity marker plus schema migration version. A successful TCP connection is not sufficient.

Vault KV v2 writes must use create-only compare-and-set (`options.cas=0`). If another worker creates the tenant secret after this worker's initial read, the write must fail rather than overwrite the existing credential; the job then retries and reads/verifies the already-created secret. Never change this to an unconditional overwrite for provisioning retries.

Tenant PostgreSQL connection strings returned by a provisioner must use `SslMode=VerifyCA` or `SslMode=VerifyFull`; `Require` is rejected because encryption without server-certificate validation is insufficient against endpoint impersonation. Prefer `VerifyFull` with a trusted CA and matching DNS hostname. Npgsql's `Trust Server Certificate` option is obsolete and has no effect in the pinned provider; do not include this deprecated parameter.

## Tenant database baseline migration

The first tenant-database schema artifact is `tenant-database/migrations/001_tenant_core_baseline.sql`. It creates a dedicated `mg` schema with an immutable tenant identity marker, append-only migration ledger, business settings, external-identity-linked users (no local password storage), role/permission assignments, customers, append-only audit events and an outbox. It intentionally does not include business-domain modules yet; those must be added as separately versioned migrations and enabled by server-side module entitlements.

A future isolated migration runner must execute each migration in a transaction as the tenant database owner, set transaction-local `mg.tenant_id` and `mg.migration_checksum` values using parameterized `set_config`, then verify the inserted identity and migration ledger before granting the separate runtime role access. The migration checksum must be SHA-256 of the exact reviewed migration artifact bytes; never accept a checksum supplied by the tenant or API request. The runtime role must not own the database/schema, must not have DDL or role-management privileges, and must not be able to update/delete the tenant identity marker. The provisioner must refuse to run if the database already contains a different tenant UUID or an unexpected migration history.

## Isolated provisioning agent adapter

The API now has an HTTP adapter for a separately deployed HTTPS agent. The API only sends the tenant UUID, durable idempotency key and deterministic database name; it does not receive cluster-administration credentials. The adapter requires an HTTPS origin and a mounted, valid PKCS#12 client certificate, sends the client certificate for mutual TLS, maps agent failures to stable safe error codes, and independently verifies the returned connection using TLS certificate validation. Verification requires the expected tenant marker and schema version, and rejects a runtime role that is superuser, can create databases/roles/schemas, bypass row-level security, replicate, or owns the tenant database.

Configure `Provisioning:Agent:Address`, `Provisioning:Agent:ClientCertificatePath` and `Provisioning:Agent:ClientCertificatePassword` only after the separate agent is deployed. In Docker, mount the PFX read-only at the configured container path. The isolated agent HTTP host conditionally registers its PostgreSQL handler when the strict-TLS control connection, packaged tenant baseline and complete AES-256 key ring are present. It creates UUID-derived LOGIN owner and runtime roles; the owner password remains encrypted inside the agent ledger and is never returned to the Platform API. The owner connection applies the packaged migration and runtime grants. PostgreSQL 17's immutable creator ADMIN membership is accounted for with a separate SET-only membership path to the owner role; the runtime role has no SET or INHERIT path. Live TLS PostgreSQL integration currently passes 108 tests, but crash-injection across every DDL/secret boundary, key rotation and backup/restore acceptance remain open. Keep the worker disabled until those operational gates pass.

See [PROVISIONING_AGENT_CONTRACT.md](PROVISIONING_AGENT_CONTRACT.md) for the detailed mTLS endpoint, request/response, idempotency, role separation, safe error mapping and end-to-end acceptance contract for the isolated agent.

The API-side independent verification also checks that the runtime role has no UPDATE or DELETE privileges on `mg.tenant_identity`, `mg.schema_migrations` or `mg.audit_events`, in addition to rejecting database/schema ownership and cluster-level capabilities. Granting any of these mutation privileges causes verification to fail before activation.

The separate agent must also persist a stable credential for idempotent replay: an operation ledger keyed by tenant/idempotency key, per-tenant advisory locking, AEAD-encrypted connection-string persistence before side effects, key-versioned decryption, durable phase reconciliation and same-credential replay. See `PROVISIONING_AGENT_CONTRACT.md` for the exact acceptance requirements. A retry must never rotate a credential while an earlier API response may still be in flight.

### Agent database control role and tenant role layout

The isolated agent's control database connection must use PostgreSQL `VerifyCA` or `VerifyFull`, a private network route and a dedicated non-superuser login with `CREATEDB` and `CREATEROLE`; it must have `NOINHERIT`, no replication, no BYPASSRLS and no superuser. PostgreSQL 17 automatically gives this CREATEROLE identity a bootstrap ADMIN membership on roles it creates; the agent adds only the explicit SET-only path needed for the owner role and never uses the bootstrap grant to inherit privileges. Treat this identity as highly privileged and never put it in the MG Platform API container.

For tenant UUID `{uuid}`, the provisioner derives:
- database: `mg_tenant_{uuid:N}`
- isolated LOGIN owner: `mg_tenant_{uuid:N}_owner` (its credential remains encrypted inside the agent and is never returned to the Platform API)
- runtime login: `mg_tenant_{uuid:N}_app`

The database and both roles receive comments containing the immutable tenant UUID and role purpose. An existing object with a mismatched marker, owner, attributes or membership is rejected. The runtime login is not a member of any other role and cannot create databases, roles, schemas or tenant tables. It cannot mutate the tenant identity marker, schema migration ledger or audit events.

The agent control database is separate from the MG central registry. Apply `src/Mg.Provisioning.Agent.Core/database/001_agent_provisioning_ledger.sql` using a migration identity, then grant the agent runtime role only CONNECT, schema USAGE, and SELECT/INSERT/UPDATE on `mg_agent.provisioning_operations`. No DELETE/TRUNCATE, schema CREATE, role membership or database ownership is required. The separate agent API host conditionally registers its handler only when the strict-TLS control connection, packaged tenant baseline and complete AES-256 key ring are configured. This layout is not an instruction to deploy or enable the platform worker before all TLS, crash-recovery, secret-management and operational acceptance gates pass.
