# MG Business Web — Backend foundation

This folder contains the first ASP.NET Core 10 control-plane API slice.

## Implemented in this slice

- PostgreSQL-backed MG central business registry.
- Liveness and database-readiness endpoints.
- Creator business registry list/create endpoints, protected by OIDC JWT validation and a platform-role claim.
- Atomic tenant registration plus durable provisioning-job enqueue in the MG central database.
- Slug validation and unique database constraint.
- Platform database stores a secret-manager reference for a tenant database, never a raw database password or connection string.
- Docker image definition.

## Not production-ready yet

Creator registry endpoints now require an OIDC access token issued by the configured HTTPS authority, with the API audience and an explicit `mg_platform_role` claim of `PlatformOwner` or `PlatformAdministrator`. The former shared bootstrap API key has been removed. This is an authentication and coarse creator-role gate only: creator user lifecycle, MFA enforcement, fine-grained permission administration, tenant membership, support grants and audited privileged access remain release blockers. Configure `Authentication__Authority` and `Authentication__Audience` as deployment settings. The issuer must publish standard OIDC discovery/JWKS metadata over HTTPS. Do not expose the API until an identity provider is configured and issuer/audience/role claims have been tested end to end. A durable provisioning queue is now recorded, but its background worker, idempotent database creation, tenant API routing, per-business migrations, business workspace APIs, CSRF/CORS policy, backups and integration/security tests are not yet implemented. Creator registry requests currently have a per-client-IP limit of 30 requests per minute; this is not a substitute for an edge WAF or user-based throttling. Do not expose this API publicly with an empty or weak bootstrap key.

## Configuration

Set `ConnectionStrings__Platform`, `Authentication__Authority` (absolute HTTPS OIDC issuer URL), and `Authentication__Audience` through deployment configuration. Never commit credentials or signing keys.

## Local run

```bash
cd backend/src/Mg.Platform.Api
dotnet restore
dotnet run
```

Apply the reviewed SQL migrations in order: `database/migrations/001_create_business_registry.sql`, then `database/migrations/002_create_tenant_provisioning_jobs.sql` to the MG central database before using registry endpoints. The service intentionally does not call `EnsureCreated` or silently mutate the production schema at startup.

## Initial endpoints

- `GET /health/live`
- `GET /health/ready`
- `GET /openapi/v1.json`
- `GET /api/v1/platform/businesses` (OIDC bearer token + creator platform role required)
- `POST /api/v1/platform/businesses` (OIDC bearer token + creator platform role required)

POST body: `{"name":"Example business","slug":"example-business"}`

The new record starts as `PendingProvisioning` and receives a durable queue item in `Pending` state in the same central-database save transaction. Queue lifecycle states are `Pending`, `Running`, `Retry`, `Succeeded` and `DeadLetter`. The record is not marked active until database provisioning and baseline initialization have been implemented and verified.
