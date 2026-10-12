using MG.BusinessPlatform.Domain.Inventory;

namespace MG.BusinessPlatform.Domain.Tests.Inventory;

public sealed class StockMovementTests
{
    [Theory]
    [InlineData(StockMovementType.OpeningBalance, 5, 5)]
    [InlineData(StockMovementType.PurchaseReceipt, 5, 5)]
    [InlineData(StockMovementType.CustomerReturn, 5, 5)]
    [InlineData(StockMovementType.TransferIn, 5, 5)]
    [InlineData(StockMovementType.SaleIssue, 5, -5)]
    [InlineData(StockMovementType.SupplierReturn, 5, -5)]
    [InlineData(StockMovementType.NegativeAdjustment, 5, -5)]
    [InlineData(StockMovementType.TransferOut, 5, -5)]
    [InlineData(StockMovementType.PositiveAdjustment, 5, 5)]
    public void SignedQuantity_UsesMovementType(StockMovementType type, decimal quantity, decimal expected)
    {
        var movement = Create(type, quantity);
        Assert.Equal(expected, movement.SignedQuantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveQuantity(decimal quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(StockMovementType.OpeningBalance, quantity));
    }

    [Fact]
    public void Constructor_NormalizesUnitCodeAndReference()
    {
        var movement = Create(StockMovementType.PurchaseReceipt, 2m, " kg ", " PO-17 ");

        Assert.Equal("KG", movement.UnitCode);
        Assert.Equal("PO-17", movement.Reference);
    }

    [Fact]
    public void Constructor_RejectsEmptyItemId()
    {
        Assert.Throws<ArgumentException>(() => new StockMovement(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(),
            DateTimeOffset.UtcNow, StockMovementType.OpeningBalance, 1m, "EA"));
    }

    private static StockMovement Create(StockMovementType type, decimal quantity, string unit = "EA", string? reference = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, type, quantity, unit, reference);
}
