using MG.BusinessPlatform.Domain.Finance;
using MG.BusinessPlatform.Domain.Inventory;

namespace MG.BusinessPlatform.Domain.Tests.Inventory;

public sealed class InventoryValuationCalculatorTests
{
    [Fact]
    public void Calculate_ReturnsQuantityValueAndAverageCost()
    {
        var item = Guid.NewGuid();
        var location = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var movements = new[]
        {
            Movement(item, location, now.AddHours(-2), StockMovementType.PurchaseReceipt, 10m, 5m),
            Movement(item, location, now.AddHours(-1), StockMovementType.SaleIssue, 4m, 5m)
        };

        var result = InventoryValuationCalculator.Calculate(movements, item, location, now, "IRR");

        Assert.Equal(6m, result.QuantityOnHand);
        Assert.Equal(new Money(30m, "IRR"), result.InventoryValue);
        Assert.Equal(new Money(5m, "IRR"), result.AverageUnitCost);
        Assert.False(result.HasNegativeStock);
    }

    [Fact]
    public void Calculate_ExcludesOtherItemsLocationsAndFutureMovements()
    {
        var item = Guid.NewGuid();
        var location = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var movements = new[]
        {
            Movement(item, location, now.AddMinutes(-1), StockMovementType.OpeningBalance, 3m, 2m),
            Movement(Guid.NewGuid(), location, now, StockMovementType.OpeningBalance, 100m, 1m),
            Movement(item, Guid.NewGuid(), now, StockMovementType.OpeningBalance, 100m, 1m),
            Movement(item, location, now.AddMinutes(1), StockMovementType.PurchaseReceipt, 50m, 2m)
        };

        var result = InventoryValuationCalculator.Calculate(movements, item, location, now, "IRR");
        Assert.Equal(3m, result.QuantityOnHand);
        Assert.Equal(new Money(6m, "IRR"), result.InventoryValue);
    }

    [Fact]
    public void Calculate_RejectsMovementWithoutUnitCost()
    {
        var movement = new StockMovement(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            StockMovementType.OpeningBalance, 1m, "EA");
        Assert.Throws<InvalidOperationException>(() => InventoryValuationCalculator.Calculate(
            [movement], movement.ItemId, movement.LocationId, DateTimeOffset.UtcNow, "IRR"));
    }

    [Fact]
    public void Calculate_RejectsMixedValuationCurrencies()
    {
        var movement = Movement(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            StockMovementType.OpeningBalance, 1m, 5m, "USD");
        Assert.Throws<InvalidOperationException>(() => InventoryValuationCalculator.Calculate(
            [movement], movement.ItemId, movement.LocationId, DateTimeOffset.UtcNow, "IRR"));
    }

    private static StockMovement Movement(Guid item, Guid location, DateTimeOffset at,
        StockMovementType type, decimal quantity, decimal cost, string currency = "IRR") =>
        new(Guid.NewGuid(), item, location, at, type, quantity, "EA", unitCost: new Money(cost, currency));
}
