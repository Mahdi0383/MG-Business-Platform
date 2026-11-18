using Mg.Platform.Api.Data;
using Mg.Platform.Api.Endpoints;
using Mg.Platform.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class TenantProvisioningLeasePostgresTests
{
    [Fact]
    public async Task ConcurrentWorkersClaimDifferentJobs()
    {
        var configured = Environment.GetEnvironmentVariable("MG_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("MG_TEST_POSTGRES must be configured for PostgreSQL integration tests.");

        var schema = "mg_lease_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(configured);
        await admin.OpenAsync();
        await using (var createSchema = new NpgsqlCommand($@"CREATE SCHEMA ""{schema}""", admin))
            await createSchema.ExecuteNonQueryAsync();

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(configured) { SearchPath = schema };
            await using (var connection = new NpgsqlConnection(builder.ConnectionString))
            {
                await connection.OpenAsync();
                await using var createTables = new NpgsqlCommand("""
                    CREATE TABLE business_tenants (
                        id uuid PRIMARY KEY,
                        name varchar(160) NOT NULL,
                        slug varchar(80) NOT NULL UNIQUE,
                        status varchar(24) NOT NULL,
                        database_secret_reference varchar(300) NOT NULL,
                        created_at_utc timestamptz NOT NULL,
                        activated_at_utc timestamptz NULL
                    );
                    CREATE TABLE tenant_provisioning_jobs (
                        id uuid PRIMARY KEY,
                        business_id uuid NOT NULL REFERENCES business_tenants(id) ON DELETE RESTRICT,
                        status varchar(20) NOT NULL,
                        attempts integer NOT NULL,
                        idempotency_key uuid NOT NULL UNIQUE,
                        lease_owner varchar(120) NULL,
                        lease_expires_at_utc timestamptz NULL,
                        created_at_utc timestamptz NOT NULL,
                        next_attempt_at_utc timestamptz NOT NULL,
                        started_at_utc timestamptz NULL,
                        completed_at_utc timestamptz NULL,
                        last_error_code varchar(80) NULL,
                        CHECK ((lease_owner IS NULL) = (lease_expires_at_utc IS NULL))
                    );
                    """, connection);
                await createTables.ExecuteNonQueryAsync();
            }

            var options = new DbContextOptionsBuilder<PlatformDbContext>()
                .UseNpgsql(builder.ConnectionString)
                .Options;
            var businessOne = NewBusiness("one");
            var businessTwo = NewBusiness("two");
            await using (var seed = new PlatformDbContext(options))
            {
                seed.Businesses.AddRange(businessOne, businessTwo);
                seed.TenantProvisioningJobs.AddRange(NewJob(businessOne.Id), NewJob(businessTwo.Id));
                await seed.SaveChangesAsync();
            }

            async Task<TenantProvisioningJob?> Claim(string worker)
            {
                await using var context = new PlatformDbContext(options);
                return await new TenantProvisioningLeaseService(context)
                    .ClaimNextAsync(worker, TimeSpan.FromMinutes(1), CancellationToken.None);
            }

            var claims = await Task.WhenAll(Claim("worker-a"), Claim("worker-b"));
            Assert.All(claims, Assert.NotNull);
            Assert.NotEqual(claims[0]!.Id, claims[1]!.Id);
            Assert.Equal(new[] { "worker-a", "worker-b" }, claims.Select(job => job!.LeaseOwner).Order().ToArray());
        }
        finally
        {
            await using var dropSchema = new NpgsqlCommand($@"DROP SCHEMA IF EXISTS ""{schema}"" CASCADE", admin);
            await dropSchema.ExecuteNonQueryAsync();
        }
    }

    private static BusinessTenant NewBusiness(string slug) => new()
    {
        Name = slug,
        Slug = slug,
        Status = "PendingProvisioning",
        DatabaseSecretReference = $"tenants/{slug}/postgres"
    };

    private static TenantProvisioningJob NewJob(Guid businessId) => new()
    {
        BusinessId = businessId,
        Status = TenantProvisioningStatus.Pending
    };
}
