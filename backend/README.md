# MG Business Web — Backend foundation

This folder contains the first ASP.NET Core 10 control-plane API slice.

## Implemented in this slice

- PostgreSQL-backed MG central business registry.
- Liveness and database-readiness endpoints.
- Creator business registry list/create endpoints, protected by OIDC JWT validation and a platform-role claim.
- Atomic tenant registration plus durable provisioning-job enqueue in the MG central database.
- Slug validation and unique database constraint.
- Durable client idempotency for business creation: UUID `Idempotency-Key`, creator/request binding, atomic business/outbox/key persistence and safe response-loss replay.
- Platform database stores a secret-manager reference for a tenant database, never a raw database password or connection string.
- Optional HashiCorp Vault KV v2 adapter for tenant database secrets; HTTPS-only, path-validated and never logs secret values.
- Docker image definition.

## Not production-ready yet

Creator registry endpoints now require an OIDC access token issued by the configured HTTPS authority, with the API audience and an explicit `mg_platform_role` claim of `PlatformOwner` or `PlatformAdministrator`. The former shared bootstrap API key has been removed. This is an authentication and coarse creator-role gate only: creator user lifecycle, MFA enforcement, fine-grained permission administration, tenant membership, support grants and audited privileged access remain release blockers. Configure `Authentication__Authority` and `Authentication__Audience` as deployment settings. The issuer must publish standard OIDC discovery/JWKS metadata over HTTPS. Do not expose the API until an identity provider is configured and issuer/audience/role claims have been tested end to end. A durable provisioning queue and lease-management service are implemented, including atomic PostgreSQL claims, owner- and per-claim-token-checked renewals/completion and bounded retry/dead-letter handling. An opt-in hosted worker consumes the lease service only when enabled and an `ITenantProvisioningHandler` is registered. The platform API conditionally registers that handler with an HTTPS/mTLS adapter when the isolated agent address and client certificate are configured. The separate `Mg.Provisioning.Agent.Api` host includes a conditional PostgreSQL handler, encrypted idempotency ledger integration, tenant database/role creation, packaged baseline migration and runtime verification. Production deployment is still blocked by secret-manager integration for the agent key ring, production Vault policy/credentials, crash-injection at all side-effect boundaries, rotation and backup/restore drills, tenant API routing/workspace APIs, and final CSRF/CORS and network-security review. PostgreSQL integration tests now cover concurrent claims, expired-lease recovery, owner-checked renewal, dead-letter handling and successful activation; they do not replace full tenant-isolation or operational recovery tests. Creator registry requests currently have a per-client-IP limit of 30 requests per minute; this is not a substitute for an edge WAF or user-based throttling. Do not expose this API publicly until an HTTPS OIDC issuer, audience, role claims, MFA and privileged access controls have been configured and tested.

## Configuration

Set `ConnectionStrings__Platform`, `Authentication__Authority` (absolute HTTPS OIDC issuer URL), and `Authentication__Audience` through deployment configuration. The web Docker Compose file requires `MG_PLATFORM_DB_PASSWORD`, `MG_OIDC_AUTHORITY`, and `MG_OIDC_AUDIENCE`; the obsolete shared bootstrap-key setting has been removed. The Vault KV v2 adapter reads `MG_VAULT_ADDRESS`, `MG_VAULT_TOKEN`, and `MG_VAULT_MOUNT` when the platform provisioner is configured. The isolated agent's control-database connection, TLS server certificate, client-certificate allowlist and AES-256 key ring are separate settings documented in `src/Mg.Provisioning.Agent.Api/README.md`. The worker remains disabled (`MG_PROVISIONING_WORKER_ENABLED=false`) until the isolated agent, Vault policy and all production acceptance gates are deployed and verified. Never commit credentials or signing keys.

## Local run

```bash
cd backend/src/Mg.Platform.Api
dotnet restore
dotnet run
```

Apply the reviewed SQL migrations in order: `database/migrations/001_create_business_registry.sql`, then `database/migrations/002_create_tenant_provisioning_jobs.sql`, then `database/migrations/003_add_provisioning_job_leases.sql`, then `database/migrations/004_uuid_tenant_secret_references.sql`, then `database/migrations/005_add_provisioning_lease_fencing_token.sql`. Apply migration 006 to the MG central database only with `bash backend/scripts/apply-platform-migration-006.sh`; it verifies `database/migrations/006_business_creation_idempotency.sha256`, requires PostgreSQL TLS `verify-full`, and records the exact applied checksum without overwriting a conflicting record. Follow `docs/PLATFORM_MIGRATION_RUNBOOK.md` for preconditions, readiness verification and rollback. Migration 006 is required before the API accepts business-creation requests. Migration 004 converts only pending tenant rows to immutable UUID-based Vault paths; active tenant references require a separate controlled secret migration. The service intentionally does not call `EnsureCreated` or silently mutate the production schema at startup.

## Initial endpoints

- `GET /health/live`
- `GET /health/ready`
- `GET /openapi/v1.json`
- `GET /api/v1/platform/businesses` (OIDC bearer token + creator platform role required)
- `POST /api/v1/platform/businesses` (OIDC bearer token + creator platform role required)

POST body: `{"name":"Example business","slug":"example-business"}`. Every POST must also include a non-empty UUID `Idempotency-Key` header. Reuse the same key for retries of the same logical create operation; the API replays the same business resource only for the same validated OIDC issuer/subject and normalized payload, and returns `409 Conflict` for mismatched reuse.

The new record starts as `PendingProvisioning` and receives a durable queue item in `Pending` state in the same central-database save transaction. Queue lifecycle states are `Pending`, `Running`, `Retry`, `Succeeded` and `DeadLetter`. The record is not marked active until database provisioning and baseline initialization have been implemented and verified.

## PostgreSQL integration tests

The backend test suite includes real PostgreSQL integration tests for concurrent claims, expired-lease recovery, owner-checked renewals, dead-letter reconciliation and successful activation. CI provisions an ephemeral PostgreSQL 17 service and sets `MG_TEST_POSTGRES` automatically. To run the complete suite locally, set `MG_TEST_POSTGRES` to a disposable PostgreSQL database connection string; the test creates and drops an isolated temporary schema. Never point this variable at production or customer data.
