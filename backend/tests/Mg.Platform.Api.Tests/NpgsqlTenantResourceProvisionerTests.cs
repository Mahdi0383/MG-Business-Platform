using Mg.Provisioning.Agent.Core;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class NpgsqlTenantResourceProvisionerTests
{
    [Theory]
    [InlineData("Disable")]
    [InlineData("Require")]
    [InlineData("VerifyFull;Trust Server Certificate=true")]
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
        var credentials = Credentials(databaseName,
            $"Host=localhost;Database={databaseName};Username=wrong_role;Password=not-a-real-secret;SSL Mode=VerifyFull");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provisioner.EnsureDatabaseAndRolesAsync(
                tenantId, Guid.NewGuid(), databaseName, credentials, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsApplicationConnectionToDifferentPostgresHost()
    {
        var provisioner = CreateProvisioner();
        var tenantId = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";
        var credentials = Credentials(databaseName,
            $"Host=untrusted-db.internal;Database={databaseName};Username={databaseName}_app;Password=not-a-real-secret;SSL Mode=VerifyFull");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provisioner.EnsureDatabaseAndRolesAsync(
                tenantId, Guid.NewGuid(), databaseName, credentials, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsApplicationConnectionWithoutStrictTls()
    {
        var provisioner = CreateProvisioner();
        var tenantId = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";
        var credentials = Credentials(databaseName,
            $"Host=localhost;Database={databaseName};Username={databaseName}_app;Password=not-a-real-secret;SSL Mode=Require");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provisioner.EnsureDatabaseAndRolesAsync(
                tenantId, Guid.NewGuid(), databaseName, credentials, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsApplicationConnectionThatTrustsAnyServerCertificate()
    {
        var provisioner = CreateProvisioner();
        var tenantId = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";
        var credentials = Credentials(databaseName,
            $"Host=localhost;Database={databaseName};Username={databaseName}_app;Password=app-secret;SSL Mode=VerifyFull;Trust Server Certificate=true");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provisioner.EnsureDatabaseAndRolesAsync(
                tenantId, Guid.NewGuid(), databaseName, credentials, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsOwnerConnectionThatTrustsAnyServerCertificate()
    {
        var provisioner = CreateProvisioner();
        var tenantId = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";
        var valid = Credentials(databaseName,
            $"Host=localhost;Database={databaseName};Username={databaseName}_app;Password=app-secret;SSL Mode=VerifyFull");
        var invalid = valid with
        {
            OwnerConnectionString =
                $"Host=localhost;Database={databaseName};Username={databaseName}_owner;Password=owner-secret;SSL Mode=VerifyFull;Trust Server Certificate=true"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provisioner.EnsureDatabaseAndRolesAsync(
                tenantId, Guid.NewGuid(), databaseName, invalid, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsOwnerCredentialWithWrongDeterministicRole()
    {
        var provisioner = CreateProvisioner();
        var tenantId = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";
        var valid = Credentials(databaseName,
            $"Host=localhost;Database={databaseName};Username={databaseName}_app;Password=app-secret;SSL Mode=VerifyFull");
        var invalid = valid with
        {
            OwnerConnectionString =
                $"Host=localhost;Database={databaseName};Username=wrong_owner;Password=owner-secret;SSL Mode=VerifyFull"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provisioner.EnsureDatabaseAndRolesAsync(
                tenantId, Guid.NewGuid(), databaseName, invalid, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsOwnerCredentialWithoutStrictTls()
    {
        var provisioner = CreateProvisioner();
        var tenantId = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";
        var valid = Credentials(databaseName,
            $"Host=localhost;Database={databaseName};Username={databaseName}_app;Password=app-secret;SSL Mode=VerifyFull");
        var invalid = valid with
        {
            OwnerConnectionString =
                $"Host=localhost;Database={databaseName};Username={databaseName}_owner;Password=owner-secret;SSL Mode=Require"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provisioner.EnsureDatabaseAndRolesAsync(
                tenantId, Guid.NewGuid(), databaseName, invalid, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsOwnerCredentialWithNonDeterministicRoleName()
    {
        var provisioner = CreateProvisioner();
        var tenantId = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";
        var app = $"Host=localhost;Database={databaseName};Username={databaseName}_app;Password=app-secret;SSL Mode=VerifyFull";
        var owner = $"Host=localhost;Database={databaseName};Username=wrong_owner;Password=owner-secret;SSL Mode=VerifyFull";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provisioner.EnsureDatabaseAndRolesAsync(
                tenantId, Guid.NewGuid(), databaseName,
                new AgentProvisioningCredentials(app, owner), CancellationToken.None));
    }

    [Fact]
    public async Task RejectsOwnerCredentialToDifferentPostgresHost()
    {
        var provisioner = CreateProvisioner();
        var tenantId = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";
        var app = $"Host=localhost;Database={databaseName};Username={databaseName}_app;Password=app-secret;SSL Mode=VerifyFull";
        var owner = $"Host=untrusted-db.internal;Database={databaseName};Username={databaseName}_owner;Password=owner-secret;SSL Mode=VerifyFull";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provisioner.EnsureDatabaseAndRolesAsync(
                tenantId, Guid.NewGuid(), databaseName,
                new AgentProvisioningCredentials(app, owner), CancellationToken.None));
    }

    [Fact]
    public async Task RejectsOwnerCredentialWithoutStrictTls()
    {
        var provisioner = CreateProvisioner();
        var tenantId = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";
        var app = $"Host=localhost;Database={databaseName};Username={databaseName}_app;Password=app-secret;SSL Mode=VerifyFull";
        var owner = $"Host=localhost;Database={databaseName};Username={databaseName}_owner;Password=owner-secret;SSL Mode=Require";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provisioner.EnsureDatabaseAndRolesAsync(
                tenantId, Guid.NewGuid(), databaseName,
                new AgentProvisioningCredentials(app, owner), CancellationToken.None));
    }

    [Fact]
    public async Task RejectsInvalidTenantDatabaseIdentityBeforeConnecting()
    {
        var provisioner = CreateProvisioner();
        var tenantId = Guid.NewGuid();
        var credentials = Credentials("mg_tenant_wrong",
            "Host=localhost;Database=mg_tenant_wrong;Username=mg_tenant_wrong_app;Password=not-a-real-secret;SSL Mode=VerifyFull");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provisioner.EnsureDatabaseAndRolesAsync(
                tenantId, Guid.NewGuid(), "mg_tenant_wrong", credentials, CancellationToken.None));
    }

    private static AgentProvisioningCredentials Credentials(string databaseName, string applicationConnectionString) =>
        new(applicationConnectionString,
            $"Host=localhost;Database={databaseName};Username={databaseName}_owner;Password=owner-secret;SSL Mode=VerifyFull");

    private static NpgsqlTenantResourceProvisioner CreateProvisioner() =>
        new("Host=localhost;Database=postgres;Username=mg_agent_control;Password=not-a-real-secret;SSL Mode=VerifyFull");
}
