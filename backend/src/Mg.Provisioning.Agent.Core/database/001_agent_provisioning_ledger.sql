-- Agent-owned database only. This schema is separate from the MG central registry
-- and from every tenant database. Apply using a dedicated agent-schema migration identity.
BEGIN;

CREATE SCHEMA IF NOT EXISTS mg_agent;

CREATE TABLE IF NOT EXISTS mg_agent.provisioning_operations
(
    tenant_id UUID PRIMARY KEY,
    idempotency_key UUID NOT NULL UNIQUE,
    database_name TEXT NOT NULL UNIQUE,
    request_hash BYTEA NOT NULL CHECK (octet_length(request_hash) = 32),
    phase TEXT NOT NULL CHECK (phase IN
        ('Prepared', 'DatabaseCreated', 'SchemaApplied', 'Verified')),
    credential_key_id TEXT NOT NULL,
    credential_nonce BYTEA NOT NULL CHECK (octet_length(credential_nonce) = 12),
    credential_ciphertext BYTEA NOT NULL CHECK (octet_length(credential_ciphertext) BETWEEN 1 AND 16384),
    credential_tag BYTEA NOT NULL CHECK (octet_length(credential_tag) = 16),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CHECK (database_name = 'mg_tenant_' || replace(tenant_id::text, '-', ''))
);

CREATE INDEX IF NOT EXISTS ix_provisioning_operations_phase_updated
    ON mg_agent.provisioning_operations (phase, updated_at);

REVOKE ALL ON SCHEMA mg_agent FROM PUBLIC;
REVOKE ALL ON TABLE mg_agent.provisioning_operations FROM PUBLIC;

COMMIT;
