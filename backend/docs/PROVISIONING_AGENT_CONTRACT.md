# Isolated tenant provisioning agent contract

Status: The isolated mTLS agent HTTP host, conditional PostgreSQL handler registration, encrypted idempotency ledger integration and tenant resource provisioner are implemented in the repository and covered by CI. The service is not approved for production deployment; the operational acceptance gates below remain mandatory.

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

The connection string is an application-role credential, not an owner or provisioning credential. It must identify exactly the requested database, use a distinct LOGIN owner role whose credential remains isolated inside the agent, and a separate least-privilege LOGIN application role. Both connections require PostgreSQL TLS certificate validation (VerifyCA or VerifyFull); only the application-role connection string is returned to the API. The API stores it in Vault and does not persist it in the central registry or job record.

## Idempotency and lifecycle

- Treat (tenantId, idempotencyKey) as a durable unique operation key.
- Same key + same normalized request returns the same tenant resources and the current application credential; same key + different request returns HTTP 409.
- Reconcile partial operations after crashes. Never create a second database or second application role for a tenant.
- Derive database and role identifiers only from the UUID; validate exact expected names. Never accept caller-supplied role names, hostnames, schema names, SQL fragments or migration paths.
- Before reusing an existing database, verify its owner role and mg.tenant_identity UUID. A mismatched marker is a hard security incident; do not adopt or overwrite the database.
- Create the tenant database and owner/application roles using a dedicated non-superuser control role. The agent control role may need CREATEDB and constrained CREATEROLE; isolate its credentials to the agent process, restrict network access to the database control plane, and audit every operation. It must not be present in the API container.
- The tenant owner role is a dedicated LOGIN with a random password stored only in the agent's authenticated encrypted credential bundle. It owns the tenant database and `mg` schema, but has NOINHERIT, NOCREATEDB, NOCREATEROLE, NOSUPERUSER, NOREPLICATION and NOBYPASSRLS. Only the isolated agent can retrieve its credential. The runtime application role is a separate LOGIN and must not own the database/schema, must have no outgoing role memberships, and may have only the unavoidable PostgreSQL 17 incoming bootstrap ADMIN edge from the isolated agent control identity (superuser grantor, ADMIN=true, INHERIT=false, SET=false). It must have no SET/INHERIT path, no CREATEDB, CREATEROLE, SUPERUSER, REPLICATION or BYPASSRLS, and no schema CREATE or DDL privileges.
- Generate a unique high-entropy application password per tenant. Never reuse the agent control credential as an application credential. Return the app connection string only to the mTLS-authenticated API so it can store it in Vault.
- Run tenant migrations in strict ascending version order. Calculate SHA-256 from the exact reviewed artifact bytes; the client request cannot choose a checksum or path. Use transaction-local mg.tenant_id and mg.migration_checksum settings when applying migration 001. Treat the migration ledger, tenant identity marker and audit events as append-only; the runtime role receives no UPDATE or DELETE privileges on them.
- Grant only required runtime privileges after migration. Revoke PUBLIC EXECUTE on all existing functions in `mg` and set owner-scoped default privileges so future functions are not executable by PUBLIC unless explicitly approved. Runtime verification must reject PUBLIC EXECUTE on tenant functions. Set default privileges for future approved migrations deliberately; never grant ownership or broad ALL privileges to the runtime role.
- Verify from the runtime application connection that tenant identity matches, baseline migration version is present, and the runtime role has none of the prohibited capabilities. The API independently repeats this verification before activation.


### Durable idempotency ledger and credential replay

A successful agent response can be lost after the database side effects complete. Therefore the agent must not generate a new application password on every retry and must not return a different connection string for the same operation.

- Before creating any PostgreSQL role or database, create or lock a durable operation row keyed by `idempotency_key` with unique constraints on both `idempotency_key` and `tenant_id`. Persist the normalized tenant UUID/database name and reject any mismatch with an existing row.
- Serialize operations per tenant with a PostgreSQL advisory lock. This prevents concurrent requests from racing while creating roles, migrating the schema or changing credentials.
- Generate the application password once. Persist the full application connection string only as authenticated ciphertext using AES-256-GCM (or an equivalently reviewed AEAD). Bind tenant UUID, idempotency key, database name and encryption-key ID as associated authenticated data. Store nonce, tag, key ID and ciphertext; never persist plaintext in the operation ledger.
- Keep encryption keys outside the agent database in a runtime secret manager. Support key IDs and a controlled key-rotation process: retain prior decryption keys until every operation encrypted with them has been re-encrypted and verified.
- Commit the encrypted credential record before the first external side effect. On retries, decrypt and reuse that exact credential; re-apply the matching role password if an earlier attempt stopped between ledger persistence and role setup.
- Record safe, monotonic operation phases (for example `Prepared`, `DatabaseCreated`, `SchemaApplied`, `Verified`). Each phase transition must be durable and idempotent. On restart, reconcile actual PostgreSQL state against the immutable tenant marker and phase; never trust the phase record alone.
- Mark the operation complete only after the tenant marker, migration checksum/version, database owner, owner/runtime role attributes, schema/table grants and runtime connection have all been independently verified.
- A duplicate request with the same key and payload must return the same decrypted connection string. A different payload for the same key, or a second idempotency key for an already provisioned tenant, must return a safe conflict and must not rotate credentials or mutate resources.
- If the API loses its response, it retries the same key and receives the same credential. The API then performs its Vault CAS write/readback and its own independent tenant/runtime-role verification before the central job can complete.


## Implemented agent core and HTTP host (not production deployed)

The repository now contains `backend/src/Mg.Provisioning.Agent.Core`:
- `AgentCredentialProtector`: AES-256-GCM envelopes with 96-bit random nonces, 128-bit tags, strict UTF-8, zeroed plaintext/key buffers, key IDs for rotation, and AAD binding tenant UUID, idempotency UUID, deterministic database name and key ID.
- `AgentProvisioningOperationStore`: PostgreSQL transaction-scoped per-tenant advisory locking; durable unique tenant/idempotency records; stable encrypted credential replay; conflict rejection; and strictly monotonic `Prepared → DatabaseCreated → SchemaApplied → Verified` phase transitions.
- The credential factory is invoked only after the tenant lock and only before any resource side effect. It must generate a credential and must not perform network/database provisioning. The store validates exact database identity and requires `SSL Mode=VerifyCA` or `VerifyFull` before encrypting/persisting.
- Agent-owned database migration: `backend/src/Mg.Provisioning.Agent.Core/database/001_agent_provisioning_ledger.sql`. Apply it to a separate agent control database. It is not a migration for the MG central registry or a tenant database.
- PostgreSQL integration tests execute the checked-in ledger migration and exercise replay, concurrent duplicate requests, key reuse conflicts, failed preparation recovery and monotonic phase progression.

The mTLS HTTP host, conditional handler registration, durable operation coordinator, PostgreSQL tenant resource creation, tenant migration runner and runtime verification are implemented in the repository. They are not yet deployed or accepted for production. Production secret-manager integration (the current host reads its configured key ring from application configuration), automated key rotation, exhaustive crash-injection after every external side effect, production control-role bootstrap/audit, backup/restore rehearsal and deployment/network acceptance remain outstanding. Do not interpret green CI as production approval. Keep the platform worker disabled.


## PostgreSQL resource provisioner implementation

The agent core includes `NpgsqlTenantResourceProvisioner`, which is wired through `NpgsqlAgentProvisioningHandler` when its required configuration and packaged migration are available. Its contract and safety boundaries are implemented:

- The control connection must use `VerifyCA` or `VerifyFull`, have a password, and identify a non-superuser login with `CREATEDB` and `CREATEROLE`; the role must not have replication, BYPASSRLS or inherited privileges. Control connections disable pooling to avoid session-role leakage.
- Tenant names are deterministic: database `mg_tenant_{tenantUuid:N}`, owner role `mg_tenant_{tenantUuid:N}_owner`, runtime role `mg_tenant_{tenantUuid:N}_app`.
- Owner/runtime roles are created with explicit safe attributes and immutable role comments inside one transaction. The owner is LOGIN but has no cluster privileges and owns only its own tenant database/schema. Its password is encrypted at rest in the agent ledger and is never returned to the platform API. The control identity receives a separate SET-only membership path to the owner role so it can create the database with that owner; it does not inherit owner privileges. The runtime role is LOGIN and NOINHERIT, has no outgoing membership, and has only the immutable PostgreSQL 17 bootstrap ADMIN membership from the isolated agent control identity (ADMIN=true, INHERIT=false, SET=false, superuser grantor); it has no SET/INHERIT path and no CREATEDB/CREATEROLE/SUPERUSER/REPLICATION/BYPASSRLS.
- The database is created with the owner role and receives a UUID-derived shared-object comment. If the process crashes after CREATE DATABASE but before the comment, a retry adopts it only when owner matches and the database is provably empty; an unmarked non-empty database is rejected. Schema migration and runtime grants run over a separate owner-authenticated connection, not by escalating the control connection.
- Public database privileges are revoked; CONNECT is granted only to the agent control and tenant runtime identities. A newly created PostgreSQL database's default PUBLIC CONNECT/TEMPORARY ACLs are tolerated only during the owner-authenticated bootstrap connection; the provisioner revokes all PUBLIC database privileges and revalidates the strict boundary before writing the immutable database marker. Every later owner-side access-configuration mutation rejects PUBLIC CONNECT/CREATE/TEMPORARY drift instead of silently repairing it. Schema/table grants are explicit; the runtime identity has no schema/database CREATE and no INSERT/UPDATE/DELETE/TRUNCATE/REFERENCES/TRIGGER on tenant identity, migration ledger or audit events. It cannot own tenant tables or functions.
- The migration runner uses the exact packaged artifact bytes and checksum, sets transaction/session-local tenant identity and migration checksum, verifies an existing migration rather than re-running it, and applies the baseline as the tenant owner. It can safely resume if the migration committed but the coordinator did not persist its next phase.
- Runtime verification independently checks the tenant marker, exact migration checksum/version, database and schema owner, runtime role attributes/memberships, immutable-table privileges, no owned tables/functions, and required customer CRUD.

The agent HTTP host conditionally registers `NpgsqlAgentProvisioningHandler` only when the dedicated control-database connection, packaged tenant migration artifact and complete AES-256 key ring are configured. `/health/ready` remains 503 when the handler is absent or live PostgreSQL readiness checks fail. The host enforces HTTPS with mandatory client certificates, certificate allowlisting, strict request parsing, request-size limits and per-certificate rate limiting. Repository CI exercises the host/handler and PostgreSQL 17 TLS integration path; this does not replace production secret-manager delivery, crash-injection, rotation, backup/restore or deployment-topology acceptance.

## Control-plane maintenance serialization

Tenant provisioning holds a shared PostgreSQL advisory session lock on the dedicated agent control database for the full external provisioning sequence. Credential rewrap holds the matching shared transaction lock. This allows independent tenant operations to proceed concurrently while providing a maintenance barrier for cooperating database administrators.

Before changing the control role's memberships or control-database/schema/ledger ACLs, an administrator must acquire the matching exclusive transaction lock in the same agent control database and keep the changes in that transaction:

```sql
BEGIN;
SELECT pg_advisory_xact_lock(19783, 20562);
-- Apply reviewed role-membership or control-database ACL changes here.
COMMIT;
```

The lock coordinates only actors that follow this procedure; PostgreSQL superusers can bypass the application protocol or terminate sessions. It is not a substitute for restricted DBA access, change approval, audit logging or the live readiness checks immediately before mutations. The provisioner fails closed when it observes an unexpected boundary after the maintenance window.

## Safe HTTP error mapping

- 400, 401, 403, 404: rejected request/identity/configuration; return no sensitive details.
- 409: idempotency key reused with a different request or conflicting existing tenant resources.
- 429, 5xx, timeout: retryable service failure; return only the documented stable error code and never expose provider details.
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

The API adapter, agent host and repository integration tests do not by themselves satisfy production acceptance. Keep Provisioning:WorkerEnabled=false until every gate passes in a disposable environment and the evidence is attached to the release record.


### PostgreSQL 17 creator membership caveat

PostgreSQL 17 automatically gives the CREATEROLE identity that creates a role a bootstrap-grantor ADMIN membership with INHERIT FALSE and SET FALSE. PostgreSQL documents that this bootstrap grant cannot be changed by the CREATEROLE identity. The agent therefore does not attempt to revoke or rewrite that grant. It adds a second, grantor-scoped membership for the owner role with ADMIN FALSE, INHERIT FALSE and SET TRUE; this is the only SET path used to assign the database owner. The application role receives only the immutable bootstrap ADMIN edge from the control identity; it receives no SET or INHERIT path and has no outgoing membership. The control identity remains a highly privileged control-plane capability because of its unavoidable creator ADMIN grants; keep it exclusively inside the isolated agent, never in the Platform API, and audit its operations.
