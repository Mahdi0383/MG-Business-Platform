# MG provisioning agent core

This project contains security primitives and the durable operation ledger used by the separately hosted `Mg.Provisioning.Agent.Api`. The core library is not registered in the customer-facing MG Platform API; the platform API calls the separate agent over mTLS through its HTTP adapter. The platform worker remains disabled by default until production acceptance is complete.

## Agent database

Apply `database/001_agent_provisioning_ledger.sql` to a dedicated agent control database, not the MG central registry and not a tenant database. The migration identity should own `mg_agent`.

After creating a dedicated non-owner runtime role, revoke PostgreSQL's default database-level PUBLIC privileges (CONNECT and TEMP), then grant only the required ledger access (substitute the actual role name):

```sql
REVOKE ALL PRIVILEGES ON DATABASE mg_agent_control FROM PUBLIC;
GRANT CONNECT ON DATABASE mg_agent_control TO mg_agent_runtime;
GRANT USAGE ON SCHEMA mg_agent TO mg_agent_runtime;
GRANT SELECT, INSERT, UPDATE ON TABLE mg_agent.provisioning_operations TO mg_agent_runtime;
```

Verify PUBLIC has no CONNECT, CREATE or TEMP privilege. Do not grant TEMP, DELETE, TRUNCATE, REFERENCES, TRIGGER, schema CREATE, role membership or database ownership. The runtime identity must not be a superuser, database owner, CREATEDB or CREATEROLE role. Keep migration ownership separate from the runtime role.

The ledger serializes each tenant operation with a PostgreSQL transaction-scoped advisory lock. It persists a stable AES-256-GCM encrypted application connection string and the `Prepared` phase before any PostgreSQL tenant resource side effect. Repeated requests with the same tenant/idempotency/database tuple decrypt and return the same credential. Conflicting keys or request payloads fail closed.

Encryption key material must be supplied by an external runtime secret manager. Do not place keys in appsettings, source control, this database, container image layers or logs. Retain old key IDs while any row still references them.

The operation ledger is one component of the agent service. The sibling `Mg.Provisioning.Agent.Api` project supplies the mTLS HTTP host and conditionally wires the ledger to `NpgsqlTenantResourceProvisioner`, which creates UUID-derived tenant databases/roles, applies the packaged baseline and verifies runtime access. Repository integration tests exercise this path against TLS-enabled PostgreSQL 17. Production secret-manager delivery/rotation for the agent key ring, exhaustive crash-injection, backup/restore, deployment topology and operational sign-off remain mandatory acceptance gates in `backend/docs/PROVISIONING_AGENT_CONTRACT.md`.
