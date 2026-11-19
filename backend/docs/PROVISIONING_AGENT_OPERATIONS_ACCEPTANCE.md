# MG Provisioning Agent — Operations Acceptance Checklist

Status: **release blocked** until every mandatory gate below has dated evidence. This checklist supplements the implementation contract and runbook; it does not authorize production enablement.

## 1. Isolation and identity
- [ ] Deploy the agent as a separate service on a private network. Do not route it through the customer-facing ingress.
- [ ] Restrict inbound traffic to the MG Platform API workload identity and the configured mTLS client-certificate allowlist.
- [ ] Verify the server certificate chain, revocation status, SAN, validity, Server Authentication EKU and Digital Signature usage from the API container.
- [ ] Verify the client certificate chain, validity, Client Authentication EKU, Digital Signature usage and exact allowlist match.
- [ ] Confirm redirects and cookies are disabled on both the API-to-agent and Vault token HTTP clients; require bounded connect/request timeouts so bearer tokens are never forwarded to redirect targets.
- [ ] Confirm request-size limits and per-certificate rate limits are active at the deployed ingress and application layers.

## 2. PostgreSQL separation and least privilege
- [ ] Verify the control role has no incoming role memberships and no outgoing membership except PostgreSQL 17's automatic bootstrap ADMIN edge to UUID-derived, marker-verified tenant owner/application roles (superuser grantor, ADMIN=true, INHERIT=false, SET=false) and the provisioner's exact SET-only memberships to marked tenant-owner roles (control-role grantor, ADMIN=false, INHERIT=false, SET=true). Readiness must fail closed for every other membership, including unmarked bootstrap targets.
- [ ] Verify each tenant runtime application role has no outgoing membership and only the unavoidable incoming bootstrap ADMIN edge from the isolated control identity (superuser grantor, ADMIN=true, INHERIT=false, SET=false); it must have no SET/INHERIT path.
- [ ] Confirm the agent re-runs the complete live readiness/least-privilege check immediately before every provisioning operation; a previously healthy process must not continue after control-role privilege drift.
- [ ] Confirm credential-rewrap maintenance also re-runs the live readiness/least-privilege check before mutating the protected provisioning ledger.
- [ ] Create a dedicated agent-control database. It must not be the platform registry database or any tenant database.
- [ ] Apply the agent ledger migration using a separate migration identity.
- [ ] Configure the runtime control identity as non-superuser, NOINHERIT, non-replication and NOBYPASSRLS, with only the reviewed CREATEDB/CREATEROLE capabilities.
- [ ] Revoke all database privileges on the agent control database from PUBLIC (including PostgreSQL's default CONNECT and TEMP); grant the runtime control identity only CONNECT. Verify PUBLIC has no CONNECT, CREATE or TEMP privilege.
- [ ] Ensure the runtime control identity does not own the control database, mg_agent schema or ledger table.
- [ ] Grant only CONNECT, schema USAGE and SELECT/INSERT/UPDATE on the operation ledger. Do not grant TEMP, DELETE, TRUNCATE, schema CREATE or object ownership.
- [ ] Use SSL Mode=VerifyCA or VerifyFull with a trusted CA. Require without certificate validation is not accepted.
- [ ] Capture the live /health/ready response and the database privilege audit as release evidence.

## 3. Credential protection and recovery
- [ ] Inject the AES-256 key ring from a secret manager; do not store keys in source control, container layers or ordinary deployment variables.
- [ ] Verify credential envelopes use authenticated encryption and bind tenant UUID, idempotency key, database name and key ID.
- [ ] Perform a key-rotation rehearsal: add a new key, make it current, rewrap all rows, verify the old-key row count reaches zero, then retire the old key.
- [ ] Verify backups cover the agent ledger and key-version metadata. Demonstrate restore using the corresponding retained key material.
- [ ] Confirm logs, traces, metrics and error responses contain no passwords, connection strings, access tokens, SQL parameter values or decrypted credential payloads.

## 4. End-to-end fault injection
Run only against disposable PostgreSQL instances and isolated test certificates.
- [ ] Submit the same tenant/idempotency request concurrently; exactly one durable operation and one credential bundle must be created.
- [ ] Replay a completed request after simulating a lost HTTP response; the same application credential must be returned without repeating destructive side effects.
- [ ] Reuse an idempotency key for another tenant and reuse a tenant with a different key; both must be rejected.
- [ ] Terminate the agent immediately after database creation, role creation, schema application and privilege grants. A retry must reconcile safely at every boundary.
- [ ] Corrupt the tenant marker, migration checksum, owner, application role or runtime grants; independent verification must fail closed.
- [ ] Remove or rotate a required secret-manager key; readiness/provisioning must fail without exposing secret material.
- [ ] Expire/revoke the mTLS client certificate and verify the request is rejected before the handler runs.
- [ ] Interrupt the platform worker during provisioning; lease expiry and fencing must prevent a stale worker from completing a reclaimed job.

## 5. Platform worker rollout
- [ ] Keep Provisioning:WorkerEnabled=false until all agent, database, secret-manager and fault-injection gates above pass.
- [ ] Verify the platform API uses only the mTLS agent client credential and has no PostgreSQL cluster-administration credential.
- [ ] Enable the worker for one isolated canary tenant first. Confirm the tenant remains Pending until schema and runtime verification succeed.
- [ ] Confirm permanent failures are dead-lettered, transient failures are retried with bounded attempts, and stale lease tokens cannot mutate jobs.
- [ ] Confirm the canary can be disabled and safely resumed without duplicate databases, roles, credentials or tenant activation.

## 6. Evidence required before production approval
Attach all of the following to the release record:
1. Commit SHA and successful backend build/test workflow URLs for the exact release candidate.
2. Successful PostgreSQL 17 integration workflow URL for the exact same SHA.
3. Sanitized output of certificate and database privilege verification.
4. Fault-injection results for every boundary in section 4.
5. Secret-manager rotation and backup/restore evidence.
6. Deployment topology, firewall rules, monitoring alerts, on-call owner and rollback procedure.
7. Explicit approval from the platform owner.

A green unit-test workflow alone is not production approval. A cancelled, skipped or stale workflow is not evidence of a passing release candidate. Until all gates are evidenced, keep automated provisioning disabled and do not mark tenants Active based only on registry creation.