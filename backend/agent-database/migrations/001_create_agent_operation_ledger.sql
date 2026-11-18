-- Agent-owned durable operation ledger. Run this as a deployment administrator.
-- The agent runtime role receives only the narrowly scoped DML rights documented in the runbook.
BEGIN;

CREATE SCHEMA IF NOT EXISTS mg_agent;

CREATE TABLE mg_agent.tenant_provisioning_operations (
    idempotency_key uuid PRIMARY KEY,
    tenant_id uuid NOT NULL UNIQUE,
    database_name varchar(63) NOT NULL UNIQUE,
    phase varchar(24) NOT NULL
        CHECK (phase IN ('Prepared', 'DatabaseCreated', 'SchemaApplied', 'Verified', 'Completed')),
    credential_key_id varchar(80) NOT NULL
        CHECK (credential_key_id ~ '^[A-Za-z0-9_-]{1,80}$'),
    credential_nonce bytea NOT NULL
        CHECK (octet_length(credential_nonce) = 12),
    credential_ciphertext bytea NOT NULL
        CHECK (octet_length(credential_ciphertext) BETWEEN 1 AND 16384),
    credential_tag bytea NOT NULL
        CHECK (octet_length(credential_tag) = 16),
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_at_utc timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_mg_agent_provisioning_phase
    ON mg_agent.tenant_provisioning_operations (phase, updated_at_utc);

COMMIT;
