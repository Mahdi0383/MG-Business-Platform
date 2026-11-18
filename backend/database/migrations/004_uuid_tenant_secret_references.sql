-- Move not-yet-provisioned tenants to immutable UUID-based Vault references.
-- Existing active tenant references are intentionally untouched; migrate those secrets
-- through a dedicated rotation procedure before changing their registry references.
BEGIN;

UPDATE business_tenants
SET database_secret_reference = 'tenants/' || replace(id::text, '-', '') || '/postgres'
WHERE status = 'PendingProvisioning'
  AND database_secret_reference LIKE 'tenants/%/postgres';

COMMIT;
