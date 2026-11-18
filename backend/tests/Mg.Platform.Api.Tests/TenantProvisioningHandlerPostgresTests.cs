using Mg.Platform.Api.Data;
using Mg.Platform.Api.Models;
using Mg.Platform.Api.Provisioning;
using Mg.Platform.Api.Secrets;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class TenantProvisioningHandlerPostgresTests
{
    [Fact]
    public async Task ProvisioningStoresSecretAndVerifiesTenantBeforeReturning()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.AddTenantAsync();
        var secretStore = new FakeSecretStore();
        var provisioner = new FakeProvisioner(ConnectionString(tenant.Id));
        await using var db = fixture.CreateContext();

        await new TenantProvisioningHandler(db, provisioner, secretStore)
            .ProvisionAsync(Job(tenant.Id), CancellationToken.None);

        Assert.Equal(1, provisioner.ProvisionCalls);
        Assert.Equal(1, provisioner.VerifyCalls);
        Assert.Equal(ConnectionString(tenant.Id), secretStore.Values[$"tenants/{tenant.Id:N}/postgres"]);
    }

    [Fact]
    public async Task RetryReusesVaultSecretAndDoesNotRecreateDatabase()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.AddTenantAsync();
        var secretStore = new FakeSecretStore();
        secretStore.Values[$"tenants/{tenant.Id:N}/postgres"] = ConnectionString(tenant.Id);
        var provisioner = new FakeProvisioner(ConnectionString(tenant.Id));
        await using var db = fixture.CreateContext();

        await new TenantProvisioningHandler(db, provisioner, secretStore)
            .ProvisionAsync(Job(tenant.Id), CancellationToken.None);

        Assert.Equal(0, provisioner.ProvisionCalls);
        Assert.Equal(1, provisioner.VerifyCalls);
    }

    [Fact]
    public async Task RejectsConnectionStringForDifferentDatabaseBeforeStoringSecret()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.AddTenantAsync();
        var secretStore = new FakeSecretStore();
        var wrongDatabase = $"Host=localhost;Database=other;Username=tenant;Password=secret;SSL Mode=VerifyFull";
        var provisioner = new FakeProvisioner(wrongDatabase);
        await using var db = fixture.CreateContext();

        var exception = await Assert.ThrowsAsync<TenantProvisioningException>(() =>
            new TenantProvisioningHandler(db, provisioner, secretStore)
                .ProvisionAsync(Job(tenant.Id), CancellationToken.None));

        Assert.Equal("TENANT_CONNECTION_STRING_INVALID", exception.SafeErrorCode);
        Assert.Empty(secretStore.Values);
        Assert.Equal(0, provisioner.VerifyCalls);
    }

    [Fact]
    public async Task RejectsSecretWhenVaultReadbackDoesNotMatchProvisionedValue()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.AddTenantAsync();
        var secretStore = new FakeSecretStore { CorruptOnStore = true };
        var provisioner = new FakeProvisioner(ConnectionString(tenant.Id));
        await using var db = fixture.CreateContext();

        var exception = await Assert.ThrowsAsync<TenantProvisioningException>(() =>
            new TenantProvisioningHandler(db, provisioner, secretStore)
                .ProvisionAsync(Job(tenant.Id), CancellationToken.None));

        Assert.Equal("TENANT_SECRET_READBACK_FAILED", exception.SafeErrorCode);
        Assert.Equal(0, provisioner.VerifyCalls);
    }

    [Fact]
    public async Task RejectsDatabaseWhenIndependentVerificationFails()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.AddTenantAsync();
        var secretStore = new FakeSecretStore();
        var provisioner = new FakeProvisioner(ConnectionString(tenant.Id), verificationResult: false);
        await using var db = fixture.CreateContext();

        var exception = await Assert.ThrowsAsync<TenantProvisioningException>(() =>
            new TenantProvisioningHandler(db, provisioner, secretStore)
                .ProvisionAsync(Job(tenant.Id), CancellationToken.None));

        Assert.Equal("TENANT_DATABASE_VERIFICATION_FAILED", exception.SafeErrorCode);
        Assert.Equal(1, provisioner.VerifyCalls);
    }

    [Fact]
    public async Task RejectsEncryptedConnectionWithoutServerCertificateValidation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.AddTenantAsync();
        var secretStore = new FakeSecretStore();
        var weakTls = $"Host=localhost;Database=mg_tenant_{tenant.Id:N};Username=tenant;Password=secret;SSL Mode=Require";
        var provisioner = new FakeProvisioner(weakTls);
        await using var db = fixture.CreateContext();

        var exception = await Assert.ThrowsAsync<TenantProvisioningException>(() =>
            new TenantProvisioningHandler(db, provisioner, secretStore)
                .ProvisionAsync(Job(tenant.Id), CancellationToken.None));

        Assert.Equal("TENANT_CONNECTION_STRING_INVALID", exception.SafeErrorCode);
        Assert.Empty(secretStore.Values);
    }

    [Fact]
    public async Task RejectsTenantNotInProvisioningState()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.AddTenantAsync("Active");
        await using var db = fixture.CreateContext();

        var exception = await Assert.ThrowsAsync<TenantProvisioningException>(() =>
            new TenantProvisioningHandler(db, new FakeProvisioner(ConnectionString(tenant.Id)),
                new FakeSecretStore()).ProvisionAsync(Job(tenant.Id), CancellationToken.None));

        Assert.Equal("TENANT_STATE_NOT_PROVISIONABLE", exception.SafeErrorCode);
    }

    private static TenantProvisioningJob Job(Guid tenantId) => new()
    {
        BusinessId = tenantId,
        IdempotencyKey = Guid.NewGuid()
    };

    private static string ConnectionString(Guid tenantId) =>
        $"Host=localhost;Database=mg_tenant_{tenantId:N};Username=tenant;Password=secret;SSL Mode=VerifyFull";

    private sealed class FakeSecretStore : ITenantSecretStore
    {
        public Dictionary<string, string> Values { get; } = new();
        public bool CorruptOnStore { get; init; }

        public Task StoreAsync(string secretReference, string secretValue, CancellationToken cancellationToken)
        {
            Values[secretReference] = CorruptOnStore ? "tampered-value" : secretValue;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(string secretReference, CancellationToken cancellationToken)
        {
            Values.TryGetValue(secretReference, out var value);
            return Task.FromResult(value);
        }

        public Task DeleteAsync(string secretReference, CancellationToken cancellationToken)
        {
            Values.Remove(secretReference);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProvisioner(string connectionString, bool verificationResult = true) : ITenantDatabaseProvisioner
    {
        public int ProvisionCalls { get; private set; }
        public int VerifyCalls { get; private set; }

        public Task<string> ProvisionAsync(
            TenantDatabaseProvisioningRequest request, CancellationToken cancellationToken)
        {
            ProvisionCalls++;
            Assert.StartsWith("mg_tenant_", request.DatabaseName);
            Assert.NotEqual(Guid.Empty, request.IdempotencyKey);
            return Task.FromResult(connectionString);
        }

        public Task<bool> VerifyAsync(
            string connectionString, Guid tenantId, CancellationToken cancellationToken)
        {
            VerifyCalls++;
            Assert.Equal(ConnectionString(tenantId), connectionString);
            return Task.FromResult(verificationResult);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string adminConnection;
        private readonly string schema;
        private readonly string tenantConnection;

        private Fixture(string adminConnection, string schema, string tenantConnection)
        {
            this.adminConnection = adminConnection;
            this.schema = schema;
            this.tenantConnection = tenantConnection;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable("MG_TEST_POSTGRES");
            if (string.IsNullOrWhiteSpace(configured))
                throw new InvalidOperationException("MG_TEST_POSTGRES must point to a disposable PostgreSQL test database.");

            var schema = "mg_handler_test_" + Guid.NewGuid().ToString("N");
            var builder = new NpgsqlConnectionStringBuilder(configured);
            var adminConnection = builder.ConnectionString;
            await using (var connection = new NpgsqlConnection(adminConnection))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand($@"CREATE SCHEMA ""{schema}""", connection);
                await command.ExecuteNonQueryAsync();
            }

            builder.SearchPath = schema;
            var tenantConnection = builder.ConnectionString;
            await using (var connection = new NpgsqlConnection(tenantConnection))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("""
                    CREATE TABLE business_tenants (
                        id uuid PRIMARY KEY,
                        name varchar(160) NOT NULL,
                        slug varchar(80) NOT NULL UNIQUE,
                        status varchar(24) NOT NULL,
                        database_secret_reference varchar(300) NOT NULL,
                        created_at_utc timestamptz NOT NULL,
                        activated_at_utc timestamptz NULL
                    );
                    """, connection);
                await command.ExecuteNonQueryAsync();
            }

            return new Fixture(adminConnection, schema, tenantConnection);
        }

        public PlatformDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(tenantConnection).Options);

        public async Task<BusinessTenant> AddTenantAsync(string status = "PendingProvisioning")
        {
            var tenantId = Guid.NewGuid();
            var tenant = new BusinessTenant
            {
                Id = tenantId,
                Name = "Test Tenant",
                Slug = "test-tenant-" + Guid.NewGuid().ToString("N")[..8],
                Status = status,
                DatabaseSecretReference = $"tenants/{tenantId:N}/postgres"
            };
            await using var context = CreateContext();
            context.Businesses.Add(tenant);
            await context.SaveChangesAsync();
            return tenant;
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnection);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($@"DROP SCHEMA IF EXISTS ""{schema}"" CASCADE", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
