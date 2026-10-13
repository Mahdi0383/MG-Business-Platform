using MG.BusinessPlatform.Domain.Finance;

namespace MG.BusinessPlatform.Domain.Inventory;

public sealed record InventoryValuationResult(
    Guid ItemId,
    Guid LocationId,
    DateTimeOffset AsOf,
    decimal QuantityOnHand,
    Money InventoryValue,
    Money AverageUnitCost)
{
    public bool HasNegativeStock => QuantityOnHand < 0m;
}

public static class InventoryValuationCalculator
{
    /// <summary>
    /// Calculates quantity and value from costed movements. Issue movements must carry
    /// their applied valuation cost (for example, the weighted-average cost at issue time).
    /// </summary>
    public static InventoryValuationResult Calculate(
        IEnumerable<StockMovement> movements,
        Guid itemId,
        Guid locationId,
        DateTimeOffset asOf,
        string currencyCode)
    {
        ArgumentNullException.ThrowIfNull(movements);
        if (itemId == Guid.Empty) throw new ArgumentException("Item ID is required.", nameof(itemId));
        if (locationId == Guid.Empty) throw new ArgumentException("Location ID is required.", nameof(locationId));

        var zero = Money.Zero(currencyCode);
        decimal quantity = 0m;
        var value = zero;
        var relevant = movements
            .Where(movement => movement.ItemId == itemId && movement.LocationId == locationId && movement.OccurredAt <= asOf)
            .OrderBy(movement => movement.OccurredAt)
            .ThenBy(movement => movement.Id)
            .ToArray();

        foreach (var movement in relevant)
        {
            if (movement.UnitCost is null)
                throw new InvalidOperationException($"Movement {movement.Id} has no unit cost; inventory value cannot be calculated.");
            if (movement.UnitCost.Value.CurrencyCode != zero.CurrencyCode)
                throw new InvalidOperationException($"Movement {movement.Id} uses a different valuation currency.");

            quantity = checked(quantity + movement.SignedQuantity);
            var movementValue = movement.UnitCost.Value.Multiply(movement.Quantity);
            value = movement.SignedQuantity < 0m ? value - movementValue : value + movementValue;
        }

        if (quantity == 0m && !value.IsZero)
            throw new InvalidOperationException("Inventory quantity is zero but inventory value is non-zero.");
        if (value.IsNegative)
            throw new InvalidOperationException("Inventory value is negative; review movement costs and stock history.");

        var averageCost = quantity > 0m
            ? new Money(value.Amount / quantity, zero.CurrencyCode)
            : zero;
        return new InventoryValuationResult(itemId, locationId, asOf, quantity, value, averageCost);
    }
}
