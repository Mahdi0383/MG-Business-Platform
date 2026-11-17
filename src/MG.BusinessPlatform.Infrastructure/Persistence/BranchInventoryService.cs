using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MG.BusinessPlatform.Infrastructure.Persistence;

/// <summary>
/// Applies branch-scoped stock mutations and their legacy business-wide stock projection
/// in the caller's transaction. Every movement is persisted with its trusted business/branch
/// ownership; callers must never accept BusinessId from an untrusted client.
/// </summary>
public sealed class BranchInventoryService(BusinessDbContext db)
{
    public async Task<Guid> GetDefaultBranchIdAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.StoreSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == 1, cancellationToken)
            ?? throw new InvalidOperationException("The local business is not configured.");
        if (settings.BusinessId is not Guid businessId || businessId == Guid.Empty
            || settings.DefaultBranchId is not Guid branchId || branchId == Guid.Empty)
            throw new InvalidOperationException("The configured business or default branch identity is missing.");

        var valid = await db.Branches.AsNoTracking().AnyAsync(
            x => x.Id == branchId && x.BusinessId == businessId && x.IsActive,
            cancellationToken);
        if (!valid)
            throw new InvalidOperationException("The configured default branch is missing, inactive, or belongs to another business.");
        return branchId;
    }

    public async Task<int> GetOnHandAsync(
        Guid businessId,
        Guid branchId,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(businessId, branchId, productId);
        var balance = await db.BranchInventoryBalances.AsNoTracking()
            .Where(x => x.BusinessId == businessId && x.BranchId == branchId && x.ProductId == productId)
            .Select(x => (int?)x.OnHandQuantity)
            .SingleOrDefaultAsync(cancellationToken);
        return balance ?? throw new InvalidOperationException(
            "The product has no initialized inventory balance for this branch. Reconcile inventory before continuing.");
    }

    /// <summary>
    /// Applies a signed stock movement. A negative quantity issues stock; a positive quantity
    /// receives stock. The branch balance and legacy business-wide aggregate are changed with
    /// conditional SQL updates so concurrent checkouts cannot oversell either projection.
    /// The caller's transaction is reused; otherwise this method owns a transaction.
    /// </summary>
    public async Task<int> ApplyMovementAsync(
        Guid businessId,
        Guid branchId,
        Guid productId,
        int signedQuantity,
        decimal unitCost,
        string currencyCode,
        string movementType,
        string reference,
        string note,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(businessId, branchId, productId);
        if (signedQuantity == 0 || signedQuantity is < -10000 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(signedQuantity), "A stock movement must be between -10000 and 10000 and cannot be zero.");
        if (unitCost < 0m)
            throw new ArgumentOutOfRangeException(nameof(unitCost), "Unit cost cannot be negative.");
        ArgumentNullException.ThrowIfNull(currencyCode);
        ArgumentNullException.ThrowIfNull(movementType);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(note);
        var normalizedCurrency = currencyCode.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3 || normalizedCurrency.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Currency must be a three-letter code.", nameof(currencyCode));
        var normalizedType = movementType.Trim();
        var normalizedReference = reference.Trim();
        var normalizedNote = note.Trim();
        if (normalizedType.Length is < 1 or > 40 || normalizedReference.Length > 100 || normalizedNote.Length > 500)
            throw new ArgumentException("Movement type, reference or note exceeds the supported length.");

        IDbContextTransaction? ownedTransaction = null;
        if (db.Database.CurrentTransaction is null)
            ownedTransaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var branchIsValid = await db.Branches.AsNoTracking().AnyAsync(
                x => x.Id == branchId && x.BusinessId == businessId && x.IsActive,
                cancellationToken);
            if (!branchIsValid)
                throw new InvalidOperationException("The selected branch is missing, inactive, or belongs to another business.");

            var product = await db.StoreProducts.SingleOrDefaultAsync(
                x => x.Id == productId && x.BusinessId == businessId,
                cancellationToken)
                ?? throw new KeyNotFoundException("The product was not found in the selected business.");

            var balanceExists = await db.BranchInventoryBalances.AnyAsync(
                x => x.BusinessId == businessId && x.BranchId == branchId && x.ProductId == productId,
                cancellationToken);
            if (!balanceExists)
            {
                // A zero-stock product created after the initial migration can safely receive
                // its first branch row. Positive legacy stock must be explicitly mapped instead
                // of silently assigning it to whichever branch happens to receive the next sale.
                if (product.StockQuantity != 0)
                    throw new InvalidOperationException(
                        "The product has no branch inventory balance. Map its opening stock to a branch before recording movements.");

                db.BranchInventoryBalances.Add(new BranchInventoryBalanceRecord
                {
                    BusinessId = businessId,
                    BranchId = branchId,
                    ProductId = productId,
                    OnHandQuantity = 0,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync(cancellationToken);
            }

            var now = DateTimeOffset.UtcNow;
            var amount = Math.Abs(signedQuantity);
            var balanceQuery = db.BranchInventoryBalances.Where(
                x => x.BusinessId == businessId && x.BranchId == branchId && x.ProductId == productId);
            var productQuery = db.StoreProducts.Where(x => x.Id == productId && x.BusinessId == businessId);

            var balanceAffected = signedQuantity > 0
                ? await balanceQuery.ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.OnHandQuantity, x => x.OnHandQuantity + amount)
                    .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken)
                : await balanceQuery.Where(x => x.OnHandQuantity >= amount)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(x => x.OnHandQuantity, x => x.OnHandQuantity - amount)
                        .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);
            if (balanceAffected != 1)
                throw new InvalidOperationException("The selected branch has insufficient stock or its inventory balance changed. Refresh and retry.");

            var productAffected = signedQuantity > 0
                ? await productQuery.ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.StockQuantity, x => x.StockQuantity + signedQuantity)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken)
                : await productQuery.Where(x => x.StockQuantity >= amount)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(x => x.StockQuantity, x => x.StockQuantity - amount)
                        .SetProperty(x => x.UpdatedAt, now), cancellationToken);
            if (productAffected != 1)
                throw new InvalidOperationException("The business-wide stock projection is inconsistent. Reconcile inventory before continuing.");

            // ExecuteUpdate bypasses EF tracking; keep any already-loaded product instance in sync.
            var trackedProduct = db.ChangeTracker.Entries<StoreProduct>()
                .SingleOrDefault(entry => entry.Entity.Id == productId)?.Entity;
            if (trackedProduct is not null)
            {
                trackedProduct.StockQuantity = checked(trackedProduct.StockQuantity + signedQuantity);
                trackedProduct.UpdatedAt = now;
            }

            db.StockMovements.Add(new StockMovementRecord
            {
                Id = Guid.NewGuid(),
                BusinessId = businessId,
                BranchId = branchId,
                ProductId = productId,
                OccurredAt = now,
                MovementType = normalizedType,
                Quantity = amount,
                SignedQuantity = signedQuantity,
                UnitCost = unitCost,
                CurrencyCode = normalizedCurrency,
                Reference = normalizedReference,
                Note = normalizedNote
            });
            await db.SaveChangesAsync(cancellationToken);

            var updatedQuantity = await db.BranchInventoryBalances.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.BranchId == branchId && x.ProductId == productId)
                .Select(x => x.OnHandQuantity)
                .SingleAsync(cancellationToken);

            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync(cancellationToken);
            return updatedQuantity;
        }
        finally
        {
            if (ownedTransaction is not null)
                await ownedTransaction.DisposeAsync();
        }
    }

    private static void ValidateIdentity(Guid businessId, Guid branchId, Guid productId)
    {
        if (businessId == Guid.Empty) throw new ArgumentException("Business identity is required.", nameof(businessId));
        if (branchId == Guid.Empty) throw new ArgumentException("Branch identity is required.", nameof(branchId));
        if (productId == Guid.Empty) throw new ArgumentException("Product identity is required.", nameof(productId));
    }
}
