-- Durable client idempotency for Creator business registration.
-- This record is committed atomically with business_tenants and tenant_provisioning_jobs.
-- The key is globally unique; raw OIDC issuer/subject and request payload are never stored.
BEGIN;

CREATE TABLE IF NOT EXISTS business_creation_requests (
    idempotency_key uuid PRIMARY KEY,
    creator_identity_hash char(64) NOT NULL,
    request_fingerprint char(64) NOT NULL,
    business_id uuid NOT NULL REFERENCES business_tenants(id) ON DELETE RESTRICT,
    created_at_utc timestamptz NOT NULL,
    CONSTRAINT ck_business_creation_creator_hash
        CHECK (creator_identity_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_business_creation_request_hash
        CHECK (request_fingerprint ~ '^[0-9a-f]{64}$')
);

CREATE INDEX IF NOT EXISTS ix_business_creation_requests_business
    ON business_creation_requests (business_id, created_at_utc);

COMMIT;
