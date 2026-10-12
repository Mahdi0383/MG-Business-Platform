using MG.BusinessPlatform.Domain.Finance;

namespace MG.BusinessPlatform.Domain.Catalog;

public enum CatalogItemKind
{
    StockItem = 1,
    Service = 2,
    Composite = 3
}

public sealed record CatalogItem
{
    public Guid Id { get; }
    public string Sku { get; }
    public string Name { get; }
    public string? Barcode { get; }
    public CatalogItemKind Kind { get; }
    public string UnitCode { get; }
    public Money SellingPrice { get; }
    public Money? PurchaseCost { get; }
    public decimal? ReorderPoint { get; }
    public bool TrackInventory { get; }
    public bool IsActive { get; }

    public CatalogItem(Guid id, string sku, string name, CatalogItemKind kind, string unitCode,
        Money sellingPrice, Money? purchaseCost = null, string? barcode = null,
        decimal? reorderPoint = null, bool trackInventory = true, bool isActive = true)
    {
        if (id == Guid.Empty) throw new ArgumentException("Catalog item ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(sku)) throw new ArgumentException("SKU is required.", nameof(sku));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Item name is required.", nameof(name));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (string.IsNullOrWhiteSpace(unitCode)) throw new ArgumentException("Unit code is required.", nameof(unitCode));
        if (sellingPrice.Amount < 0m) throw new ArgumentOutOfRangeException(nameof(sellingPrice));
        if (purchaseCost is { Amount: < 0m }) throw new ArgumentOutOfRangeException(nameof(purchaseCost));
        if (purchaseCost is not null && purchaseCost.Value.CurrencyCode != sellingPrice.CurrencyCode)
            throw new InvalidOperationException("Selling price and purchase cost currencies must match.");
        if (reorderPoint is < 0m) throw new ArgumentOutOfRangeException(nameof(reorderPoint));
        if (kind == CatalogItemKind.Service && trackInventory)
            throw new ArgumentException("Services cannot be configured for stock tracking.", nameof(trackInventory));

        Id = id;
        Sku = sku.Trim().ToUpperInvariant();
        Name = name.Trim();
        Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();
        Kind = kind;
        UnitCode = unitCode.Trim().ToUpperInvariant();
        SellingPrice = sellingPrice;
        PurchaseCost = purchaseCost;
        ReorderPoint = reorderPoint;
        TrackInventory = trackInventory;
        IsActive = isActive;
    }
}
