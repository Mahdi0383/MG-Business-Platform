using Mg.Platform.Api.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class PlatformSchemaReadinessHealthCheckTests
{
    [Fact]
    public async Task ReadinessIsHealthyWhenBusinessRegistryOutboxAndIdempotencyMigrationExist()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var check = new PlatformSchemaReadinessHealthCheck(context);

        var result = await check.CheckHealthAsync(CreateContext(check));

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenBusinessIdempotencyMigrationIsMissing()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await fixture.DropIdempotencyTableAsync();
        await using var context = fixture.CreateContext();
        var check = new PlatformSchemaReadinessHealthCheck(context);

        var result = await check.CheckHealthAsync(CreateContext(check));

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("schema is missing", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    private static HealthCheckContext CreateContext(IHealthCheck check)
    {
        var registration = new HealthCheckRegistration(
            "platform-schema",
            _ => check,
            HealthStatus.Unhealthy,
            tags: null,
            timeout: TimeSpan.FromSeconds(5));
        return new HealthCheckContext { Registration = registration };
    }
}
