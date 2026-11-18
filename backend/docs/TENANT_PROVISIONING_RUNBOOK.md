# Tenant database provisioning — operational contract

## Current implementation boundary

Business registration creates two records in the MG central PostgreSQL database:

1. A `business_tenants` row in `PendingProvisioning`.
2. A `tenant_provisioning_jobs` row in `Pending`.

Both are added to the same EF Core unit of work and saved together. The relational provider wraps the multi-command save in a transaction. A tenant must never be reported as active merely because its registry row exists.

The queue schema and enqueue path are implemented. Migration 003 adds a stable idempotency key and paired lease-owner/expiry fields, plus indexes for lease recovery. **There is not yet a worker, tenant database creator, secret-manager adapter, or tenant migration runner.** Do not advertise automated provisioning as available.

## Required worker state machine

```text
Pending ──claim──> Running ──success──> Succeeded
                       │
                       ├──transient failure──> Retry ──due──> Running
                       │
                       └──permanent / retry limit──> DeadLetter
```

Only a verified successful provisioning operation may move the business from `PendingProvisioning` (or `Provisioning`) to `Active`. The worker must not infer success from database existence alone.

## Worker requirements before implementation

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

The API code now contains an atomic PostgreSQL lease-claim operation using `FOR UPDATE SKIP LOCKED`, a lease renewal guarded by owner and unexpired lease, and a completion operation that updates the job and business registry in one central-database transaction. Completion is the final step only after an external provisioner has independently verified the tenant database, migrations and baseline. The lease service is registered for dependency injection but no hosted worker invokes it yet. The current claim operation skips jobs at the retry limit; an operator/reaper process must move exhausted jobs to `DeadLetter` before production use. This slice deliberately does not create databases, secrets or schemas.

## Manual deployment sequence

Apply central migrations in order:

1. `database/migrations/001_create_business_registry.sql`
2. `database/migrations/002_create_tenant_provisioning_jobs.sql`
3. `database/migrations/003_add_provisioning_job_leases.sql` (required before enabling any worker)

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
