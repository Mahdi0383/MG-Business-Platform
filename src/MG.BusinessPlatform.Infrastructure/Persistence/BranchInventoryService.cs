using Microsoft.EntityFrameworkCore;

using MG.BusinessPlatform.Infrastructure.Security;

namespace MG.BusinessPlatform.Infrastructure.Persistence;

public sealed record InventoryLocation(Guid BusinessId, Guid BranchId);

public sealed record BranchInventorySnapshot(
    Guid BusinessId,
    Guid BranchId,
    Guid ProductId,
    int Quantity,
    decimal AverageUnitCost);

/// <summary>
/// The single write path for branch-scoped stock and weighted-average cost.
/// Callers own the surrounding transaction so stock, order/invoice, journals and
/// stock-movement history can commit or roll back as one operation.
/// </summary>
public sealed class BranchInventoryService(BusinessDbContext db)
{
    public async Task<InventoryLocation> ResolveCurrentLocationAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await db.StoreSettings.AsNoTracking()
            .Where(x => x.Id == 1)
            .Select(x => new { x.BusinessId, x.DefaultBranchId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The local store settings are missing.");

        if (settings.BusinessId is not Guid businessId || businessId == Guid.Empty
            || settings.DefaultBranchId is not Guid defaultBranchId || defaultBranchId == Guid.Empty)
            throw new InvalidOperationException("The local business and default branch must be configured before inventory operations.");

        var staffContext = StaffRequestContextScope.Current;
        if (staffContext is not null && staffContext.BusinessId != businessId)
            throw new InvalidOperationException("The validated staff business does not match the local inventory database.");

        var branchId = staffContext?.BranchId ?? defaultBranchId;
        var branchIsActiveAndOwned = await db.Branches.AsNoTracking()
            .AnyAsync(x => x.Id == branchId && x.BusinessId == businessId && x.IsActive, cancellationToken);
        if (!branchIsActiveAndOwned)
            throw new InvalidOperationException("The selected inventory branch is missing, inactive, or belongs to another business.");

        return new InventoryLocation(businessId, branchId);
    }

    public async Task<BranchInventorySnapshot> GetBalanceAsync(
        Guid businessId,
        Guid branchId,
        StoreProduct product,
        CancellationToken cancellationToken = default)
    {
        ValidateProductOwnership(businessId, product);
        var balance = await db.BranchInventoryBalances.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.BusinessId == businessId && x.BranchId == branchId && x.ProductId == product.Id,
                cancellationToken);

        if (balance is null)
        {
            if (product.StockQuantity > 0)
                throw new InvalidDataException(
                    $"Product '{product.Sku}' has legacy stock but no branch balance. Inventory initialization or explicit branch mapping is required.");
            return new BranchInventorySnapshot(businessId, branchId, product.Id, 0, product.UnitCost);
        }

        return ToSnapshot(balance);
    }

    public async Task<decimal> IssueAsync(
        Guid businessId,
        Guid branchId,
        StoreProduct product,
        int quantity,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        ValidateQuantity(quantity);
        await ValidateBranchAsync(businessId, branchId, cancellationToken);
        ValidateProductOwnership(businessId, product);

        var balance = await db.BranchInventoryBalances.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.BusinessId == businessId && x.BranchId == branchId && x.ProductId == product.Id,
                cancellationToken);
        if (balance is null || balance.Quantity < quantity)
            throw new InvalidOperationException(
                $"Insufficient stock in the selected branch. Available quantity: {balance?.Quantity ?? 0}.");

        var remaining = balance.Quantity - quantity;
        var affected = await db.BranchInventoryBalances
            .Where(x => x.BusinessId == businessId
                && x.BranchId == branchId
                && x.ProductId == product.Id
                && x.Quantity == balance.Quantity
                && x.AverageUnitCost == balance.AverageUnitCost)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Quantity, remaining)
                .SetProperty(x => x.UpdatedAtUtc, occurredAt), cancellationToken);

        if (affected != 1)
            throw new InvalidOperationException("Branch stock changed during checkout. Refresh the cart and retry.");

        await SynchronizeProductAggregateAsync(businessId, product, occurredAt, cancellationToken);
        return balance.AverageUnitCost;
    }

    public async Task ReceiveAsync(
        Guid businessId,
        Guid branchId,
        StoreProduct product,
        int quantity,
        decimal unitCost,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        ValidateQuantity(quantity);
        if (unitCost < 0m)
            throw new ArgumentOutOfRangeException(nameof(unitCost), "Inventory unit cost cannot be negative.");
        await ValidateBranchAsync(businessId, branchId, cancellationToken);
        ValidateProductOwnership(businessId, product);

        var balance = await db.BranchInventoryBalances.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.BusinessId == businessId && x.BranchId == branchId && x.ProductId == product.Id,
                cancellationToken);

        if (balance is null)
        {
            db.BranchInventoryBalances.Add(new BranchInventoryBalanceRecord
            {
                BusinessId = businessId,
                BranchId = branchId,
                ProductId = product.Id,
                Quantity = quantity,
                AverageUnitCost = unitCost,
                UpdatedAtUtc = occurredAt
            });
        }
        else
        {
            var newQuantity = checked(balance.Quantity + quantity);
            var newAverageCost = newQuantity == 0
                ? unitCost
                : checked(balance.Quantity * balance.AverageUnitCost + quantity * unitCost) / newQuantity;
            var affected = await db.BranchInventoryBalances
                .Where(x => x.BusinessId == businessId
                    && x.BranchId == branchId
                    && x.ProductId == product.Id
                    && x.Quantity == balance.Quantity
                    && x.AverageUnitCost == balance.AverageUnitCost)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Quantity, newQuantity)
                    .SetProperty(x => x.AverageUnitCost, newAverageCost)
                    .SetProperty(x => x.UpdatedAtUtc, occurredAt), cancellationToken);

            if (affected != 1)
                throw new InvalidOperationException("Branch inventory changed during receipt. Retry the purchase invoice.");
        }

        await SynchronizeProductAggregateAsync(businessId, product, occurredAt, cancellationToken);
    }

    public async Task CreateOpeningBalanceAsync(
        Guid businessId,
        Guid branchId,
        StoreProduct product,
        int quantity,
        decimal unitCost,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        if (unitCost < 0m)
            throw new ArgumentOutOfRangeException(nameof(unitCost));
        await ValidateBranchAsync(businessId, branchId, cancellationToken);
        ValidateProductOwnership(businessId, product);

        var exists = await db.BranchInventoryBalances.AnyAsync(
            x => x.BusinessId == businessId && x.BranchId == branchId && x.ProductId == product.Id,
            cancellationToken);
        if (exists)
            throw new InvalidOperationException("An opening balance already exists for this product and branch.");

        db.BranchInventoryBalances.Add(new BranchInventoryBalanceRecord
        {
            BusinessId = businessId,
            BranchId = branchId,
            ProductId = product.Id,
            Quantity = quantity,
            AverageUnitCost = unitCost,
            UpdatedAtUtc = occurredAt
        });

        await SynchronizeProductAggregateAsync(businessId, product, occurredAt, cancellationToken);
    }

    private async Task SynchronizeProductAggregateAsync(
        Guid businessId,
        StoreProduct product,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        var persistedBalances = await db.BranchInventoryBalances.AsNoTracking()
            .Where(x => x.BusinessId == businessId && x.ProductId == product.Id)
            .ToListAsync(cancellationToken);
        var newLocalBalances = db.BranchInventoryBalances.Local
            .Where(x => x.BusinessId == businessId
                && x.ProductId == product.Id
                && db.Entry(x).State == EntityState.Added)
            .ToArray();
        var balances = persistedBalances
            .Concat(newLocalBalances)
            .ToArray();

        var totalQuantity = balances.Aggregate(0, (sum, balance) => checked(sum + balance.Quantity));
        var totalValue = balances.Aggregate(0m,
            (sum, balance) => checked(sum + balance.Quantity * balance.AverageUnitCost));

        product.StockQuantity = totalQuantity;
        if (totalQuantity > 0)
            product.UnitCost = totalValue / totalQuantity;
        product.UpdatedAt = updatedAt;
    }

    private async Task ValidateBranchAsync(
        Guid businessId,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        if (businessId == Guid.Empty || branchId == Guid.Empty)
            throw new ArgumentException("Business and branch identities are required.");

        var valid = await db.Branches.AsNoTracking()
            .AnyAsync(x => x.Id == branchId && x.BusinessId == businessId && x.IsActive, cancellationToken);
        if (!valid)
            throw new InvalidOperationException("Inventory branch is missing, inactive, or owned by another business.");
    }

    private static void ValidateProductOwnership(Guid businessId, StoreProduct product)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (businessId == Guid.Empty || product.Id == Guid.Empty || product.BusinessId != businessId)
            throw new InvalidOperationException("The product does not belong to the configured inventory business.");
    }

    private static void ValidateQuantity(int quantity)
    {
        if (quantity is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Inventory quantity must be between 1 and 10,000.");
    }

    private static BranchInventorySnapshot ToSnapshot(BranchInventoryBalanceRecord balance) =>
        new(balance.BusinessId, balance.BranchId, balance.ProductId, balance.Quantity, balance.AverageUnitCost);
}
