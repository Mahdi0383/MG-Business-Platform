# MG provisioning agent core

This project contains security primitives and the durable operation ledger for the separately deployed tenant provisioning agent. It is deliberately not registered in the MG Platform API and does not enable the API worker.

## Agent database

Apply `database/001_agent_provisioning_ledger.sql` to a dedicated agent control database, not the MG central registry and not a tenant database. The migration identity should own `mg_agent`. After creating a dedicated non-owner runtime role, grant only the required ledger access (substitute the actual role name):\n\n```sql\nGRANT CONNECT ON DATABASE mg_agent_control TO mg_agent_runtime;\nGRANT USAGE ON SCHEMA mg_agent TO mg_agent_runtime;\nGRANT SELECT, INSERT, UPDATE ON TABLE mg_agent.provisioning_operations TO mg_agent_runtime;\n```\n\nDo not grant DELETE, TRUNCATE, REFERENCES, TRIGGER, schema CREATE, role membership or database ownership. The runtime identity must not be a superuser, database owner, CREATEDB or CREATEROLE role. Keep migration ownership separate from the runtime role.

The ledger serializes each tenant operation with a PostgreSQL transaction-scoped advisory lock. It persists a stable AES-256-GCM encrypted application connection string and the `Prepared` phase before any PostgreSQL tenant resource side effect. Repeated requests with the same tenant/idempotency/database tuple decrypt and return the same credential. Conflicting keys or request payloads fail closed.

Encryption key material must be supplied by an external runtime secret manager. Do not place keys in appsettings, source control, this database, container image layers or logs. Retain old key IDs while any row still references them.

The operation ledger is a durable replay primitive, not a complete agent service. It does not itself create databases, roles, apply tenant migrations, enforce mTLS, manage a PostgreSQL control role or prove end-to-end recovery. Those remain separate acceptance gates in `backend/docs/PROVISIONING_AGENT_CONTRACT.md`.
