-- Durable provisioning outbox: business registration and its provisioning request
-- are committed atomically in the MG central database.
BEGIN;

CREATE TABLE IF NOT EXISTS tenant_provisioning_jobs (
    id uuid PRIMARY KEY,
    business_id uuid NOT NULL REFERENCES business_tenants(id) ON DELETE RESTRICT,
    status varchar(20) NOT NULL,
    attempts integer NOT NULL DEFAULT 0,
    created_at_utc timestamptz NOT NULL,
    next_attempt_at_utc timestamptz NOT NULL,
    started_at_utc timestamptz NULL,
    completed_at_utc timestamptz NULL,
    last_error_code varchar(80) NULL,
    CONSTRAINT ck_tenant_provisioning_jobs_status
        CHECK (status IN ('Pending', 'Running', 'Retry', 'Succeeded', 'DeadLetter')),
    CONSTRAINT ck_tenant_provisioning_jobs_attempts CHECK (attempts >= 0)
);

CREATE INDEX IF NOT EXISTS ix_tenant_provisioning_jobs_ready
    ON tenant_provisioning_jobs (status, next_attempt_at_utc, created_at_utc)
    WHERE status IN ('Pending', 'Retry');

CREATE INDEX IF NOT EXISTS ix_tenant_provisioning_jobs_business
    ON tenant_provisioning_jobs (business_id, created_at_utc DESC);

COMMIT;
