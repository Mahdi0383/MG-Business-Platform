# Isolated MG provisioning agent API

This is a separate, internal-only HTTPS host. It is not part of the customer-facing MG Platform API and must not be exposed through a public reverse proxy.

## Startup configuration

Inject these values through the deployment secret manager/environment; never commit certificate passwords or private keys:

- `Agent__ListenPort`: internal TLS port (default deployment choice: 9443).
- `Agent__ServerCertificatePath`: read-only mounted PKCS#12 server certificate path.
- `Agent__ServerCertificatePassword`: runtime secret.
- `Agent__AllowedClientThumbprints`: comma-separated explicit SHA-1 certificate thumbprints for approved MG API client certificates.

Startup fails closed unless the port, server certificate and non-empty client thumbprint allowlist are valid. The server certificate must have a private key, be in date, be a non-CA certificate and include Server Authentication EKU plus Digital Signature key usage. Every incoming TLS connection requires a client certificate; Kestrel validates its chain/revocation, while the application additionally requires Client Authentication EKU, Digital Signature, non-CA status, validity and an explicit thumbprint allowlist match.

The HTTP surface is intentionally narrow:
- `GET /health/live`
- `GET /health/ready`
- `POST /internal/v1/tenant-databases`

The POST endpoint accepts only the exact JSON fields `tenantId`, `idempotencyKey` and `databaseName`; rejects unknown/duplicate properties and mismatched UUID-derived database names; caps request bodies at 4 KiB; and rate-limits each client certificate. Error responses contain stable codes only. Exception messages, SQL and credentials are never logged.

## Not production-ready yet

No `IAgentProvisioningHandler` is registered. Therefore readiness returns 503 and provisioning requests return 503 even after valid mTLS authentication. This is deliberate: the host must never return a fabricated connection string or report success until the real restricted PostgreSQL provisioner, exact tenant migration runner, independent runtime verification, secret-manager key loading and crash recovery are implemented and tested. Do not deploy or enable the platform worker yet.
