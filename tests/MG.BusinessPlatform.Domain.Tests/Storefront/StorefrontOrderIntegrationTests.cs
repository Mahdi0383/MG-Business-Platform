using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Domain.Tests.Storefront;

[Collection("StorefrontIntegration")]
public sealed class StorefrontOrderIntegrationTests
{
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
        private readonly string? _previousDatabasePath = Environment.GetEnvironmentVariable("MG_BUSINESS_DB_PATH");
        private readonly string _testDatabasePath = Path.Combine(Path.GetTempPath(), $"mg-storefront-{Guid.NewGuid():N}.db");

        public StorefrontTestFactory()
        {
            Environment.SetEnvironmentVariable("MG_BUSINESS_DB_PATH", _testDatabasePath);
        }

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
}

[CollectionDefinition("StorefrontIntegration", DisableParallelization = true)]
public sealed class StorefrontIntegrationCollection { }
