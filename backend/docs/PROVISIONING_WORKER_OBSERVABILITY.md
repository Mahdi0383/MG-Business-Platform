# MG platform provisioning worker — observability contract

## Meter and instruments

The worker emits `System.Diagnostics.Metrics` instruments through meter `MG.Platform.Provisioning` (version `1.0.0`). The instruments are:

| Instrument | Type | Unit | Tags | Meaning |
|---|---|---|---|---|
| `mg.provisioning.jobs.claimed` | Counter | jobs | none | Valid fenced jobs claimed by this worker process. |
| `mg.provisioning.jobs.attempts` | Counter | attempts | `outcome` | A claimed job attempt finished, including success, cancellation and lease loss. |
| `mg.provisioning.jobs.duration` | Histogram | seconds | `outcome` | Time from valid claim through the attempt's completion/abandonment. |
| `mg.provisioning.lease.renewals` | Counter | renewals | `outcome` | Lease heartbeat renewal results. |
| `mg.provisioning.worker.iteration.failures` | Counter | failures | none | Unexpected worker-loop iteration failures. |

`jobs.attempts` and `jobs.duration` use only these bounded outcome values: `succeeded`, `lease_lost`, `cancelled`, `permanent_failure`, `retryable_failure`, `failed`. Lease renewal outcomes are `succeeded`, `lost` and `error`.

## Data minimization

Metric tags are deliberately low-cardinality. Never add tenant IDs, job IDs, worker IDs, database names, exception types/messages, SQL text, credential references, passwords, access tokens or connection strings. Error logs continue to record only safe exception type names and safe job identifiers where the existing operational policy permits them; logs and metrics have separate privacy rules.

The automated `MeterListener` integration test verifies that the worker emits claim, attempt, duration and successful-renewal measurements and that the only permitted tag key is `outcome`.

## Collection and alerting

The API currently creates the meter instruments but does not expose a public `/metrics` endpoint or configure an exporter. Deploy an approved OpenTelemetry .NET auto-instrumentation agent or an explicitly configured collector/exporter that subscribes to `MG.Platform.Provisioning`. Do not expose a metrics endpoint on the customer-facing network merely to collect these instruments.

Before enabling the worker in any environment, verify the collector receives the meter and that alert delivery is tested. Suggested initial alerts:

- Any increase in `mg.provisioning.worker.iteration.failures` over a five-minute window.
- Any `mg.provisioning.lease.renewals` with `outcome` equal to `lost` or `error`.
- Any `mg.provisioning.jobs.attempts` with `outcome` equal to `permanent_failure` or `failed`.
- Sustained p95 of `mg.provisioning.jobs.duration` above the tenant-provisioning service objective.
- Repeated claims without corresponding attempt-finished measurements, indicating a worker process interruption or telemetry gap.

Do not alert on absence of worker metrics while `Provisioning:WorkerEnabled=false`; that is the required default. Once a canary is explicitly enabled, alert thresholds and expected traffic must be tied to the approved canary schedule.

## Production acceptance still required

Automated instrumentation and its unit/integration assertion do not constitute production observability acceptance. The release record must include the deployed collector configuration, dashboard/query examples, alert rule IDs, a successful test notification, on-call owner, retention/access controls and evidence that no high-cardinality or sensitive fields are exported. Keep `Provisioning:WorkerEnabled=false` until all other database, secret-manager, mTLS, crash-recovery, backup/restore and operator gates are also approved.
