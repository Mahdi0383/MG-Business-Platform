using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MG.BusinessPlatform.Licensing;
using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Domain.Tests.Storefront;

[Collection("StorefrontIntegration")]
public sealed class StorefrontOrderIntegrationTests
{
    [Fact]
    public async Task PublicHealthEndpoint_IsRateLimitedPerClient()
    {
        await using var factory = new StorefrontTestFactory();
        using var client = factory.CreateClient();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 40).Select(_ => client.GetAsync("/health/live")));
        try
        {
            Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.TooManyRequests);
            Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.OK);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task CustomerStorefront_UsesJsonFieldCasingForAvailabilityAndCurrency()
    {
        await using var factory = new StorefrontTestFactory();
        using var client = factory.CreateClient();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
            var settings = await db.StoreSettings.SingleAsync(x => x.Id == 1);
            settings.CurrencyCode = "IRR";
            settings.OnlineOrderingEnabled = true;
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync("/api/storefront");
        response.EnsureSuccessStatusCode();
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("onlineOrderingEnabled").GetBoolean());
        Assert.Equal("IRR", json.RootElement.GetProperty("currencyCode").GetString());

        using var pageResponse = await client.GetAsync("/");
        pageResponse.EnsureSuccessStatusCode();
        Assert.Contains("frame-ancestors 'none'", pageResponse.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        var html = await pageResponse.Content.ReadAsStringAsync();
        Assert.Contains("!info.onlineOrderingEnabled", html, StringComparison.Ordinal);
        Assert.Contains("fmt(total,info.currencyCode)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("!info.OnlineOrderingEnabled", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductCatalog_IsHiddenWhenOnlineOrderingIsDisabled()
    {
        await using var factory = new StorefrontTestFactory();
        using var client = factory.CreateClient();
        var productId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
            var settings = await db.StoreSettings.SingleAsync(x => x.Id == 1);
            settings.OnlineOrderingEnabled = false;
            db.StoreProducts.Add(new StoreProduct
            {
                Id = productId,
                Sku = "PRIVATE-CATALOG-ITEM",
                Name = "Private catalogue item",
                Description = "Must not be publicly disclosed",
                UnitPrice = 123m,
                CurrencyCode = "IRR",
                IsAvailable = true,
                StockQuantity = 7,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync("/api/storefront/products");
        response.EnsureSuccessStatusCode();
        var products = await response.Content.ReadFromJsonAsync<ProductResponse[]>();

        Assert.NotNull(products);
        Assert.Empty(products);
    }

    [Fact]
    public async Task PublicOrder_RecalculatesPriceAndRetryDoesNotDuplicateOrderOrStockDeduction()
    {
        await using var factory = new StorefrontTestFactory();
        using var client = factory.CreateClient();
        var productId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
            var settings = await db.StoreSettings.SingleAsync(x => x.Id == 1);
            settings.StoreName = "Integration Store";
            settings.CurrencyCode = "IRR";
            settings.OnlineOrderingEnabled = true;
            db.StoreProducts.Add(new StoreProduct
            {
                Id = productId,
                Sku = "TEST-ITEM",
                Name = "Test Item",
                Description = "Test",
                UnitPrice = 17.25m,
                CurrencyCode = "IRR",
                IsAvailable = true,
                StockQuantity = 5,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var payload = new
        {
            customerName = "Test Customer",
            customerPhone = "09120000000",
            deliveryAddress = "Test Address",
            notes = "",
            items = new[] { new { productId, quantity = 2, price = 0.01m } }
        };
        const string idempotencyKey = "integration-retry-key-000001";
        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/storefront/orders")
        {
            Content = JsonContent.Create(payload)
        };
        firstRequest.Headers.Add("Idempotency-Key", idempotencyKey);

        using var firstResponse = await client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(firstBody);
        Assert.Equal(34.50m, firstBody.Total);
        Assert.Equal("IRR", firstBody.CurrencyCode);

        using var retryRequest = new HttpRequestMessage(HttpMethod.Post, "/api/storefront/orders")
        {
            Content = JsonContent.Create(payload)
        };
        retryRequest.Headers.Add("Idempotency-Key", idempotencyKey);
        using var retryResponse = await client.SendAsync(retryRequest);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        var retryBody = await retryResponse.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(retryBody);
        Assert.Equal(firstBody.Id, retryBody.Id);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        Assert.Equal(1, await verificationDb.CustomerOrders.CountAsync());
        Assert.Equal(3, (await verificationDb.StoreProducts.SingleAsync(x => x.Id == productId)).StockQuantity);
    }

    [Fact]
    public async Task ConcurrentRetries_WithSameIdempotencyKeyCreateOnlyOneOrder()
    {
        await using var factory = new StorefrontTestFactory();
        using var client = factory.CreateClient();
        var productId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
            var settings = await db.StoreSettings.SingleAsync(x => x.Id == 1);
            settings.OnlineOrderingEnabled = true;
            db.StoreProducts.Add(new StoreProduct
            {
                Id = productId, Sku = "TEST-CONCURRENT", Name = "Concurrent item",
                UnitPrice = 12m, UnitCost = 5m, CurrencyCode = "IRR",
                IsAvailable = true, StockQuantity = 10, UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        const string key = "integration-concurrent-retry-0001";
        var payload = new
        {
            customerName = "Concurrent Customer",
            customerPhone = "09120000000",
            items = new[] { new { productId, quantity = 2 } }
        };

        async Task<HttpResponseMessage> SubmitAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/storefront/orders")
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request);
        }

        var responses = await Task.WhenAll(SubmitAsync(), SubmitAsync());
        using var first = responses[0];
        using var second = responses[1];
        Assert.Contains(first.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
        Assert.Contains(second.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });

        var firstBody = await first.Content.ReadFromJsonAsync<OrderResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(firstBody);
        Assert.NotNull(secondBody);
        Assert.Equal(firstBody.Id, secondBody.Id);
        Assert.Equal(24m, firstBody.Total);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        Assert.Equal(1, await verificationDb.CustomerOrders.CountAsync());
        Assert.Equal(8, (await verificationDb.StoreProducts.SingleAsync(x => x.Id == productId)).StockQuantity);
        Assert.Equal(1, await verificationDb.StockMovements.CountAsync());
    }

    [Fact]
    public async Task PublicOrder_AggregatesDuplicateProductLinesBeforeStockValidation()
    {
        await using var factory = new StorefrontTestFactory();
        using var client = factory.CreateClient();
        var productId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
            var settings = await db.StoreSettings.SingleAsync(x => x.Id == 1);
            settings.OnlineOrderingEnabled = true;
            db.StoreProducts.Add(new StoreProduct
            {
                Id = productId, Sku = "TEST-DUPLICATE", Name = "Limited stock",
                UnitPrice = 10m, UnitCost = 4m, CurrencyCode = "IRR",
                IsAvailable = true, StockQuantity = 5, UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/storefront/orders")
        {
            Content = JsonContent.Create(new
            {
                customerName = "Test Customer",
                customerPhone = "09120000000",
                items = new[]
                {
                    new { productId, quantity = 3, price = 0m },
                    new { productId, quantity = 3, price = 0m }
                }
            })
        };
        request.Headers.Add("Idempotency-Key", "integration-duplicate-stock-0001");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        Assert.Empty(await verificationDb.CustomerOrders.ToListAsync());
        Assert.Empty(await verificationDb.StockMovements.ToListAsync());
        Assert.Equal(5, (await verificationDb.StoreProducts.SingleAsync(x => x.Id == productId)).StockQuantity);
    }

    [Fact]
    public async Task PublicReservation_RejectsOverlapsAndRetriesIdempotently()
    {
        await using var factory = new StorefrontTestFactory();
        using var client = factory.CreateClient();
        var resourceId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var startsAt = DateTimeOffset.UtcNow.AddHours(2);
        var endsAt = startsAt.AddHours(1);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
            var settings = await db.StoreSettings.SingleAsync(x => x.Id == 1);
            settings.OnlineReservationsEnabled = true;
            db.ReservationResources.Add(new ReservationResourceRecord
            {
                Id = resourceId, BranchId = branchId, Name = "Station 1",
                ResourceType = "GameStation", Capacity = 2, IsAvailable = true
            });
            await db.SaveChangesAsync();
        }

        using var resourcesResponse = await client.GetAsync("/api/storefront/reservation-resources");
        resourcesResponse.EnsureSuccessStatusCode();
        var resources = await resourcesResponse.Content.ReadFromJsonAsync<List<ReservationResourceResponse>>();
        Assert.NotNull(resources);
        Assert.Contains(resources, resource => resource.Id == resourceId);

        var payload = new
        {
            resourceId,
            guestName = "Reservation Guest",
            guestPhone = "09120000000",
            guestCount = 2,
            startsAt,
            endsAt,
            notes = "Test"
        };
        const string key = "integration-reservation-key-0001";

        async Task<HttpResponseMessage> SubmitAsync(string idempotencyKey)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/storefront/reservations")
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            return await client.SendAsync(request);
        }

        using var first = await SubmitAsync(key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.NotNull(firstBody);
        Assert.Equal("Requested", firstBody.Status);

        using var retry = await SubmitAsync(key);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var retryBody = await retry.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.NotNull(retryBody);
        Assert.Equal(firstBody.Id, retryBody.Id);

        using var mismatchedRetry = new HttpRequestMessage(HttpMethod.Post, "/api/storefront/reservations")
        {
            Content = JsonContent.Create(new
            {
                resourceId,
                guestName = "Different Guest",
                guestPhone = "09120000000",
                guestCount = 2,
                startsAt,
                endsAt,
                notes = "Test"
            })
        };
        mismatchedRetry.Headers.Add("Idempotency-Key", key);
        using var mismatchedResponse = await client.SendAsync(mismatchedRetry);
        Assert.Equal(HttpStatusCode.Conflict, mismatchedResponse.StatusCode);

        using var overlap = await SubmitAsync("integration-reservation-key-0002");
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        Assert.Equal(1, await verificationDb.CustomerReservations.CountAsync());
    }

    [Fact]
    public async Task ReservationEndpoint_RequiresReservationsLicenseEntitlement()
    {
        await using var factory = new StorefrontTestFactory(["Core", "Storefront"]);
        using var client = factory.CreateClient();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
            var settings = await db.StoreSettings.SingleAsync(x => x.Id == 1);
            settings.OnlineReservationsEnabled = true;
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync("/api/storefront/reservation-resources");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PublicReservation_RequiresFeatureEnabledAndValidTimeWindow()
    {
        await using var factory = new StorefrontTestFactory();
        using var client = factory.CreateClient();
        var resourceId = Guid.NewGuid();

        using var disabledResponse = await client.PostAsJsonAsync("/api/storefront/reservations", new
        {
            resourceId,
            guestName = "Guest",
            guestPhone = "09120000000",
            guestCount = 1,
            startsAt = DateTimeOffset.UtcNow.AddHours(2),
            endsAt = DateTimeOffset.UtcNow.AddHours(3),
            notes = ""
        });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabledResponse.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
            var settings = await db.StoreSettings.SingleAsync(x => x.Id == 1);
            settings.OnlineReservationsEnabled = true;
            db.ReservationResources.Add(new ReservationResourceRecord
            {
                Id = resourceId, BranchId = Guid.NewGuid(), Name = "Station 2",
                ResourceType = "GameStation", Capacity = 1, IsAvailable = true
            });
            await db.SaveChangesAsync();
        }

        using var invalidRequest = new HttpRequestMessage(HttpMethod.Post, "/api/storefront/reservations")
        {
            Content = JsonContent.Create(new
            {
                resourceId,
                guestName = "Guest",
                guestPhone = "09120000000",
                guestCount = 1,
                startsAt = DateTimeOffset.UtcNow.AddMinutes(1),
                endsAt = DateTimeOffset.UtcNow.AddMinutes(31),
                notes = ""
            })
        };
        invalidRequest.Headers.Add("Idempotency-Key", "integration-reservation-invalid-0001");
        using var invalidResponse = await client.SendAsync(invalidRequest);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
    }

    [Fact]
    public async Task Storefront_RejectsUnexpectedHostHeader()
    {
        await using var factory = new StorefrontTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = "unexpected.example";
        using var response = await client.GetAsync("/api/storefront/products");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OrderEndpoint_RejectsMissingIdempotencyKey()
    {
        await using var factory = new StorefrontTestFactory();
        using var client = factory.CreateClient();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
            var settings = await db.StoreSettings.SingleAsync(x => x.Id == 1);
            settings.OnlineOrderingEnabled = true;
            await db.SaveChangesAsync();
        }

        using var response = await client.PostAsJsonAsync("/api/storefront/orders", new
        {
            customerName = "Customer",
            customerPhone = "09120000000",
            items = new[] { new { productId = Guid.NewGuid(), quantity = 1 } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class StorefrontTestFactory : WebApplicationFactory<Program>
    {
        private readonly string[] _modules;

        public StorefrontTestFactory(string[]? modules = null)
        {
            _modules = modules ?? LicensePayload.SupportedModules;
            Environment.SetEnvironmentVariable("MG_BUSINESS_DB_PATH", _testDatabasePath);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStorefrontLicenseGuard>();
                services.AddSingleton<IStorefrontLicenseGuard>(_ => new TestStorefrontLicenseGuard(_modules));
            });
        }

        private sealed class TestStorefrontLicenseGuard(string[] modules) : IStorefrontLicenseGuard
        {
            private readonly LicenseValidationResult _license = new(

                LicenseValidationStatus.Valid,
                "Integration-test license",
                new LicensePayload
                {
                    LicenseId = Guid.NewGuid(),
                    CustomerName = "Integration Test",
                    SoftwareSerial = "MGS-00000000000000000000000000000000",
                    HardwareSerial = "MGH-00000000000000000000000000000000",
                    IssuedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                    ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
                    Modules = modules,
                    MaxBranches = 100,
                    MaxUsers = 100
                });

            public LicenseValidationResult Validate() => _license;
        }

        private readonly string? _previousDatabasePath = Environment.GetEnvironmentVariable("MG_BUSINESS_DB_PATH");
        private readonly string _testDatabasePath = Path.Combine(Path.GetTempPath(), $"mg-storefront-{Guid.NewGuid():N}.db");

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            Environment.SetEnvironmentVariable("MG_BUSINESS_DB_PATH", _previousDatabasePath);
            foreach (var path in new[] { _testDatabasePath, _testDatabasePath + "-shm", _testDatabasePath + "-wal" })
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private sealed record OrderResponse(Guid Id, string OrderNumber, decimal Total, string CurrencyCode, string Status);
    private sealed record ReservationResponse(Guid Id, string ReservationNumber, Guid ResourceId, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, string Status);
    private sealed record ReservationResourceResponse(Guid Id, string Name, string ResourceType, int Capacity);
}

[CollectionDefinition("StorefrontIntegration", DisableParallelization = true)]
public sealed class StorefrontIntegrationCollection { }
