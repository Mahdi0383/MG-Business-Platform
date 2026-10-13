# Security, Privacy and Licensing

## Security baseline

- Least privilege for users, services and files.
- Enforce role permissions and license entitlements in application/service boundaries, not just in the UI.
- Store secrets in an appropriate OS-protected store; never commit tokens, passwords, private keys, production data or real customer information.
- Validate input at boundaries and use parameterized database operations.
- Protect backups and exports because they may contain personal and financial data.
- Keep structured logs useful without recording passwords, payment secrets or unnecessary personal data.
- Audit sensitive actions, permission changes, financial postings/reversals, exports, restores and license changes.
- Use supported cryptographic libraries and established signed-token formats; do not design custom cryptography.
- Define a threat model for local attackers, stolen devices, malicious operators, compromised updates and remote API abuse.
- Apply database migrations and updates only through controlled, recoverable processes.

## License model

The main application verifies a digitally signed entitlement containing, as appropriate:
- customer/account identifier;
- product and module IDs;
- issue and expiry timestamps;
- permitted device/branch counts;
- license identifier and schema/version;
- optional trial or subscription terms.

The current implementation uses a JSON payload signed with RSA-PSS/SHA-256. The main app reads a public verification key only; the separate License Manager holds the private signing key at issuance time. This is a signed-entitlement foundation, not production enforcement: each protected operation must still check the license status and module entitlement. Never commit a private signing key.

## Offline behavior

- A valid offline license remains usable until its defined expiry while the application is disconnected.
- Offline validation cannot guarantee immediate remote revocation; communicate that trade-off.
- Expiry must not delete business data. Define a safe restricted/read-only and export/renewal policy.
- License failure must not corrupt the database or block data recovery.
- Device binding and clock rollback protections must be designed without falsely claiming they are impossible to bypass on a fully controlled offline computer.

## Online service baseline

- HTTPS, authentication, authorization, rate limits and request validation.
- Behind the bundled Caddy reverse proxy, trust forwarded host/protocol/client-IP headers only from loopback proxy addresses; never trust arbitrary internet-supplied forwarded headers. Per-client rate limits must use the validated client IP.
- No direct internet exposure of database files or database administration ports.
- Secrets and signing keys are server-side only.
- Idempotency and replay protection for payment/order endpoints.
- Backups, monitoring, incident response and patching procedures.
- Explicit retention, privacy and consent policies for customer data.

## Release security gates

Secret scanning, dependency review, authorization tests, license-tampering tests, backup protection checks and a documented update/recovery procedure are required before release.
