# Isolated tenant provisioning agent contract

Status: API client adapter implemented; agent service is not implemented or deployed. This document is the acceptance contract for that separate service.

## Network and identity

- Expose only HTTPS. Require mutual TLS and validate the client certificate chain, validity period, EKU and an explicit MG API client identity allowlist (thumbprint or approved SAN), not merely any certificate from a broad CA.
- Keep the endpoint on a private network. Do not publish it through the customer-facing reverse proxy.
- Rate-limit requests by authenticated client identity and tenant. Enforce a small request body limit and strict JSON schema.
- Never log request bodies, passwords, connection strings, certificate private material or raw provider exceptions.
- Agent server TLS certificates must be trusted by the API container. The API's PKCS#12 client certificate is mounted read-only and its password is injected as a runtime secret. The API accepts only a currently valid certificate with a private key, a non-CA Basic Constraints extension, Client Authentication EKU and Digital Signature key usage. The agent must additionally enforce its explicit client identity allowlist and revocation policy.

## Request and response

```http
POST /internal/v1/tenant-databases
Content-Type: application/json
```

Request JSON (no credentials or arbitrary SQL):

```json
{
  "tenantId": "UUID",
  "idempotencyKey": "UUID",
  "databaseName": "mg_tenant_<tenant UUID without hyphens>"
}
```

Success response is returned only over the authenticated mTLS channel:

```json
{
  "connectionString": "..."
}
```

The connection string is an application-role credential, not an owner or provisioning credential. It must identify exactly the requested database, use a distinct non-login database owner and a separate login role, and require PostgreSQL TLS certificate validation (VerifyCA or VerifyFull). The API stores it in Vault and does not persist it in the central registry or job record.

## Idempotency and lifecycle

- Treat (tenantId, idempotencyKey) as a durable unique operation key.
- Same key + same normalized request returns the same tenant resources and the current application credential; same key + different request returns HTTP 409.
- Reconcile partial operations after crashes. Never create a second database or second application role for a tenant.
- Derive database and role identifiers only from the UUID; validate exact expected names. Never accept caller-supplied role names, hostnames, schema names, SQL fragments or migration paths.
- Before reusing an existing database, verify its owner role and mg.tenant_identity UUID. A mismatched marker is a hard security incident; do not adopt or overwrite the database.
- Create the tenant database and owner/application roles using a dedicated non-superuser control role. The agent control role may need CREATEDB and constrained CREATEROLE; isolate its credentials to the agent process, restrict network access to the database control plane, and audit every operation. It must not be present in the API container.
- The owner role must be NOLOGIN and own the database and mg schema. The runtime application role must not own the database/schema, must not have CREATEDB, CREATEROLE, SUPERUSER, REPLICATION or BYPASSRLS, and must not have schema CREATE or DDL privileges.
- Generate a unique high-entropy application password per tenant. Never reuse the agent control credential as an application credential. Return the app connection string only to the mTLS-authenticated API so it can store it in Vault.
- Run tenant migrations in strict ascending version order. Calculate SHA-256 from the exact reviewed artifact bytes; the client request cannot choose a checksum or path. Use transaction-local mg.tenant_id and mg.migration_checksum settings when applying migration 001.
- Grant only required runtime privileges after migration. Set default privileges for future approved migrations deliberately; never grant ownership or broad ALL privileges to the runtime role.
- Verify from the runtime application connection that tenant identity matches, baseline migration version is present, and the runtime role has none of the prohibited capabilities. The API independently repeats this verification before activation.

## Safe HTTP error mapping

- 400, 401, 403, 404: rejected request/identity/configuration; return no sensitive details.
- 409: idempotency key reused with a different request or conflicting existing tenant resources.
- 429, 5xx, timeout: retryable service failure with a correlation ID.
- Never return SQL, database host credentials, raw exception text or secret-manager values in error responses.

## Required end-to-end tests before enabling the worker

1. Duplicate same-key requests produce one database and one role and return a valid credential.
2. Same idempotency key with changed tenant/database parameters returns 409.
3. Crash injection after each create/grant/migration/secret phase recovers without duplicate resources.
4. Existing database with a different tenant marker is rejected without mutation.
5. Runtime credential cannot create roles, databases, schemas, tables, triggers, functions or alter/drop the identity marker.
6. Runtime credential cannot read/write another tenant database.
7. Agent credential is absent from API process environment, filesystem and container.
8. mTLS rejects unknown, expired, wrong-EKU and revoked client certificates.
9. API retries agent 429/5xx safely and does not activate until Vault readback and independent verification succeeds.
10. Backup/restore and credential rotation are tested before production rollout.

The API adapter and its tests do not satisfy these agent acceptance gates. Keep Provisioning:WorkerEnabled=false until every gate passes in a disposable environment.
