-- Fence stale workers even when a worker ID is accidentally reused.
-- Every claim receives a new unguessable token; every lease mutation must present it.
BEGIN;

ALTER TABLE tenant_provisioning_jobs
    ADD COLUMN IF NOT EXISTS lease_token uuid;

UPDATE tenant_provisioning_jobs
SET lease_token = gen_random_uuid()
WHERE lease_owner IS NOT NULL
  AND lease_token IS NULL;

ALTER TABLE tenant_provisioning_jobs
    ADD CONSTRAINT ck_tenant_provisioning_jobs_lease_token_pair
    CHECK ((lease_owner IS NULL) = (lease_token IS NULL));

COMMIT;
