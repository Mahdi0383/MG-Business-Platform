# MG Business Web — Backend foundation

This folder contains the first ASP.NET Core 10 control-plane API slice.

## Implemented in this slice

- PostgreSQL-backed MG central business registry.
- Liveness and database-readiness endpoints.
- Creator business registry list/create endpoints.
- Slug validation and unique database constraint.
- Platform database stores a secret-manager reference for a tenant database, never a raw database password or connection string.
- Docker image definition.

## Not production-ready yet

The `X-MG-Platform-Key` guard is a temporary bootstrap boundary, not the final identity system. Before public deployment, replace it with OIDC authentication, creator role/permission policies, MFA and audited privileged access. Tenant database provisioning, tenant API routing, per-business migrations, business workspace APIs, rate limiting, CSRF/CORS policy, backups and integration/security tests are not yet implemented. Do not expose this API publicly with an empty or weak bootstrap key.

## Configuration

Set `ConnectionStrings__Platform` and a long random `Security__BootstrapPlatformKey` through deployment secrets. Do not commit real credentials.

## Local run

```bash
cd backend/src/Mg.Platform.Api
dotnet restore
dotnet run
```

Apply reviewed EF Core migrations before production use; the service intentionally does not call `EnsureCreated` or silently mutate the production schema at startup.

## Initial endpoints

- `GET /health/live`
- `GET /health/ready`
- `GET /openapi/v1.json`
- `GET /api/v1/platform/businesses` (bootstrap key required)
- `POST /api/v1/platform/businesses` (bootstrap key required)

POST body: `{"name":"Example business","slug":"example-business"}`

The new record starts as `PendingProvisioning`; it is not marked active until database provisioning and baseline initialization have been implemented and verified.
