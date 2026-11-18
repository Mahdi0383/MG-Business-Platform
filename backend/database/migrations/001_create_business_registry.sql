-- MG central control-plane schema. Review and apply through the deployment migration process.
-- This migration intentionally stores a secret-manager reference, never tenant DB credentials.
BEGIN;

CREATE TABLE IF NOT EXISTS business_tenants (
    id uuid PRIMARY KEY,
    name varchar(160) NOT NULL,
    slug varchar(80) NOT NULL,
    status varchar(24) NOT NULL,
    database_secret_reference varchar(300) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    activated_at_utc timestamptz NULL,
    CONSTRAINT uq_business_tenants_slug UNIQUE (slug),
    CONSTRAINT ck_business_tenants_status CHECK (
        status IN ('PendingProvisioning', 'Provisioning', 'Active', 'Suspended', 'Offboarding', 'Archived')
    )
);

CREATE INDEX IF NOT EXISTS ix_business_tenants_status_created_at_utc
    ON business_tenants (status, created_at_utc DESC);

COMMIT;
