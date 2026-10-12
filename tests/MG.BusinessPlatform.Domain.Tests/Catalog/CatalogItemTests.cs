using MG.BusinessPlatform.Domain.Catalog;
using MG.BusinessPlatform.Domain.Finance;

namespace MG.BusinessPlatform.Domain.Tests.Catalog;

public sealed class CatalogItemTests
{
    [Fact]
    public void Constructor_NormalizesSkuUnitAndBarcode()
    {
        var item = Create(" sku-1 ", " ea ", " 12345 ");
        Assert.Equal("SKU-1", item.Sku);
        Assert.Equal("EA", item.UnitCode);
        Assert.Equal("12345", item.Barcode);
    }

    [Fact]
    public void Constructor_RejectsMismatchedCostCurrency()
    {
        Assert.Throws<InvalidOperationException>(() => new CatalogItem(Guid.NewGuid(), "SKU", "Item",
            CatalogItemKind.StockItem, "EA", new Money(10m, "IRR"), new Money(2m, "USD")));
    }

    [Fact]
    public void Constructor_RejectsNegativePrice()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CatalogItem(Guid.NewGuid(), "SKU", "Item",
            CatalogItemKind.StockItem, "EA", new Money(-1m, "IRR")));
    }

    [Fact]
    public void Service_CannotTrackInventory()
    {
        Assert.Throws<ArgumentException>(() => new CatalogItem(Guid.NewGuid(), "SVC", "Repair",
            CatalogItemKind.Service, "EA", new Money(10m, "IRR"), trackInventory: true));
    }

    private static CatalogItem Create(string sku, string unit, string? barcode) =>
        new(Guid.NewGuid(), sku, "Sample", CatalogItemKind.StockItem, unit,
            new Money(100m, "IRR"), barcode: barcode);
}
