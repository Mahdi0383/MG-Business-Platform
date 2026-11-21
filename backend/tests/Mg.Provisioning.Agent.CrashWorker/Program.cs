using Mg.Provisioning.Agent.Core;
using Npgsql;

static string Required(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"Required crash-worker environment variable {name} is missing.");

static Func<CancellationToken, Task> PauseAt(string boundary) => async cancellationToken =>
{
    if (!string.Equals(Required("MG_CRASH_WORKER_BOUNDARY"), boundary, StringComparison.Ordinal))
        return;

    Console.WriteLine($"MG_CRASH_BOUNDARY_REACHED:{boundary}");
    await Console.Out.FlushAsync(cancellationToken);
    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
};

var mode = Environment.GetEnvironmentVariable("MG_CRASH_WORKER_MODE") ?? "tenant";
var controlConnectionString = Required("MG_CRASH_WORKER_CONTROL_CONNECTION");
var tenantId = Guid.Parse(Required("MG_CRASH_WORKER_TENANT_ID"));
var idempotencyKey = Guid.Parse(Required("MG_CRASH_WORKER_IDEMPOTENCY_KEY"));
var databaseName = $"mg_tenant_{tenantId:N}";

if (string.Equals(mode, "tenant", StringComparison.Ordinal))
{
    var boundary = Required("MG_CRASH_WORKER_BOUNDARY");
    var application = new NpgsqlConnectionStringBuilder(controlConnectionString)
    {
        Database = databaseName,
        Username = $"{databaseName}_app",
        Password = Required("MG_CRASH_WORKER_APP_PASSWORD"),
        Pooling = true
    };
    var owner = new NpgsqlConnectionStringBuilder(controlConnectionString)
    {
        Database = databaseName,
        Username = $"{databaseName}_owner",
        Password = Required("MG_CRASH_WORKER_OWNER_PASSWORD"),
        Pooling = false
    };
    var credentials = new AgentProvisioningCredentials(
        application.ConnectionString,
        owner.ConnectionString);

    var provisioner = new NpgsqlTenantResourceProvisioner(
        controlConnectionString,
        PauseAt("after-owner-role-created"),
        PauseAt("after-application-role-created"),
        PauseAt("after-owner-membership-ensured"),
        PauseAt("after-database-created-before-marker"),
        PauseAt("after-database-marker-written-before-access-configuration"),
        PauseAt("after-tenant-baseline-applied-before-runtime-grants"),
        PauseAt("after-owner-readiness-before-tenant-baseline"),
        PauseAt("after-runtime-grants-applied"));

    await provisioner.EnsureDatabaseAndRolesAsync(
        tenantId, idempotencyKey, databaseName, credentials, CancellationToken.None);

    var migration = TenantSchemaMigrationArtifact.LoadTenantCoreBaseline(AppContext.BaseDirectory);
    await provisioner.ApplyTenantSchemaAsync(
        tenantId, credentials, migration, CancellationToken.None);

    Console.WriteLine("MG_CRASH_WORKER_COMPLETED_WITHOUT_INJECTION");
    return;
}

var currentKeyId = Required("MG_CRASH_WORKER_CURRENT_KEY_ID");
var keyRing = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["old"] = Required("MG_CRASH_WORKER_KEY_OLD"),
    ["new"] = Required("MG_CRASH_WORKER_KEY_NEW")
};
using var protector = new AgentCredentialProtector(currentKeyId, keyRing);
await using var dataSource = NpgsqlDataSource.Create(controlConnectionString);

var store = new AgentProvisioningOperationStore(
    dataSource,
    protector,
    afterPreparedCredentialInserted: PauseAt("after-prepared-credential-inserted"),
    afterCredentialRewrapped: PauseAt("after-credential-rewrapped"),
    afterPhaseUpdated: PauseAt("after-phase-updated"));

switch (mode)
{
    case "ledger-prepare":
        var credentials = new AgentProvisioningCredentials(
            Required("MG_CRASH_WORKER_APPLICATION_CONNECTION"),
            Required("MG_CRASH_WORKER_OWNER_CONNECTION"));
        await store.GetOrCreatePreparedCredentialsAsync(
            tenantId,
            idempotencyKey,
            databaseName,
            _ => ValueTask.FromResult(credentials),
            CancellationToken.None);
        break;

    case "ledger-rewrap":
        await store.RewrapStoredCredentialsAsync(100, CancellationToken.None);
        break;

    case "ledger-phase":
        await store.AdvancePhaseAsync(
            tenantId, idempotencyKey, "Prepared", "DatabaseCreated", CancellationToken.None);
        break;

    default:
        throw new InvalidOperationException($"Unsupported crash-worker mode '{mode}'.");
}

Console.WriteLine("MG_CRASH_WORKER_COMPLETED_WITHOUT_INJECTION");
