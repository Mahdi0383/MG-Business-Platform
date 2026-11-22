# MG Platform API contract

## Creator business registry

### Create business

`POST /api/v1/platform/businesses/`

Authentication:
- HTTPS OIDC bearer token.
- Authorization policy `mg-creator`: authenticated creator with `mg_platform_role=PlatformOwner` or `PlatformAdministrator`.
- The validated token must contain non-empty `iss` and `sub` claims. These are used only to derive a one-way creator identity hash for idempotency; raw values are not stored in the registry.

Required header:

```http
Idempotency-Key: 2b4f4a7e-6a4c-4c0e-9c74-5d4d8f42b4c0
```

The value must be a non-empty UUID. Clients must generate one key per logical business-creation operation and persist/reuse that same key if the network fails or the response is lost. Do not generate a new key for a retry of the same logical operation.

Request body:

```json
{
  "name": "Example Cafe",
  "slug": "example-cafe"
}
```

Behavior:
- First valid request returns `201 Created`, a `Location` header for the business resource and a summary with status `PendingProvisioning`.
- A retry with the same key, same validated creator identity and same normalized request returns `201 Created` for the same business ID and Location. The body reflects the resource's current status. The response includes `Idempotency-Replayed: true`.
- Reusing a key with a different normalized name/slug or a different creator identity returns `409 Conflict` (`business_creation_conflict`).
- Creating a slug already owned by another business with a different key returns `409 Conflict`.
- Missing, malformed or empty idempotency key returns `400 Bad Request`.
- The business row, provisioning outbox job and idempotency binding are committed in one PostgreSQL transaction. Unique constraints, not preflight reads, arbitrate concurrent requests.
- The idempotency key is globally unique. It is not a bearer credential: authentication and authorization are evaluated on every request.
- Idempotency records are retained for the lifetime of the business to prevent a delayed retry from creating a duplicate.

The server stores SHA-256 hashes of the issuer+subject identity tuple and the normalized request; it does not persist raw OIDC subject/issuer or request body in the idempotency table. It stores only the business ID, hashes, key and creation timestamp.

### Rate limit

The `creator-bootstrap` fixed-window policy allows 30 requests per minute per remote IP. Requests above the limit receive `429 Too Many Requests`. Production ingress should additionally enforce suitable per-identity limits and abuse controls.

## Migration order

Apply central platform migrations in numeric order. Migration `006_business_creation_idempotency.sql` must be applied before deploying an API version that requires the `Idempotency-Key` header. Do not enable the provisioning worker as part of this schema migration.

The `/health/ready` endpoint checks that `business_tenants` and `tenant_provisioning_jobs` exist and that `business_creation_requests` has all five required non-null columns with the expected PostgreSQL types, its named primary key, the `business_id` foreign key to `business_tenants(id)` with `ON DELETE RESTRICT`, both hash-format check constraints, and a valid/ready business lookup index. It returns Unhealthy when migration 006 is absent or structurally incomplete, so the new API must not receive traffic before the migration is applied. Automated PostgreSQL tests deliberately remove each critical schema element and verify readiness fails closed. This check does not apply migrations automatically.
