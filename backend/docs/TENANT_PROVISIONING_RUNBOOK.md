# Tenant database provisioning — operational contract

## Current implementation boundary

Business registration creates two records in the MG central PostgreSQL database:

1. A `business_tenants` row in `PendingProvisioning`.
2. A `tenant_provisioning_jobs` row in `Pending`.

Both are added to the same EF Core unit of work and saved together. The relational provider wraps the multi-command save in a transaction. A tenant must never be reported as active merely because its registry row exists.

The queue schema and enqueue path are implemented. Migration 003 adds a stable idempotency key and paired lease-owner/expiry fields, plus indexes for lease recovery. **An opt-in hosted worker skeleton is present but no concrete handler, tenant database creator, least-privilege Vault policy deployment, or tenant migration runner is registered. An optional Vault KV v2 adapter now provides secret storage by reference; it has only been tested with HTTP stubs, not a live Vault server.** Do not advertise automated provisioning as available.

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
- Migration 003 adds `idempotency_key` (unique), `lease_owner`, and `lease_expires_at_utc`. Every worker claim must set owner and expiry atomically; lease renew/release must compare the owner token to prevent one replica releasing another replica's lease.
- Make every operation idempotent: database create, role/user create, secret write, schema migration, baseline seed and health verification.
- Persist only secret-manager references in the central registry. Never log or store tenant passwords, connection strings, access tokens or raw secret values in job error fields.
- Use bounded exponential retry with jitter for transient errors; classify permanent configuration and authorization errors into `DeadLetter`.
- Record a safe error code and correlation ID, not raw exception text that may contain secrets.
- Verify connectivity, expected migration version, required baseline records and tenant isolation before activation.
- Keep tenant database creation and migrations separate from HTTP request handling. The API only enqueues durable work.
- Provide operator-visible retry, cancel and dead-letter review actions with an immutable audit event before production rollout.

## Lease service boundary

The API code now contains an atomic PostgreSQL lease-claim operation using `FOR UPDATE SKIP LOCKED`, a lease renewal guarded by owner and unexpired lease, bounded exponential retry with jitter and safe error-code validation, and a completion operation that updates the job and business registry in one central-database transaction. Completion rolls back unless the business is eligible to transition from `PendingProvisioning` to `Active`. Completion is the final step only after an external provisioner has independently verified the tenant database, migrations and baseline. The opt-in hosted worker invokes claim, renewal, failure, completion and periodic dead-letter reconciliation only after a concrete ITenantProvisioningHandler is registered and the feature flag is enabled. It is disabled by default; startup fails closed if enabled without a handler. The claim operation skips jobs at the retry limit; transient failures reaching the retry limit and permanent failures are moved to `DeadLetter` by the lease service. `DeadLetterExhaustedAsync` now reconciles pending/retry jobs at the attempt limit and expired running leases at the attempt limit into `DeadLetter` with `LEASE_EXHAUSTED`. A hosted worker must invoke this method periodically; no scheduler invokes it yet. PostgreSQL 17 integration tests now cover concurrent claims, lease recovery, stale-owner renewal rejection, dead-letter transitions, and successful activation. These tests validate the queue mechanics only; they do not prove tenant isolation or production provisioning. The worker skeleton deliberately does not create databases or schemas by itself; the concrete provisioner and migration runner remain unimplemented. The Vault adapter is a separate secret-storage primitive and is not called by the worker until a reviewed handler is added.

## Manual deployment sequence

Apply central migrations in order:

1. `database/migrations/001_create_business_registry.sql`
2. `database/migrations/002_create_tenant_provisioning_jobs.sql`
3. `database/migrations/003_add_provisioning_job_leases.sql` (required before enabling any worker)
4. `database/migrations/004_uuid_tenant_secret_references.sql` (moves pending tenant secret references to immutable UUID-based paths; active references are intentionally untouched)

The API intentionally does not run schema migrations automatically at startup. Confirm both tables, unique slug constraint, queue status check and indexes before starting the API.

## Acceptance gates

- Duplicate slug requests remain safe under concurrent registration.
- A failed insert cannot leave an orphan business or orphan queue job.
- Two worker replicas cannot hold the same active lease; stale leases are recoverable after expiry.
- A repeated command with the same idempotency key cannot create duplicate tenant resources.
- Worker restart recovers expired leases without losing work.
- Repeated delivery does not create duplicate databases, users, secrets or baseline records.
- No business becomes `Active` before all provisioning checks pass.
- Backup/restore drills include the central queue and tenant databases.

## Tenant secret storage

An optional `ITenantSecretStore` adapter targets HashiCorp Vault KV v2. Configure `Secrets:Vault:Address` as an HTTPS origin, `Secrets:Vault:Token` from deployment secret injection, and `Secrets:Vault:Mount` (defaults to `secret`). The API container receives these through the optional `MG_VAULT_*` environment variables. The central registry stores only the secret reference. The Vault token must be scoped to the tenant-secret prefix only; do not use a root token. No Vault server, token, policy or tenant database provisioning is configured by this repository. The adapter has not been exercised against a live Vault deployment.

## Hosted worker safety gate

A hosted queue worker skeleton is present but is opt-in (Provisioning:WorkerEnabled=false by default). Enabling it fails closed during API startup unless a concrete ITenantProvisioningHandler is registered and Provisioning:WorkerId is set. The worker resolves the handler before claiming jobs, claims with a two-minute lease, renews every 30 seconds using an independent scope, stops work when lease renewal fails, and only marks success through the owner-checked lease service. Provider exception messages are not logged; only exception types and stable safe error codes are recorded. No concrete tenant database provisioner is registered yet, so keep the worker disabled.

## Provisioning orchestration contract

`TenantProvisioningHandler` now coordinates the tenant lifecycle without granting the API process direct PostgreSQL administration rights: it derives an immutable database name and Vault reference from the tenant UUID, calls `ITenantDatabaseProvisioner` with the durable job idempotency key, writes the returned connection string to Vault, reads it back using constant-time comparison, and requires an independent verification of tenant identity plus the expected schema baseline before returning. Existing Vault credentials are re-verified on retries rather than blindly overwritten. The central registry stores only `tenants/{tenant-uuid}/postgres` as the secret reference. No concrete `ITenantDatabaseProvisioner` implementation is registered yet; the worker remains disabled until an isolated least-privilege provisioner adapter and tenant migration runner exist.

The provisioner implementation must make repeated requests with the same tenant UUID/idempotency key safe, must not log credentials, must provision the exact deterministic database name, and must verify an immutable tenant identity marker plus schema migration version. A successful TCP connection is not sufficient.

Vault KV v2 writes must use create-only compare-and-set (`options.cas=0`). If another worker creates the tenant secret after this worker's initial read, the write must fail rather than overwrite the existing credential; the job then retries and reads/verifies the already-created secret. Never change this to an unconditional overwrite for provisioning retries.

Tenant PostgreSQL connection strings returned by a provisioner must use `SslMode=VerifyCA` or `SslMode=VerifyFull`; `Require` is rejected because encryption without server-certificate validation is insufficient against endpoint impersonation. Prefer `VerifyFull` with a trusted CA and matching DNS hostname. Npgsql's `Trust Server Certificate` option is obsolete and has no effect in the pinned provider; do not include this deprecated parameter.

## Tenant database baseline migration

The first tenant-database schema artifact is `tenant-database/migrations/001_tenant_core_baseline.sql`. It creates a dedicated `mg` schema with an immutable tenant identity marker, migration ledger, business settings, external-identity-linked users (no local password storage), role/permission assignments, customers, append-oriented audit events and an outbox. It intentionally does not include business-domain modules yet; those must be added as separately versioned migrations and enabled by server-side module entitlements.

A future isolated migration runner must execute each migration in a transaction as the tenant database owner, set transaction-local `mg.tenant_id` and `mg.migration_checksum` values using parameterized `set_config`, then verify the inserted identity and migration ledger before granting the separate runtime role access. The migration checksum must be SHA-256 of the exact reviewed migration artifact bytes; never accept a checksum supplied by the tenant or API request. The runtime role must not own the database/schema, must not have DDL or role-management privileges, and must not be able to update/delete the tenant identity marker. The provisioner must refuse to run if the database already contains a different tenant UUID or an unexpected migration history.
