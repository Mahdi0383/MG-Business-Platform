namespace MG.BusinessPlatform.Domain.Inventory;

public enum StockMovementType
{
    OpeningBalance = 1,
    PurchaseReceipt = 2,
    SaleIssue = 3,
    CustomerReturn = 4,
    SupplierReturn = 5,
    PositiveAdjustment = 6,
    NegativeAdjustment = 7,
    TransferIn = 8,
    TransferOut = 9
}

/// <summary>
/// A quantity change in one stock item and location. Persist movements as an append-only
/// ledger; corrections should be compensating movements rather than editing history.
/// </summary>
public sealed record StockMovement
{
    public Guid Id { get; }
    public Guid ItemId { get; }
    public Guid LocationId { get; }
    public DateTimeOffset OccurredAt { get; }
    public StockMovementType Type { get; }
    public decimal Quantity { get; }
    public string UnitCode { get; }
    public string? Reference { get; }
    public decimal SignedQuantity => Type is StockMovementType.SaleIssue
        or StockMovementType.SupplierReturn
        or StockMovementType.NegativeAdjustment
        or StockMovementType.TransferOut ? -Quantity : Quantity;

    public StockMovement(Guid id, Guid itemId, Guid locationId, DateTimeOffset occurredAt,
        StockMovementType type, decimal quantity, string unitCode, string? reference = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Movement ID is required.", nameof(id));
        if (itemId == Guid.Empty) throw new ArgumentException("Item ID is required.", nameof(itemId));
        if (locationId == Guid.Empty) throw new ArgumentException("Stock location ID is required.", nameof(locationId));
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (quantity <= 0m) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive; movement type determines direction.");
        if (string.IsNullOrWhiteSpace(unitCode)) throw new ArgumentException("A unit code is required.", nameof(unitCode));

        Id = id;
        ItemId = itemId;
        LocationId = locationId;
        OccurredAt = occurredAt;
        Type = type;
        Quantity = quantity;
        UnitCode = unitCode.Trim().ToUpperInvariant();
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
    }
}
