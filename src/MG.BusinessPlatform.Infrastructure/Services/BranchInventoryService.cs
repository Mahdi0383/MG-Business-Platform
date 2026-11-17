using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using MG.BusinessPlatform.Infrastructure.Persistence;
using MG.BusinessPlatform.Infrastructure.Security;

namespace MG.BusinessPlatform.Infrastructure.Services;

/// <summary>
/// The single atomic write boundary for branch-owned stock.
/// Callers may wrap it in a larger sale/purchase transaction; otherwise this service
/// owns the transaction that updates the balance and appends its movement.
/// </summary>
public sealed class BranchInventoryService(BusinessDbContext db)
{
    public async Task<int> GetOnHandAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        if (productId == Guid.Empty)
            throw new ArgumentException("Product ID is required.", nameof(productId));

        var scope = await ResolveScopeAsync(cancellationToken);
        var productExists = await db.StoreProducts.AsNoTracking()
            .AnyAsync(x => x.Id == productId && x.BusinessId == scope.BusinessId, cancellationToken);
        if (!productExists)
            throw new KeyNotFoundException("Product was not found in the active business.");

        return await db.BranchInventoryBalances.AsNoTracking()
            .Where(x => x.BusinessId == scope.BusinessId
                && x.BranchId == scope.BranchId
                && x.ProductId == productId)
            .Select(x => (int?)x.OnHandQuantity)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;
    }

    public async Task<int> ApplyMovementAsync(
        Guid productId,
        int signedQuantity,
        string movementType,
        string reference,
        string note,
        StaffPermission requiredPermission,
        DateTimeOffset? occurredAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (productId == Guid.Empty)
            throw new ArgumentException("Product ID is required.", nameof(productId));
        if (signedQuantity == 0 || Math.Abs((long)signedQuantity) > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(signedQuantity), "Stock movement must be non-zero and within the supported quantity range.");
        if (string.IsNullOrWhiteSpace(movementType) || movementType.Trim().Length > 60)
            throw new ArgumentException("Movement type is required and must not exceed 60 characters.", nameof(movementType));
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(note);
        if (reference.Length > 160 || note.Length > 500)
            throw new ArgumentOutOfRangeException(nameof(reference), "Movement reference or note exceeds the allowed length.");

        if (requiredPermission is not (StaffPermission.InventoryAdjust
            or StaffPermission.SalesCreate
            or StaffPermission.SalesRefund
            or StaffPermission.AccountingManage))
            throw new ArgumentOutOfRangeException(nameof(requiredPermission),
                "Inventory movements must use an inventory, sales, refund or purchasing permission.");

        var staffContext = StaffRequestContextScope.Current;
        if (staffContext is not null
            && !StaffAuthorizationPolicy.IsAllowed(staffContext, requiredPermission))
            throw new UnauthorizedAccessException("The current staff role is not allowed to perform this inventory operation.");

        var scope = await ResolveScopeAsync(cancellationToken);
        var product = await db.StoreProducts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == productId && x.BusinessId == scope.BusinessId, cancellationToken)
            ?? throw new KeyNotFoundException("Product was not found in the active business.");
        if (product.StockQuantity < 0)
            throw new InvalidOperationException("The product has a negative legacy stock quantity; inventory movement was rejected.");

        IDbContextTransaction? transaction = db.Database.CurrentTransaction;
        var ownsTransaction = transaction is null;
        if (ownsTransaction)
            transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO BranchInventoryBalances
                    (BusinessId, BranchId, ProductId, OnHandQuantity, UpdatedAtUtc)
                VALUES
                    ({scope.BusinessId}, {scope.BranchId}, {productId},
                     CASE WHEN EXISTS (
                         SELECT 1 FROM BranchInventoryBalances
                         WHERE BusinessId = {scope.BusinessId} AND ProductId = {productId}
                     ) THEN 0 ELSE {product.StockQuantity} END,
                     {DateTimeOffset.UtcNow})
                ON CONFLICT(BusinessId, BranchId, ProductId) DO NOTHING;
                """, cancellationToken);

            var now = occurredAtUtc ?? DateTimeOffset.UtcNow;
            var balanceQuery = db.BranchInventoryBalances.Where(x =>
                x.BusinessId == scope.BusinessId
                && x.BranchId == scope.BranchId
                && x.ProductId == productId);
            if (signedQuantity < 0)
                balanceQuery = balanceQuery.Where(x => x.OnHandQuantity >= -signedQuantity);
            else
                balanceQuery = balanceQuery.Where(x => x.OnHandQuantity <= int.MaxValue - signedQuantity);

            var affected = await balanceQuery.ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.OnHandQuantity, x => x.OnHandQuantity + signedQuantity)
                .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);
            if (affected != 1)
                throw new InvalidOperationException("Insufficient branch stock or inventory balance changed concurrently.");

            db.StockMovements.Add(new StockMovementRecord
            {
                Id = Guid.NewGuid(),
                BusinessId = scope.BusinessId,
                BranchId = scope.BranchId,
                ProductId = product.Id,
                OccurredAt = now,
                MovementType = movementType.Trim(),
                Quantity = (int)Math.Abs((long)signedQuantity),
                SignedQuantity = signedQuantity,
                UnitCost = product.UnitCost,
                CurrencyCode = product.CurrencyCode,
                Reference = reference.Trim(),
                Note = note.Trim()
            });
            await db.SaveChangesAsync(cancellationToken);

            var resultingQuantity = await db.BranchInventoryBalances.AsNoTracking()
                .Where(x => x.BusinessId == scope.BusinessId
                    && x.BranchId == scope.BranchId
                    && x.ProductId == productId)
                .Select(x => x.OnHandQuantity)
                .SingleAsync(cancellationToken);

            if (ownsTransaction)
                await transaction!.CommitAsync(cancellationToken);
            return resultingQuantity;
        }
        catch
        {
            if (ownsTransaction && transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (ownsTransaction && transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private async Task<(Guid BusinessId, Guid BranchId)> ResolveScopeAsync(
        CancellationToken cancellationToken)
    {
        var settings = await db.StoreSettings.AsNoTracking()
            .Where(x => x.Id == 1)
            .Select(x => new { x.BusinessId, x.DefaultBranchId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Store organization settings are not configured.");

        var staffContext = StaffRequestContextScope.Current;
        var businessId = staffContext?.BusinessId ?? settings.BusinessId;
        var branchId = staffContext?.BranchId ?? settings.DefaultBranchId;
        if (businessId is not Guid validBusinessId || validBusinessId == Guid.Empty
            || branchId is not Guid validBranchId || validBranchId == Guid.Empty
            || settings.BusinessId != validBusinessId)
            throw new InvalidOperationException("A valid business and branch inventory scope is required.");

        var branchIsActive = await db.Branches.AsNoTracking()
            .AnyAsync(x => x.Id == validBranchId
                && x.BusinessId == validBusinessId
                && x.IsActive, cancellationToken);
        if (!branchIsActive)
            throw new InvalidOperationException("The selected inventory branch is not active or does not belong to this business.");

        return (validBusinessId, validBranchId);
    }
}
