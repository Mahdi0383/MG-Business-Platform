using Microsoft.EntityFrameworkCore;

namespace MG.BusinessPlatform.Infrastructure.Persistence;

/// <summary>
/// Maintains shared add-on definitions and the product-to-add-on assignments used by POS
/// and the customer storefront. Historical order lines keep immutable JSON snapshots.
/// </summary>
public sealed class ProductAddonService(BusinessDbContext db)
{
    public async Task<ProductAddonRecord> SaveDefinitionAsync(
        Guid? addonId,
        string name,
        decimal price,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normalizedName = name.Trim();
        if (normalizedName.Length is < 1 or > 120 || normalizedName.Any(char.IsControl))
            throw new ArgumentException("Add-on name must contain 1-120 printable characters.", nameof(name));
        if (price < 0m)
            throw new ArgumentOutOfRangeException(nameof(price), "Add-on price cannot be negative.");

        var currency = (await db.StoreSettings.AsNoTracking()
            .Where(settings => settings.Id == 1)
            .Select(settings => settings.CurrencyCode)
            .SingleAsync(cancellationToken)).Trim().ToUpperInvariant();

        ProductAddonRecord addon;
        if (addonId is Guid id)
        {
            addon = await db.ProductAddons.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("Product add-on was not found.");
            addon.Name = normalizedName;
            addon.Price = price;
            addon.CurrencyCode = currency;
            addon.IsActive = isActive;
            addon.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            addon = new ProductAddonRecord
            {
                Id = Guid.NewGuid(),
                Name = normalizedName,
                Price = price,
                CurrencyCode = currency,
                IsActive = isActive,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.ProductAddons.Add(addon);
        }

        await db.SaveChangesAsync(cancellationToken);
        return addon;
    }

    public async Task SetProductAddonsAsync(
        Guid productId,
        IReadOnlyCollection<Guid> addonIds,
        CancellationToken cancellationToken = default)
    {
        if (productId == Guid.Empty)
            throw new ArgumentException("Product ID is required.", nameof(productId));
        ArgumentNullException.ThrowIfNull(addonIds);
        if (addonIds.Count > 20)
            throw new ArgumentOutOfRangeException(nameof(addonIds), "A product can have at most 20 add-ons.");
        if (addonIds.Any(id => id == Guid.Empty) || addonIds.Distinct().Count() != addonIds.Count)
            throw new ArgumentException("Add-on assignments must contain unique, non-empty IDs.", nameof(addonIds));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var product = await db.StoreProducts.SingleOrDefaultAsync(x => x.Id == productId, cancellationToken)
            ?? throw new KeyNotFoundException("Product was not found.");

        var selectedIds = addonIds.ToArray();
        List<ProductAddonRecord> selectedAddons;
        if (selectedIds.Length == 0)
            selectedAddons = [];
        else
            selectedAddons = await db.ProductAddons
                .Where(x => selectedIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
        if (selectedAddons.Count != selectedIds.Length)
            throw new KeyNotFoundException("One or more selected add-ons were not found.");
        if (selectedAddons.Any(addon => !addon.IsActive))
            throw new InvalidOperationException("Inactive add-ons cannot be assigned to a product.");
        if (selectedAddons.Any(addon => !string.Equals(addon.CurrencyCode, product.CurrencyCode, StringComparison.Ordinal)))
            throw new InvalidOperationException("Product and add-on currencies must match.");

        var existing = await db.StoreProductAddons
            .Where(link => link.ProductId == productId)
            .ToListAsync(cancellationToken);
        db.StoreProductAddons.RemoveRange(existing);
        foreach (var addonId in selectedIds)
            db.StoreProductAddons.Add(new StoreProductAddonLink { ProductId = productId, AddonId = addonId });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductAddonRecord>> GetAssignedActiveAddonsAsync(
        Guid productId,
        CancellationToken cancellationToken = default) =>
        await (from link in db.StoreProductAddons.AsNoTracking()
               join addon in db.ProductAddons.AsNoTracking() on link.AddonId equals addon.Id
               where link.ProductId == productId && addon.IsActive
               orderby addon.Name
               select addon).ToListAsync(cancellationToken);
}
