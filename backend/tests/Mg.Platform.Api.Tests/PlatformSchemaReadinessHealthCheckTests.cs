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

        var result = await CheckAsync(fixture);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenBusinessIdempotencyMigrationIsMissing()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await fixture.DropIdempotencyTableAsync();

        await AssertUnhealthyAsync(fixture);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenBusinessReferenceColumnIsMissing()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await fixture.DropIdempotencyBusinessIdColumnAsync();

        await AssertUnhealthyAsync(fixture);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenBusinessReferenceIsNullable()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await fixture.AllowNullIdempotencyBusinessIdAsync();

        await AssertUnhealthyAsync(fixture);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenBusinessReferenceForeignKeyIsMissing()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await fixture.DropIdempotencyBusinessForeignKeyAsync();

        await AssertUnhealthyAsync(fixture);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenCreationTimestampColumnIsMissing()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await fixture.DropIdempotencyCreatedAtColumnAsync();

        await AssertUnhealthyAsync(fixture);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenCreatorHashConstraintIsMissing()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await fixture.DropIdempotencyCreatorHashConstraintAsync();

        await AssertUnhealthyAsync(fixture);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenBusinessLookupIndexIsMissing()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await fixture.DropIdempotencyBusinessIndexAsync();

        await AssertUnhealthyAsync(fixture);
    }

    private static async Task AssertUnhealthyAsync(
        BusinessRegistryConcurrencyPostgresTests.DatabaseFixture fixture)
    {
        var result = await CheckAsync(fixture);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("schema is missing", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HealthCheckResult> CheckAsync(
        BusinessRegistryConcurrencyPostgresTests.DatabaseFixture fixture)
    {
        await using var context = fixture.CreateContext();
        var check = new PlatformSchemaReadinessHealthCheck(context);
        return await check.CheckHealthAsync(CreateContext(check));
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
