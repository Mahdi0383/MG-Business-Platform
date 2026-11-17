using Microsoft.EntityFrameworkCore;

namespace MG.BusinessPlatform.Infrastructure.Persistence;

/// <summary>
/// Performs branch-level stock changes inside the caller's transaction.
/// Every stock movement, branch balance delta and legacy aggregate quantity update
/// is committed or rolled back together.
/// </summary>
public static class BranchInventoryService
{
    public static async Task<int> GetOnHandQuantityAsync(
        BusinessDbContext db,
        Guid businessId,
        Guid branchId,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (businessId == Guid.Empty || branchId == Guid.Empty || productId == Guid.Empty)
            throw new ArgumentException("Business, branch and product identities are required.");

        return await db.BranchInventoryBalances.AsNoTracking()
            .Where(x => x.BusinessId == businessId && x.BranchId == branchId && x.ProductId == productId)
            .Select(x => (int?)x.OnHandQuantity)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;
    }

    public static async Task<BranchInventoryBalanceRecord> ApplyMovementWithinTransactionAsync(
        BusinessDbContext db,
        Guid businessId,
        Guid branchId,
        Guid productId,
        int signedQuantity,
        string movementType,
        string reference,
        string note,
        decimal? unitCost = null,
        string? currencyCode = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Branch inventory changes require an active database transaction.");
        if (businessId == Guid.Empty || branchId == Guid.Empty || productId == Guid.Empty)
            throw new ArgumentException("Business, branch and product identities are required.");
        if (signedQuantity == 0 || signedQuantity == int.MinValue)
            throw new ArgumentOutOfRangeException(nameof(signedQuantity), "Stock movement quantity must be a non-zero signed integer.");
        ArgumentNullException.ThrowIfNull(movementType);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(note);
        if (string.IsNullOrWhiteSpace(movementType) || movementType.Length > 40
            || reference.Length > 100 || note.Length > 500)
            throw new ArgumentException("Movement type, reference or note exceed their supported lengths.");

        var businessIsActive = await db.Businesses.AsNoTracking()
            .AnyAsync(x => x.Id == businessId && x.IsActive, cancellationToken);
        if (!businessIsActive)
            throw new InvalidOperationException("The configured business is inactive or missing.");

        var branchIsActive = await db.Branches.AsNoTracking()
            .AnyAsync(x => x.Id == branchId && x.BusinessId == businessId && x.IsActive, cancellationToken);
        if (!branchIsActive)
            throw new InvalidOperationException("The selected inventory branch is inactive, missing, or belongs to another business.");

        var product = await db.StoreProducts.SingleOrDefaultAsync(
            x => x.Id == productId && x.BusinessId == businessId, cancellationToken)
            ?? throw new KeyNotFoundException("The product does not belong to the configured business.");

        var resolvedUnitCost = unitCost ?? product.UnitCost;
        var resolvedCurrency = string.IsNullOrWhiteSpace(currencyCode)
            ? product.CurrencyCode.Trim().ToUpperInvariant()
            : currencyCode.Trim().ToUpperInvariant();
        if (resolvedUnitCost < 0m)
            throw new InvalidOperationException("Stock movement unit cost cannot be negative.");
        if (resolvedCurrency.Length != 3
            || resolvedCurrency.Any(character => character is < 'A' or > 'Z')
            || !string.Equals(resolvedCurrency, product.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Stock movement currency must match the product's three-letter currency.");

        var balanceExists = await db.BranchInventoryBalances.AsNoTracking()
            .AnyAsync(x => x.BusinessId == businessId && x.BranchId == branchId && x.ProductId == productId,
                cancellationToken);
        if (!balanceExists && signedQuantity < 0)
            throw new InvalidOperationException("Insufficient branch stock: no inventory balance exists for this product at the selected branch.");

        var now = DateTimeOffset.UtcNow;
        var balanceRows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO BranchInventoryBalances
                (BusinessId, BranchId, ProductId, OnHandQuantity, UpdatedAtUtc)
            VALUES
                ({businessId}, {branchId}, {productId}, {signedQuantity}, {now})
            ON CONFLICT (BusinessId, BranchId, ProductId) DO UPDATE SET
                OnHandQuantity = BranchInventoryBalances.OnHandQuantity + excluded.OnHandQuantity,
                UpdatedAtUtc = excluded.UpdatedAtUtc
            WHERE BranchInventoryBalances.OnHandQuantity + excluded.OnHandQuantity >= 0
              AND BranchInventoryBalances.OnHandQuantity + excluded.OnHandQuantity <= {int.MaxValue};
            """, cancellationToken);
        if (balanceRows != 1)
            throw new InvalidOperationException(
                signedQuantity < 0
                    ? "Insufficient branch stock for this transaction."
                    : "Branch stock quantity would exceed the supported numeric range.");

        var aggregateQuery = db.StoreProducts.Where(x => x.Id == productId
            && x.BusinessId == businessId);
        aggregateQuery = signedQuantity > 0
            ? aggregateQuery.Where(x => x.StockQuantity <= int.MaxValue - signedQuantity)
            : aggregateQuery.Where(x => x.StockQuantity >= -signedQuantity);

        var aggregateRows = await aggregateQuery.ExecuteUpdateAsync(update => update
            .SetProperty(x => x.StockQuantity, x => x.StockQuantity + signedQuantity)
            .SetProperty(x => x.UpdatedAt, now), cancellationToken);
        if (aggregateRows != 1)
            throw new InvalidOperationException(
                signedQuantity < 0
                    ? "Aggregate product stock is insufficient or changed concurrently."
                    : "Aggregate product stock would exceed the supported numeric range.");

        // ExecuteUpdate bypasses the change tracker. Keep any product entity already
        // loaded by the caller synchronized with the database update.
        product.StockQuantity = checked(product.StockQuantity + signedQuantity);
        product.UpdatedAt = now;

        db.StockMovements.Add(new StockMovementRecord
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            BranchId = branchId,
            ProductId = productId,
            OccurredAt = now,
            MovementType = movementType.Trim(),
            Quantity = Math.Abs(signedQuantity),
            SignedQuantity = signedQuantity,
            UnitCost = resolvedUnitCost,
            CurrencyCode = resolvedCurrency,
            Reference = reference.Trim(),
            Note = note.Trim()
        });

        var onHand = await GetOnHandQuantityAsync(db, businessId, branchId, productId, cancellationToken);
        return new BranchInventoryBalanceRecord
        {
            BusinessId = businessId,
            BranchId = branchId,
            ProductId = productId,
            OnHandQuantity = onHand,
            UpdatedAtUtc = now
        };
    }

    public static async Task EnsureZeroBalanceWithinTransactionAsync(
        BusinessDbContext db,
        Guid businessId,
        Guid branchId,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Branch inventory initialization requires an active database transaction.");
        if (businessId == Guid.Empty || branchId == Guid.Empty || productId == Guid.Empty)
            throw new ArgumentException("Business, branch and product identities are required.");

        var productExists = await db.StoreProducts.AsNoTracking()
            .AnyAsync(x => x.Id == productId && x.BusinessId == businessId, cancellationToken);
        var branchExists = await db.Branches.AsNoTracking()
            .AnyAsync(x => x.Id == branchId && x.BusinessId == businessId && x.IsActive, cancellationToken);
        if (!productExists || !branchExists)
            throw new InvalidOperationException("A zero inventory balance can only be created for a product and active branch in the same business.");

        var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO BranchInventoryBalances
                (BusinessId, BranchId, ProductId, OnHandQuantity, UpdatedAtUtc)
            VALUES ({businessId}, {branchId}, {productId}, 0, {now})
            ON CONFLICT (BusinessId, BranchId, ProductId) DO NOTHING;
            """, cancellationToken);
    }
}
