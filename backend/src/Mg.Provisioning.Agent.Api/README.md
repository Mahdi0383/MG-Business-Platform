# Isolated MG provisioning agent API

This is a separate, internal-only HTTPS host. It is not part of the customer-facing MG Platform API and must not be exposed through a public reverse proxy.

## Startup configuration

Inject these values through the deployment secret manager/environment; never commit certificate passwords or private keys:

- `Agent__ListenPort`: internal TLS port (default deployment choice: 9443).
- `Agent__ServerCertificatePath`: read-only mounted PKCS#12 server certificate path.
- `Agent__ServerCertificatePassword`: runtime secret.
- `Agent__AllowedClientThumbprints`: comma-separated explicit SHA-1 certificate thumbprints for approved MG API client certificates.

Startup fails closed unless the port, server certificate and non-empty client thumbprint allowlist are valid. The server certificate must have a private key, be in date, be a non-CA certificate and include Server Authentication EKU plus Digital Signature key usage. Every incoming TLS connection requires a client certificate; Kestrel validates its chain/revocation, while the application additionally requires Client Authentication EKU, Digital Signature, non-CA status, validity and an explicit thumbprint allowlist match.

The HTTP surface is intentionally narrow:
- `GET /health/live`
- `GET /health/ready`
- `POST /internal/v1/tenant-databases`

The POST endpoint accepts only the exact JSON fields `tenantId`, `idempotencyKey` and `databaseName`; rejects unknown/duplicate properties and mismatched UUID-derived database names; caps request bodies at 4 KiB; and rate-limits each client certificate. Idempotency-key reuse or immutable tenant-resource marker conflicts return HTTP 409 with `IDEMPOTENCY_CONFLICT`; these are permanent failures, not retryable service outages. Other error responses contain stable codes only. Exception messages, SQL and credentials are never logged.


## Enabling the real handler

The API now conditionally registers `NpgsqlAgentProvisioningHandler` only when all of the following are present:
- `Agent__ControlDatabase__ConnectionString`: dedicated agent-control PostgreSQL connection, using `SSL Mode=VerifyCA` or `VerifyFull`, a trusted root certificate, and a non-superuser login with `CREATEDB` + `CREATEROLE`, `NOINHERIT`, no replication and no BYPASSRLS.
- `Agent__CredentialEncryption__CurrentKeyId`: active encryption key ID.
- `Agent__CredentialEncryption__Keys__<key-id>`: base64-encoded 32-byte AES-256 key for each active/retained key ID. Inject from a runtime secret manager; never commit or bake into images.

The handler constructs the PostgreSQL operation ledger, AES-GCM protector, exact packaged tenant migration artifact, coordinator and Npgsql tenant provisioner. It generates separate random owner and application passwords once, stores both connection strings together in one authenticated encrypted agent-ledger payload, and returns only the least-privilege application connection string over mTLS after database/schema/runtime verification. The owner login is used only inside the isolated agent for database marking, schema migration and grants.

### Agent control database bootstrap

Create the `mg_agent_control` database and a dedicated control role using a separate database administrator. Example role attributes (replace the password through secure secret injection):

```sql
CREATE ROLE mg_agent_control
    LOGIN PASSWORD '<runtime-secret>'
    CREATEDB CREATEROLE
    NOINHERIT NOSUPERUSER NOREPLICATION NOBYPASSRLS;
```

Use a separate migration identity to apply `backend/src/Mg.Provisioning.Agent.Core/database/001_agent_provisioning_ledger.sql` to the agent control database. The API project copies this reviewed source artifact into its output as `agent-database/migrations/001_agent_provisioning_ledger.sql`; that output path is not a second source migration. The migration identity owns `mg_agent`; the runtime control role must not own the schema or database. PostgreSQL grants CONNECT and TEMP on databases to PUBLIC by default, so revoke those inherited database privileges before granting the agent role its explicit access. Grant only:

```sql
REVOKE ALL PRIVILEGES ON DATABASE mg_agent_control FROM PUBLIC;
GRANT CONNECT ON DATABASE mg_agent_control TO mg_agent_control;
GRANT USAGE ON SCHEMA mg_agent TO mg_agent_control;
GRANT SELECT, INSERT, UPDATE
    ON TABLE mg_agent.provisioning_operations TO mg_agent_control;
```

Do not grant DELETE, TRUNCATE, TEMP, schema CREATE, database ownership or table ownership. Verify PUBLIC has no CONNECT, CREATE or TEMP privilege on the control database. Readiness stays 503 until the ledger exists and the live control role/privilege checks pass. The tenant baseline migration is packaged at `tenant-database/migrations/001_tenant_core_baseline.sql` and is applied by the agent provisioner only after the tenant database and owner/runtime roles have been safely created.


### PostgreSQL 17 role-creation semantics

PostgreSQL 17 automatically creates a bootstrap-grantor ADMIN membership for the CREATEROLE identity that creates a role. That membership is INHERIT FALSE and SET FALSE, and the creating CREATEROLE identity cannot revoke or alter the bootstrap grant. The agent adds a separate grantor-scoped membership to the tenant owner role with ADMIN FALSE, INHERIT FALSE and SET TRUE so it can assign the database owner without inheriting owner privileges. The application role receives no SET or INHERIT membership path. The bootstrap ADMIN capability remains a powerful control-plane permission; keep the control credential only in this isolated service, audit it, and never place it in the Platform API.

The API host does not auto-run either agent or tenant migrations. Provisioning configuration being present is not sufficient by itself: readiness verifies the agent control database, required ledger grants, strict control-role attributes and that the control identity does not own the control database, agent schema or operation ledger table. Provisioning responses are explicitly marked no-store. Production rollout still requires a live TLS-enabled PostgreSQL integration environment, crash injection at database/role/migration/grant boundaries, key rotation and backup/restore drills.

## Remaining production rollout gates

`NpgsqlAgentProvisioningHandler` is conditionally registered when the control connection, packaged tenant baseline and complete AES-256 key ring are present. Readiness remains 503 until the control database ledger and least-privilege grants pass live checks. When secrets are absent, no handler is registered and provisioning requests return 503. Do not deploy or enable the platform worker until live TLS PostgreSQL integration, crash injection, secret-manager delivery/rotation, backup/restore and operational acceptance are complete.
