using Microsoft.EntityFrameworkCore;

using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Infrastructure.Security;

public enum StaffPermission
{
    DashboardRead,
    SalesRead,
    SalesCreate,
    SalesRefund,
    InventoryRead,
    InventoryAdjust,
    AccountingRead,
    AccountingManage,
    GameNetRead,
    GameNetManage,
    ReservationsRead,
    ReservationsManage,
    CustomersRead,
    CustomersManage,
    StaffRead,
    StaffManage,
    SettingsManage,
    ReportsRead
}

/// <summary>
/// A request-scoped identity resolved from a persisted, unrevoked staff session.
/// Instances can only be created by StaffRequestContextResolver within this assembly.
/// </summary>
public sealed class StaffRequestContext
{
    internal StaffRequestContext(
        Guid businessId,
        Guid branchId,
        Guid staffUserId,
        string userName,
        string displayName,
        string role,
        string correlationId)
    {
        BusinessId = businessId;
        BranchId = branchId;
        StaffUserId = staffUserId;
        UserName = userName;
        DisplayName = displayName;
        Role = role;
        CorrelationId = correlationId;
    }

    public Guid BusinessId { get; }
    public Guid BranchId { get; }
    public Guid StaffUserId { get; }
    public string UserName { get; }
    public string DisplayName { get; }
    public string Role { get; }
    public string CorrelationId { get; }
}

public sealed class StaffRequestContextResolver(
    BusinessDbContext db,
    StaffAuthenticationService authentication)
{
    public async Task<StaffRequestContext?> ResolveAsync(
        string? sessionToken,
        Guid? requestedBranchId = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return null;

        // Tenant identity is sourced from the local persisted installation settings,
        // never from a request header, query string, body or caller-provided business ID.
        var settings = await db.StoreSettings.AsNoTracking()
            .Where(x => x.Id == 1)
            .Select(x => new { x.BusinessId, x.DefaultBranchId })
            .SingleOrDefaultAsync(cancellationToken);
        if (settings?.BusinessId is not Guid businessId || businessId == Guid.Empty)
            return null;

        var identity = await authentication.ValidateSessionAsync(
            businessId, sessionToken, cancellationToken);
        if (identity is null || identity.BusinessId != businessId)
            return null;

        var branchId = requestedBranchId ?? settings.DefaultBranchId;
        if (branchId is not Guid selectedBranch || selectedBranch == Guid.Empty
            || !identity.BranchIds.Contains(selectedBranch))
            return null;

        var branchIsActiveAndOwned = await db.Branches.AsNoTracking()
            .AnyAsync(x => x.Id == selectedBranch
                && x.BusinessId == businessId
                && x.IsActive,
                cancellationToken);
        if (!branchIsActiveAndOwned)
            return null;

        var normalizedCorrelationId = NormalizeCorrelationId(correlationId);
        return new StaffRequestContext(
            businessId,
            selectedBranch,
            identity.StaffUserId,
            identity.UserName,
            identity.DisplayName,
            identity.Role,
            normalizedCorrelationId);
    }

    private static string NormalizeCorrelationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Guid.NewGuid().ToString("N");

        var normalized = value.Trim();
        if (normalized.Length > 100 || normalized.Any(char.IsControl))
            return Guid.NewGuid().ToString("N");
        return normalized;
    }
}

public static class StaffAuthorizationPolicy
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<StaffPermission>> RolePermissions =
        new Dictionary<string, IReadOnlySet<StaffPermission>>(StringComparer.Ordinal)
        {
            ["Owner"] = AllPermissions(),
            ["Administrator"] = AllPermissions(),
            ["Manager"] = Set(
                StaffPermission.DashboardRead,
                StaffPermission.SalesRead, StaffPermission.SalesCreate, StaffPermission.SalesRefund,
                StaffPermission.InventoryRead, StaffPermission.InventoryAdjust,
                StaffPermission.AccountingRead,
                StaffPermission.GameNetRead, StaffPermission.GameNetManage,
                StaffPermission.ReservationsRead, StaffPermission.ReservationsManage,
                StaffPermission.CustomersRead, StaffPermission.CustomersManage,
                StaffPermission.ReportsRead),
            ["Cashier"] = Set(
                StaffPermission.DashboardRead,
                StaffPermission.SalesRead, StaffPermission.SalesCreate,
                StaffPermission.InventoryRead,
                StaffPermission.GameNetRead, StaffPermission.GameNetManage,
                StaffPermission.CustomersRead,
                StaffPermission.ReportsRead),
            ["Accountant"] = Set(
                StaffPermission.DashboardRead,
                StaffPermission.SalesRead,
                StaffPermission.InventoryRead,
                StaffPermission.AccountingRead, StaffPermission.AccountingManage,
                StaffPermission.CustomersRead,
                StaffPermission.ReportsRead),
            ["ReservationOperator"] = Set(
                StaffPermission.DashboardRead,
                StaffPermission.ReservationsRead, StaffPermission.ReservationsManage,
                StaffPermission.CustomersRead),
            ["ReadOnly"] = Set(
                StaffPermission.DashboardRead,
                StaffPermission.SalesRead,
                StaffPermission.InventoryRead,
                StaffPermission.AccountingRead,
                StaffPermission.GameNetRead,
                StaffPermission.ReservationsRead,
                StaffPermission.CustomersRead,
                StaffPermission.StaffRead,
                StaffPermission.ReportsRead)
        };

    public static bool IsAllowed(StaffRequestContext? context, StaffPermission permission) =>
        context is not null && IsRoleAllowed(context.Role, permission);

    public static bool IsRoleAllowed(string? role, StaffPermission permission) =>
        !string.IsNullOrWhiteSpace(role)
        && RolePermissions.TryGetValue(role, out var permissions)
        && permissions.Contains(permission);

    private static IReadOnlySet<StaffPermission> AllPermissions() =>
        Enum.GetValues<StaffPermission>().ToHashSet();

    private static IReadOnlySet<StaffPermission> Set(params StaffPermission[] permissions) =>
        permissions.ToHashSet();
}
