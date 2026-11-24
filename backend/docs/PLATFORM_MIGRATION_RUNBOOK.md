# MG central platform migration 006 — deployment runbook

## Scope

Migration 006 adds durable Creator business-registration idempotency and the checksum ledger used by the API readiness gate. It is a central MG platform database migration, not a tenant-database migration. It does not enable or start the provisioning worker.

## Preconditions

- Review the exact release candidate and the SQL file at `backend/database/migrations/006_business_creation_idempotency.sql`.
- Use a dedicated migration identity with only the DDL and table privileges needed for the reviewed platform migrations. Do not use a tenant runtime identity or a PostgreSQL cluster administrator for routine deployment.
- Install PostgreSQL client tools (`psql`) and GNU `sha256sum` on the migration runner.
- Inject connection settings from the deployment secret manager. Never put credentials in the command line, shell script, repository, logs or CI artifacts.
- Set `PGHOST`, `PGDATABASE`, `PGUSER`, `PGPASSWORD` (when password authentication is used), `PGSSLMODE=verify-full`, and `PGSSLROOTCERT` in the runner environment. The certificate must validate the configured server hostname.
- Confirm a tested backup and restore point exists before applying the migration.

## Apply and register the migration

From the repository root, execute:

```bash
bash backend/scripts/apply-platform-migration-006.sh
```

The runner:
1. Computes the SHA-256 digest of the exact SQL file and compares it with the reviewed `.sha256` manifest.
2. Refuses to run unless PostgreSQL TLS uses `verify-full` and a trusted root certificate is configured.
3. Checks any existing ledger record before SQL execution and refuses immediately if the recorded checksum conflicts with the reviewed artifact.
4. Executes the migration with `psql` and `ON_ERROR_STOP`.
5. Inserts the migration ID and reviewed checksum into `platform_schema_migrations` only when no record exists. It never overwrites an existing checksum.
6. Reads the durable record back and exits non-zero if it is missing or differs from the reviewed checksum.

A repeated invocation is safe for the unchanged artifact: the SQL uses additive `IF NOT EXISTS` operations and the checksum registration uses `ON CONFLICT DO NOTHING`. A conflicting pre-existing checksum is a hard stop, not an instruction to update the record manually.

The API runtime database identity must have read access to `platform_schema_migrations`, `business_creation_requests`, and the registry/outbox objects it uses. Keep DDL ownership with the migration identity.

## Readiness and rollout order

1. Apply migrations 001–005 in their reviewed order.
2. Apply migration 006 with the checksum-verifying runner above.
3. Verify the `platform_schema_migrations` row for `006_business_creation_idempotency` contains the expected SHA-256 value.
4. Start the API candidate with the provisioning worker disabled.
5. Require `GET /health/ready` to report Healthy. Readiness verifies both the exact durable migration checksum and the PostgreSQL catalog semantics of the migration's required tables, columns, constraints, foreign key and lookup index.
6. Run the authenticated Creator API smoke tests and inspect sanitized deployment logs.
7. Only proceed with a separately approved rollout. This migration does not authorize enabling `Provisioning:WorkerEnabled`.

The API fails closed when the migration ledger/table/record is missing, when the checksum differs, or when the required schema objects are incomplete. It does not apply or repair migrations automatically.

## Failure and rollback

- **Checksum manifest mismatch:** stop before executing SQL. Restore the reviewed migration artifact or create a separately reviewed migration/code change. Do not edit the manifest to bless an unreviewed SQL change.
- **SQL succeeds but checksum registration/readback fails:** keep the new API out of service; readiness remains Unhealthy. Inspect the migration ledger and permissions. Re-run the same reviewed runner only when the existing record is absent and the schema is verified. Never overwrite a mismatched record to force readiness.
- **Readiness remains Unhealthy:** stop rollout and inspect the sanitized health result plus the database catalog using the migration identity. Correct schema drift with a new, reviewed forward migration; do not weaken readiness predicates.
- **API rollback:** roll back the API deployment to the previous compatible API version that does not require the new idempotency header. Leave migration 006, its checksum record and all business-creation idempotency records in place. The migration is additive and has no destructive down migration; dropping these objects could destroy replay guarantees.
- **Database restore:** restore the central database and its migration ledger from the same consistent backup. Re-run the reviewed checksum verification before deploying the API version that requires migration 006.

Record the API image/version, migration SQL SHA-256, migration runner result, `/health/ready` result, backup identifier, operator and approval in the release record. Production rollout and operator acceptance remain separate release gates.
