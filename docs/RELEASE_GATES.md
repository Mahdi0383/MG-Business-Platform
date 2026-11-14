# Release Gates

A release may not be described as complete until all applicable gates have evidence in CI and a reproducible Windows release build.

## Build and quality
- Restore, Release build, and tests pass on supported Windows runners.
- Nullable warnings and compiler warnings are treated as errors.
- Formatting/analyzers run in CI.
- Dependency vulnerability checks run and high/critical findings are triaged.
- Reproducible publish/package artifacts are produced and checksummed.

## Financial correctness
- Journal debits equal credits for every posted transaction.
- Currency, precision, rounding, tax, discounts, refunds, and reversals have explicit policies and tests.
- Closed periods reject unauthorized edits.
- Voids and corrections preserve audit history.
- Inventory and accounting updates are atomic and idempotent.

## Data safety
- Schema migrations upgrade supported versions and fail safely.
- File-backed SQLite initialization serializes backup, migration and seeding through an exclusive adjacent `.migration.lock` file handle with a bounded 60-second wait; validate this behavior on supported local Windows filesystems. Network/shared filesystem database paths require separate validation.
- Backup restore acquires the same per-database maintenance lock before replacing the target, so it cannot race startup migration/backup; callers must still stop all database clients and active writes before restore. Validate lock behavior on supported local Windows filesystems; network/shared filesystem semantics remain unverified.
- Before applying pending migrations to an existing file-backed SQLite database with the supported initial EF migration history, startup creates and verifies a pre-migration backup; if backup validation fails, startup stops before applying schema changes.
- Databases without the initial MG Business Platform EF migration history are not automatically upgraded; a separately tested legacy import/migration path is required. Backup retention, restore instructions and an actual upgrade/restore drill remain release gates.
- Backup and restore are tested against real generated databases.
- A migration failure after an earlier DDL operation is covered by a rollback regression: partial schema changes are absent and the pre-migration backup remains integrity-verified.
- A failed update or restore cannot silently destroy the last known-good data.
- Export/import validation reports rejected records without partial corruption.

## Security
- Authorization is enforced server-side for every protected operation.
- Public endpoints cannot reach admin APIs, local files, backups, or secrets.
- License verification uses a public key only in the main app.
- Secrets are not logged or committed.
- Static analysis and dependency scanning are reviewed.

## Storefront publication
- DNS, HTTPS, reverse proxy, host-header validation, CSRF, rate limits, and public health checks tested.
- CGNAT/inbound-port-blocked scenarios fail safely and explain alternatives.
- Certificate renewal, host restart, WAN IP change, and unpublish tested.
- Never declare online availability based only on a local health check.

## License Manager
- The standalone Persian RTL GUI generates keys without overwriting existing key files.
- A new signed license can be issued to a new output path and then verified successfully.
- Tampered, expired, wrong-device, malformed, and wrong-key licenses are rejected.
- The independent installer launches the GUI and includes the CLI companion and documentation.
- The main application enforces module and expiry entitlements in protected operations before licensing can be called production-ready.

## Release evidence
Publish CI run links, test summary, known limitations, supported Windows versions, installation/upgrade instructions, rollback instructions, and SHA-256 checksums. If a gate has not been tested, mark it NOT VERIFIED; do not imply it passed.
