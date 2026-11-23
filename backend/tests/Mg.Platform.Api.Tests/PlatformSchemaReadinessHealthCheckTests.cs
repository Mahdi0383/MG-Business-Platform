using Mg.Platform.Api.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class PlatformSchemaReadinessHealthCheckTests
{
    [Fact]
    public async Task ReadinessIsHealthyWhenBusinessRegistryOutboxAndIdempotencyMigrationExist()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await CreateBusinessLookupIndexAsync(fixture);

        var result = await CheckAsync(fixture);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenBusinessIdempotencyMigrationIsMissing()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await CreateBusinessLookupIndexAsync(fixture);
        await fixture.DropIdempotencyTableAsync();

        await AssertUnhealthyAsync(fixture);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenBusinessReferenceColumnIsMissing()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await CreateBusinessLookupIndexAsync(fixture);
        await ExecuteSchemaCommandAsync(fixture,
            "ALTER TABLE business_creation_requests DROP COLUMN business_id");

        await AssertUnhealthyAsync(fixture);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenBusinessReferenceIsNullable()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await CreateBusinessLookupIndexAsync(fixture);
        await ExecuteSchemaCommandAsync(fixture,
            "ALTER TABLE business_creation_requests ALTER COLUMN business_id DROP NOT NULL");

        await AssertUnhealthyAsync(fixture);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenBusinessReferenceForeignKeyIsMissing()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await CreateBusinessLookupIndexAsync(fixture);
        await ExecuteSchemaCommandAsync(fixture,
            "ALTER TABLE business_creation_requests DROP CONSTRAINT business_creation_requests_business_id_fkey");

        await AssertUnhealthyAsync(fixture);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenCreationTimestampColumnIsMissing()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await CreateBusinessLookupIndexAsync(fixture);
        await ExecuteSchemaCommandAsync(fixture,
            "ALTER TABLE business_creation_requests DROP COLUMN created_at_utc");

        await AssertUnhealthyAsync(fixture);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenCreatorHashConstraintIsMissing()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await CreateBusinessLookupIndexAsync(fixture);
        await ExecuteSchemaCommandAsync(fixture,
            "ALTER TABLE business_creation_requests DROP CONSTRAINT ck_business_creation_creator_hash");

        await AssertUnhealthyAsync(fixture);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenBusinessLookupIndexIsMissing()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await CreateBusinessLookupIndexAsync(fixture);
        await ExecuteSchemaCommandAsync(fixture,
            "DROP INDEX ix_business_creation_requests_business");

        await AssertUnhealthyAsync(fixture);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenPrimaryKeyDoesNotBindIdempotencyKey()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await CreateBusinessLookupIndexAsync(fixture);
        await ExecuteSchemaCommandAsync(fixture,
            "ALTER TABLE business_creation_requests DROP CONSTRAINT pk_business_creation_requests");
        await ExecuteSchemaCommandAsync(fixture,
            "ALTER TABLE business_creation_requests ADD CONSTRAINT pk_business_creation_requests PRIMARY KEY (business_id)");

        await AssertUnhealthyAsync(fixture);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenCreatorHashConstraintHasAWeakExpression()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await CreateBusinessLookupIndexAsync(fixture);
        await ExecuteSchemaCommandAsync(fixture,
            "ALTER TABLE business_creation_requests DROP CONSTRAINT ck_business_creation_creator_hash");
        await ExecuteSchemaCommandAsync(fixture,
            "ALTER TABLE business_creation_requests ADD CONSTRAINT ck_business_creation_creator_hash CHECK (creator_identity_hash IS NOT NULL)");

        await AssertUnhealthyAsync(fixture);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenRequestHashConstraintHasAWeakExpression()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await CreateBusinessLookupIndexAsync(fixture);
        await ExecuteSchemaCommandAsync(fixture,
            "ALTER TABLE business_creation_requests DROP CONSTRAINT ck_business_creation_request_hash");
        await ExecuteSchemaCommandAsync(fixture,
            "ALTER TABLE business_creation_requests ADD CONSTRAINT ck_business_creation_request_hash CHECK (request_fingerprint IS NOT NULL)");

        await AssertUnhealthyAsync(fixture);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenHashConstraintIsNotValidated()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await CreateBusinessLookupIndexAsync(fixture);
        await ExecuteSchemaCommandAsync(fixture,
            "ALTER TABLE business_creation_requests DROP CONSTRAINT ck_business_creation_request_hash");
        await ExecuteSchemaCommandAsync(fixture,
            "ALTER TABLE business_creation_requests ADD CONSTRAINT ck_business_creation_request_hash CHECK (request_fingerprint ~ '^[0-9a-f]{64}$') NOT VALID");

        await AssertUnhealthyAsync(fixture);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenBusinessLookupIndexUsesTheWrongKeyColumns()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await CreateBusinessLookupIndexAsync(fixture);
        await ExecuteSchemaCommandAsync(fixture,
            "DROP INDEX ix_business_creation_requests_business");
        await ExecuteSchemaCommandAsync(fixture,
            "CREATE INDEX ix_business_creation_requests_business ON business_creation_requests (created_at_utc)");

        await AssertUnhealthyAsync(fixture);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenCreatorHashRegexIsAppliedToAConstant()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await CreateBusinessLookupIndexAsync(fixture);
        await ExecuteSchemaCommandAsync(fixture, "ALTER TABLE business_creation_requests DROP CONSTRAINT ck_business_creation_creator_hash");
        await ExecuteSchemaCommandAsync(fixture, "ALTER TABLE business_creation_requests ADD CONSTRAINT ck_business_creation_creator_hash CHECK (creator_identity_hash IS NOT NULL AND 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' ~ '^[0-9a-f]{64}$')");
        await AssertUnhealthyAsync(fixture);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenRequestHashRegexIsAppliedToAConstant()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        await CreateBusinessLookupIndexAsync(fixture);
        await ExecuteSchemaCommandAsync(fixture, "ALTER TABLE business_creation_requests DROP CONSTRAINT ck_business_creation_request_hash");
        await ExecuteSchemaCommandAsync(fixture, "ALTER TABLE business_creation_requests ADD CONSTRAINT ck_business_creation_request_hash CHECK (request_fingerprint IS NOT NULL AND 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' ~ '^[0-9a-f]{64}$')");
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

    private static async Task CreateBusinessLookupIndexAsync(
        BusinessRegistryConcurrencyPostgresTests.DatabaseFixture fixture) =>
        await ExecuteSchemaCommandAsync(fixture, """
            CREATE INDEX ix_business_creation_requests_business
                ON business_creation_requests (business_id, created_at_utc)
            """);

    private static async Task ExecuteSchemaCommandAsync(
        BusinessRegistryConcurrencyPostgresTests.DatabaseFixture fixture,
        string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.TestConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
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
