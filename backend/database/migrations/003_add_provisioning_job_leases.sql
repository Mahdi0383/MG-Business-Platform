-- Add lease ownership and a stable idempotency key before running concurrent workers.
-- Existing queue rows receive a deterministic key based on their immutable job id.
BEGIN;

ALTER TABLE tenant_provisioning_jobs
    ADD COLUMN IF NOT EXISTS idempotency_key uuid,
    ADD COLUMN IF NOT EXISTS lease_owner varchar(120),
    ADD COLUMN IF NOT EXISTS lease_expires_at_utc timestamptz;

UPDATE tenant_provisioning_jobs
SET idempotency_key = id
WHERE idempotency_key IS NULL;

ALTER TABLE tenant_provisioning_jobs
    ALTER COLUMN idempotency_key SET NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_tenant_provisioning_jobs_idempotency
    ON tenant_provisioning_jobs (idempotency_key);

CREATE INDEX IF NOT EXISTS ix_tenant_provisioning_jobs_lease_recovery
    ON tenant_provisioning_jobs (status, lease_expires_at_utc)
    WHERE status = 'Running';

ALTER TABLE tenant_provisioning_jobs
    ADD CONSTRAINT ck_tenant_provisioning_jobs_lease_pair
    CHECK ((lease_owner IS NULL) = (lease_expires_at_utc IS NULL));

COMMIT;
