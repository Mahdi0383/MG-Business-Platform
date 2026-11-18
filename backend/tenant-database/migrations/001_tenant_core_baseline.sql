-- First migration applied to each newly provisioned, dedicated tenant database.
-- The provisioning agent must run this as the tenant database owner, then grant
-- runtime access only to the separate least-privilege application role.
BEGIN;

CREATE SCHEMA IF NOT EXISTS mg;

CREATE TABLE mg.schema_migrations (
    version integer PRIMARY KEY,
    name varchar(160) NOT NULL,
    checksum_sha256 char(64) NOT NULL,
    applied_at_utc timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE mg.tenant_identity (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    tenant_id uuid NOT NULL UNIQUE,
    created_at_utc timestamptz NOT NULL DEFAULT now()
);

CREATE FUNCTION mg.reject_tenant_identity_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'tenant identity is immutable';
END;
$$;

CREATE TRIGGER trg_tenant_identity_immutable
BEFORE UPDATE OR DELETE ON mg.tenant_identity
FOR EACH ROW EXECUTE FUNCTION mg.reject_tenant_identity_mutation();

CREATE TABLE mg.business_settings (
    setting_key varchar(120) PRIMARY KEY,
    setting_value jsonb NOT NULL,
    updated_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_by_user_id uuid NULL
);

CREATE TABLE mg.users (
    id uuid PRIMARY KEY,
    external_identity_provider varchar(80) NOT NULL,
    external_subject varchar(200) NOT NULL,
    email varchar(320) NULL,
    display_name varchar(160) NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'Active'
        CHECK (status IN ('Invited', 'Active', 'Suspended', 'Archived')),
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_at_utc timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_mg_users_external_identity
        UNIQUE (external_identity_provider, external_subject)
);

CREATE UNIQUE INDEX ux_mg_users_email_lower
    ON mg.users (lower(email))
    WHERE email IS NOT NULL;

CREATE TABLE mg.roles (
    id uuid PRIMARY KEY,
    role_key varchar(80) NOT NULL UNIQUE,
    display_name varchar(120) NOT NULL,
    is_system_role boolean NOT NULL DEFAULT false,
    created_at_utc timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE mg.permissions (
    permission_key varchar(120) PRIMARY KEY,
    description varchar(240) NOT NULL
);

CREATE TABLE mg.role_permissions (
    role_id uuid NOT NULL REFERENCES mg.roles(id) ON DELETE CASCADE,
    permission_key varchar(120) NOT NULL REFERENCES mg.permissions(permission_key) ON DELETE RESTRICT,
    PRIMARY KEY (role_id, permission_key)
);

CREATE TABLE mg.user_roles (
    user_id uuid NOT NULL REFERENCES mg.users(id) ON DELETE CASCADE,
    role_id uuid NOT NULL REFERENCES mg.roles(id) ON DELETE RESTRICT,
    granted_at_utc timestamptz NOT NULL DEFAULT now(),
    granted_by_user_id uuid NULL REFERENCES mg.users(id) ON DELETE SET NULL,
    PRIMARY KEY (user_id, role_id)
);

CREATE TABLE mg.customers (
    id uuid PRIMARY KEY,
    customer_number varchar(64) NULL,
    display_name varchar(180) NOT NULL,
    phone_e164 varchar(24) NULL,
    email varchar(320) NULL,
    status varchar(20) NOT NULL DEFAULT 'Active'
        CHECK (status IN ('Active', 'Blocked', 'Archived')),
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_at_utc timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX ux_mg_customers_number
    ON mg.customers (customer_number)
    WHERE customer_number IS NOT NULL;

CREATE INDEX ix_mg_customers_phone ON mg.customers (phone_e164)
    WHERE phone_e164 IS NOT NULL;

CREATE TABLE mg.audit_events (
    id uuid PRIMARY KEY,
    occurred_at_utc timestamptz NOT NULL DEFAULT now(),
    actor_user_id uuid NULL REFERENCES mg.users(id) ON DELETE SET NULL,
    action varchar(120) NOT NULL,
    subject_type varchar(80) NOT NULL,
    subject_id varchar(120) NULL,
    correlation_id uuid NULL,
    safe_metadata jsonb NOT NULL DEFAULT '{}'::jsonb
);

CREATE INDEX ix_mg_audit_events_time
    ON mg.audit_events (occurred_at_utc DESC);

CREATE INDEX ix_mg_audit_events_subject
    ON mg.audit_events (subject_type, subject_id, occurred_at_utc DESC);

CREATE TABLE mg.outbox_messages (
    id uuid PRIMARY KEY,
    occurred_at_utc timestamptz NOT NULL DEFAULT now(),
    message_type varchar(160) NOT NULL,
    payload jsonb NOT NULL,
    correlation_id uuid NULL,
    available_at_utc timestamptz NOT NULL DEFAULT now(),
    attempts integer NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    processed_at_utc timestamptz NULL,
    last_error_code varchar(80) NULL
);

CREATE INDEX ix_mg_outbox_ready
    ON mg.outbox_messages (available_at_utc, occurred_at_utc)
    WHERE processed_at_utc IS NULL;

INSERT INTO mg.tenant_identity (singleton, tenant_id)
VALUES (true, :'mg_tenant_id'::uuid);

INSERT INTO mg.roles (id, role_key, display_name, is_system_role)
VALUES
    ('00000000-0000-4000-8000-000000000001', 'business_owner', 'Business Owner', true),
    ('00000000-0000-4000-8000-000000000002', 'business_admin', 'Business Administrator', true),
    ('00000000-0000-4000-8000-000000000003', 'employee', 'Employee', true);

INSERT INTO mg.permissions (permission_key, description)
VALUES
    ('platform.access', 'Access the business workspace'),
    ('users.manage', 'Manage business users and roles'),
    ('customers.read', 'View customer records'),
    ('customers.manage', 'Create and update customer records'),
    ('audit.read', 'View business audit events');

INSERT INTO mg.role_permissions (role_id, permission_key)
SELECT '00000000-0000-4000-8000-000000000001', permission_key
FROM mg.permissions;

INSERT INTO mg.role_permissions (role_id, permission_key)
SELECT '00000000-0000-4000-8000-000000000002', permission_key
FROM mg.permissions
WHERE permission_key <> 'audit.read';

INSERT INTO mg.role_permissions (role_id, permission_key)
VALUES ('00000000-0000-4000-8000-000000000003', 'platform.access');

INSERT INTO mg.schema_migrations (version, name, checksum_sha256)
VALUES (1, 'tenant_core_baseline', repeat('0', 64));

COMMIT;
