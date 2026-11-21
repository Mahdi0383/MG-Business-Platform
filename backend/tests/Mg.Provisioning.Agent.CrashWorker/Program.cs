using Mg.Provisioning.Agent.Core;
using Npgsql;

static string Required(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"Required crash-worker environment variable {name} is missing.");

var controlConnectionString = Required("MG_CRASH_WORKER_CONTROL_CONNECTION");
var tenantId = Guid.Parse(Required("MG_CRASH_WORKER_TENANT_ID"));
var idempotencyKey = Guid.Parse(Required("MG_CRASH_WORKER_IDEMPOTENCY_KEY"));
var boundary = Required("MG_CRASH_WORKER_BOUNDARY");
var applicationPassword = Required("MG_CRASH_WORKER_APP_PASSWORD");
var ownerPassword = Required("MG_CRASH_WORKER_OWNER_PASSWORD");
var databaseName = $"mg_tenant_{tenantId:N}";

var application = new NpgsqlConnectionStringBuilder(controlConnectionString)
{
    Database = databaseName,
    Username = $"{databaseName}_app",
    Password = applicationPassword,
    Pooling = true
};
var owner = new NpgsqlConnectionStringBuilder(controlConnectionString)
{
    Database = databaseName,
    Username = $"{databaseName}_owner",
    Password = ownerPassword,
    Pooling = false
};
var credentials = new AgentProvisioningCredentials(
    application.ConnectionString,
    owner.ConnectionString);

Func<string, Func<CancellationToken, Task>> pauseAt = target => async cancellationToken =>
{
    if (!string.Equals(boundary, target, StringComparison.Ordinal))
        return;

    Console.WriteLine($"MG_CRASH_BOUNDARY_REACHED:{target}");
    await Console.Out.FlushAsync(cancellationToken);
    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
};

var provisioner = new NpgsqlTenantResourceProvisioner(
    controlConnectionString,
    pauseAt("after-owner-role-created"),
    pauseAt("after-application-role-created"),
    pauseAt("after-owner-membership-ensured"),
    pauseAt("after-database-created-before-marker"),
    pauseAt("after-database-marker-written-before-access-configuration"),
    pauseAt("after-tenant-baseline-applied-before-runtime-grants"),
    pauseAt("after-owner-readiness-before-tenant-baseline"),
    pauseAt("after-runtime-grants-applied"));

await provisioner.EnsureDatabaseAndRolesAsync(
    tenantId, idempotencyKey, databaseName, credentials, CancellationToken.None);

var migration = TenantSchemaMigrationArtifact.LoadTenantCoreBaseline(AppContext.BaseDirectory);
await provisioner.ApplyTenantSchemaAsync(
    tenantId, credentials, migration, CancellationToken.None);

Console.WriteLine("MG_CRASH_WORKER_COMPLETED_WITHOUT_INJECTION");
