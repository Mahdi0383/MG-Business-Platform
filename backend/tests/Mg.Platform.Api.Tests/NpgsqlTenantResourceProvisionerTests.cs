using Mg.Provisioning.Agent.Core;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class NpgsqlTenantResourceProvisionerTests
{
    [Theory]
    [InlineData("Disable")]
    [InlineData("Require")]
    public void RejectsControlConnectionWithoutCertificateValidation(string sslMode)
    {
        var control = $"Host=localhost;Database=postgres;Username=mg_agent_control;Password=not-a-real-secret;SSL Mode={sslMode}";
        Assert.Throws<ArgumentException>(() => new NpgsqlTenantResourceProvisioner(control));
    }

    [Fact]
    public void RejectsIncompleteControlConnection()
    {
        Assert.Throws<ArgumentException>(() =>
            new NpgsqlTenantResourceProvisioner(
                "Host=localhost;Database=postgres;Username=mg_agent_control;SSL Mode=VerifyFull"));
    }

    [Fact]
    public async Task RejectsApplicationRoleMismatchBeforeOpeningControlDatabase()
    {
        var provisioner = CreateProvisioner();
        var tenantId = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";
        var app = $"Host=localhost;Database={databaseName};Username=wrong_role;Password=not-a-real-secret;SSL Mode=VerifyFull";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provisioner.EnsureDatabaseAndRolesAsync(
                tenantId, Guid.NewGuid(), databaseName, app, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsApplicationConnectionToDifferentPostgresHost()
    {
        var provisioner = CreateProvisioner();
        var tenantId = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";
        var app = $"Host=untrusted-db.internal;Database={databaseName};Username={databaseName}_app;Password=not-a-real-secret;SSL Mode=VerifyFull";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provisioner.EnsureDatabaseAndRolesAsync(
                tenantId, Guid.NewGuid(), databaseName, app, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsApplicationConnectionWithoutStrictTls()
    {
        var provisioner = CreateProvisioner();
        var tenantId = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";
        var app = $"Host=localhost;Database={databaseName};Username={databaseName}_app;Password=not-a-real-secret;SSL Mode=Require";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provisioner.EnsureDatabaseAndRolesAsync(
                tenantId, Guid.NewGuid(), databaseName, app, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsInvalidTenantDatabaseIdentityBeforeConnecting()
    {
        var provisioner = CreateProvisioner();
        var tenantId = Guid.NewGuid();
        var app = $"Host=localhost;Database=mg_tenant_wrong;Username=mg_tenant_wrong_app;Password=not-a-real-secret;SSL Mode=VerifyFull";

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provisioner.EnsureDatabaseAndRolesAsync(
                tenantId, Guid.NewGuid(), "mg_tenant_wrong", app, CancellationToken.None));
    }

    private static NpgsqlTenantResourceProvisioner CreateProvisioner() =>
        new("Host=localhost;Database=postgres;Username=mg_agent_control;Password=not-a-real-secret;SSL Mode=VerifyFull");
}
