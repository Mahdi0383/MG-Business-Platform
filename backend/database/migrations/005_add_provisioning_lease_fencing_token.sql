-- Fence stale workers even when a worker ID is accidentally reused.
-- Every claim receives a new unguessable token; every lease mutation must present it.
BEGIN;

-- Recover legacy Running rows that never had a lease (for example, interrupted
-- work from before lease ownership was introduced) instead of leaving them stuck.
UPDATE tenant_provisioning_jobs
SET status = 'Retry',
    next_attempt_at_utc = now(),
    last_error_code = 'LEASE_MISSING_ON_UPGRADE',
    lease_owner = NULL,
    lease_expires_at_utc = NULL
WHERE status = 'Running'
  AND lease_expires_at_utc IS NULL;

ALTER TABLE tenant_provisioning_jobs
    ADD COLUMN IF NOT EXISTS lease_token uuid;

UPDATE tenant_provisioning_jobs
SET lease_token = gen_random_uuid()
WHERE lease_owner IS NOT NULL
  AND lease_token IS NULL;

ALTER TABLE tenant_provisioning_jobs
    ADD CONSTRAINT ck_tenant_provisioning_jobs_lease_token_pair
    CHECK ((lease_owner IS NULL) = (lease_token IS NULL));

ALTER TABLE tenant_provisioning_jobs
    ADD CONSTRAINT ck_tenant_provisioning_jobs_running_lease
    CHECK ((status = 'Running') = (lease_owner IS NOT NULL));

COMMIT;
