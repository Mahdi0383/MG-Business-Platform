#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
MIGRATION_ID="006_business_creation_idempotency"
MIGRATION_FILE="$SCRIPT_DIR/006_business_creation_idempotency.sql"
CHECKSUM_FILE="$SCRIPT_DIR/006_business_creation_idempotency.sha256"

: "${PGHOST:?Set PGHOST to the PostgreSQL server hostname}"
: "${PGDATABASE:?Set PGDATABASE to the MG central platform database}"
: "${PGUSER:?Set PGUSER to the dedicated migration identity}"
: "${PGSSLMODE:?Set PGSSLMODE=verify-full}"
: "${PGSSLROOTCERT:?Set PGSSLROOTCERT to the trusted database CA certificate}"

if [[ "$PGSSLMODE" != "verify-full" ]]; then
    printf '%s\n' "Refusing migration: PGSSLMODE must be verify-full." >&2
    exit 1
fi

EXPECTED_CHECKSUM="$(awk 'NF { print $1; exit }' "$CHECKSUM_FILE")"
ACTUAL_CHECKSUM="$(sha256sum "$MIGRATION_FILE" | awk '{ print $1 }')"
if [[ ! "$EXPECTED_CHECKSUM" =~ ^[0-9a-f]{64}$ || "$ACTUAL_CHECKSUM" != "$EXPECTED_CHECKSUM" ]]; then
    printf '%s\n' "Refusing migration: migration 006 does not match its reviewed SHA-256 manifest." >&2
    exit 1
fi

EXISTING_CHECKSUM="$(
    psql -X --set=ON_ERROR_STOP=1 \
        --set=migration_id="$MIGRATION_ID" \
        --tuples-only --no-align <<'SQL'
SELECT CASE
    WHEN to_regclass(format('%I.%I', current_schema(), 'platform_schema_migrations')) IS NULL
        THEN 'SELECT NULL::text WHERE false'
    ELSE format(
        'SELECT btrim(checksum_sha256) FROM %I.platform_schema_migrations WHERE migration_id = %L',
        current_schema(),
        :'migration_id')
END
\gexec
SQL
)"

if [[ -n "$EXISTING_CHECKSUM" && "$EXISTING_CHECKSUM" != "$EXPECTED_CHECKSUM" ]]; then
    printf '%s\\n' "Refusing migration: a different checksum is already recorded for migration 006." >&2
    exit 1
fi

export PGAPPNAME="mg-platform-migration-006"
psql -X --set=ON_ERROR_STOP=1 --file="$MIGRATION_FILE"

psql -X --set=ON_ERROR_STOP=1 \
    --set=migration_id="$MIGRATION_ID" \
    --set=migration_checksum="$EXPECTED_CHECKSUM" <<'SQL'
INSERT INTO platform_schema_migrations (migration_id, checksum_sha256, applied_at_utc)
VALUES (:'migration_id', :'migration_checksum', clock_timestamp())
ON CONFLICT (migration_id) DO NOTHING;
SQL

RECORDED_CHECKSUM="$(
    psql -X --set=ON_ERROR_STOP=1 \
        --set=migration_id="$MIGRATION_ID" \
        --tuples-only --no-align <<'SQL'
SELECT format(
    'SELECT btrim(checksum_sha256) FROM %I.platform_schema_migrations WHERE migration_id = %L',
    current_schema(),
    :'migration_id')
\gexec
SQL
)"

if [[ "$RECORDED_CHECKSUM" != "$EXPECTED_CHECKSUM" ]]; then
    printf '%s\n' "Migration SQL completed, but the durable checksum record is missing or mismatched." >&2
    printf '%s\n' "Readiness will remain unhealthy. Stop deployment and investigate; do not edit the checksum row manually." >&2
    exit 1
fi

printf '%s\n' "Migration 006 applied and its SHA-256 checksum was verified."
