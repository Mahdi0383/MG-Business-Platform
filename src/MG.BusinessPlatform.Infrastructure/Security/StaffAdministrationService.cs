using System.Text;

using Microsoft.EntityFrameworkCore;

using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Infrastructure.Security;

public sealed record StaffUserSummary(
    Guid StaffUserId,
    string UserName,
    string DisplayName,
    string Role,
    bool IsActive,
    bool MustChangePassword,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc,
    IReadOnlyList<Guid> BranchIds);

/// <summary>
/// Business-scoped staff administration. Every operation requires a validated staff
/// request context; tenant identity is never accepted from the caller.
/// </summary>
public sealed class StaffAdministrationService(
    BusinessDbContext db,
    StaffPasswordHasher passwordHasher)
{
    public async Task<IReadOnlyList<StaffUserSummary>> ListStaffAsync(
        CancellationToken cancellationToken = default)
    {
        var context = RequireContext(StaffPermission.StaffRead);
        var users = await db.StaffUsers.AsNoTracking()
            .Where(x => x.BusinessId == context.BusinessId)
            .OrderBy(x => x.UserNameNormalized)
            .Select(x => new
            {
                x.Id,
                x.UserNameNormalized,
                x.DisplayName,
                x.Role,
                x.IsActive,
                x.MustChangePassword,
                x.CreatedAtUtc,
                x.LastLoginAtUtc
            })
            .ToListAsync(cancellationToken);

        var access = await db.StaffBranchAccess.AsNoTracking()
            .Where(x => x.BusinessId == context.BusinessId)
            .ToListAsync(cancellationToken);
        return users.Select(user => new StaffUserSummary(
                user.Id,
                user.UserNameNormalized,
                user.DisplayName,
                user.Role,
                user.IsActive,
                user.MustChangePassword,
                user.CreatedAtUtc,
                user.LastLoginAtUtc,
                access.Where(x => x.StaffUserId == user.Id)
                    .Select(x => x.BranchId)
                    .OrderBy(x => x)
                    .ToArray()))
            .ToArray();
    }

    public async Task<Guid> CreateStaffAsync(
        string userName,
        string displayName,
        string password,
        string role,
        IReadOnlyCollection<Guid> branchIds,
        CancellationToken cancellationToken = default)
    {
        var context = RequireContext(StaffPermission.StaffManage);
        var normalizedUserName = NormalizeUserName(userName);
        var normalizedDisplayName = NormalizeDisplayName(displayName);
        ValidatePassword(password);
        var normalizedRole = ValidateRole(role);
        if (normalizedRole == "Owner" && context.Role != "Owner")
            throw new UnauthorizedAccessException("Only an Owner can create another Owner account.");
        var requestedBranches = NormalizeBranchIds(branchIds);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AcquireBusinessWriteLockAsync(context.BusinessId, cancellationToken);

        if (await db.StaffUsers.AnyAsync(
                x => x.BusinessId == context.BusinessId
                    && x.UserNameNormalized == normalizedUserName,
                cancellationToken))
            throw new InvalidOperationException("A staff account with this username already exists.");

        await ValidateBranchesAsync(context.BusinessId, requestedBranches, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var user = new StaffUserRecord
        {
            Id = Guid.NewGuid(),
            BusinessId = context.BusinessId,
            UserNameNormalized = normalizedUserName,
            DisplayName = normalizedDisplayName,
            PasswordHash = passwordHasher.Hash(password),
            Role = normalizedRole,
            IsActive = true,
            MustChangePassword = false,
            FailedLoginCount = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.StaffUsers.Add(user);
        foreach (var branchId in requestedBranches)
        {
            db.StaffBranchAccess.Add(new StaffBranchAccessRecord
            {
                BusinessId = context.BusinessId,
                StaffUserId = user.Id,
                BranchId = branchId,
                GrantedAtUtc = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return user.Id;
    }

    public async Task UpdateStaffAccessAsync(
        Guid staffUserId,
        string role,
        bool isActive,
        IReadOnlyCollection<Guid> branchIds,
        CancellationToken cancellationToken = default)
    {
        var context = RequireContext(StaffPermission.StaffManage);
        if (staffUserId == Guid.Empty || staffUserId == context.StaffUserId)
            throw new InvalidOperationException("Administrators cannot change their own role, status or branch grants.");

        var normalizedRole = ValidateRole(role);
        var requestedBranches = NormalizeBranchIds(branchIds);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AcquireBusinessWriteLockAsync(context.BusinessId, cancellationToken);

        var user = await db.StaffUsers.SingleOrDefaultAsync(
            x => x.Id == staffUserId && x.BusinessId == context.BusinessId,
            cancellationToken)
            ?? throw new KeyNotFoundException("The staff account was not found in the active business.");

        if (user.Role == "Owner" && context.Role != "Owner")
            throw new UnauthorizedAccessException("Only an Owner can modify another Owner account.");
        if (normalizedRole == "Owner" && context.Role != "Owner")
            throw new UnauthorizedAccessException("Only an Owner can promote a staff account to Owner.");

        await ValidateBranchesAsync(context.BusinessId, requestedBranches, cancellationToken);

        var otherAdministrators = await db.StaffUsers.CountAsync(
            x => x.BusinessId == context.BusinessId
                && x.Id != user.Id
                && x.IsActive
                && (x.Role == "Owner" || x.Role == "Administrator"),
            cancellationToken);
        var targetRemainsAdministrator = isActive && IsAdministrator(normalizedRole);
        if (otherAdministrators + (targetRemainsAdministrator ? 1 : 0) == 0)
            throw new InvalidOperationException("The business must retain at least one active Owner or Administrator.");

        var existingAccess = await db.StaffBranchAccess
            .Where(x => x.BusinessId == context.BusinessId && x.StaffUserId == user.Id)
            .ToListAsync(cancellationToken);
        var existingBranchIds = existingAccess.Select(x => x.BranchId).ToHashSet();
        var requestedBranchSet = requestedBranches.ToHashSet();
        var accessChanged = !existingBranchIds.SetEquals(requestedBranchSet);

        foreach (var grant in existingAccess.Where(x => !requestedBranchSet.Contains(x.BranchId)))
            db.StaffBranchAccess.Remove(grant);
        foreach (var branchId in requestedBranches.Where(x => !existingBranchIds.Contains(x)))
        {
            db.StaffBranchAccess.Add(new StaffBranchAccessRecord
            {
                BusinessId = context.BusinessId,
                StaffUserId = user.Id,
                BranchId = branchId,
                GrantedAtUtc = DateTimeOffset.UtcNow
            });
        }

        var roleChanged = !string.Equals(user.Role, normalizedRole, StringComparison.Ordinal);
        var statusChanged = user.IsActive != isActive;
        user.Role = normalizedRole;
        user.IsActive = isActive;
        user.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (roleChanged || statusChanged || accessChanged)
            await RevokeSessionsAsync(context.BusinessId, user.Id, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ChangeOwnPasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var context = StaffRequestContextScope.Current
            ?? throw new UnauthorizedAccessException("A validated staff session is required to change a password.");
        ValidatePassword(newPassword);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AcquireBusinessWriteLockAsync(context.BusinessId, cancellationToken);

        var user = await db.StaffUsers.SingleOrDefaultAsync(
            x => x.Id == context.StaffUserId
                && x.BusinessId == context.BusinessId
                && x.IsActive,
            cancellationToken)
            ?? throw new UnauthorizedAccessException("The current staff account is no longer active.");

        if (!passwordHasher.Verify(currentPassword, user.PasswordHash))
            throw new UnauthorizedAccessException("The current password is incorrect.");

        user.PasswordHash = passwordHasher.Hash(newPassword);
        user.MustChangePassword = false;
        user.FailedLoginCount = 0;
        user.LockedUntilUtc = null;
        user.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await RevokeSessionsAsync(context.BusinessId, user.Id, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private StaffRequestContext RequireContext(StaffPermission permission)
    {
        var context = StaffRequestContextScope.Current;
        if (!StaffAuthorizationPolicy.IsAllowed(context, permission))
            throw new UnauthorizedAccessException(
                $"A validated staff session with '{permission}' permission is required.");
        return context!;
    }

    private async Task AcquireBusinessWriteLockAsync(
        Guid businessId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var updated = await db.StoreSettings
            .Where(x => x.Id == 1 && x.BusinessId == businessId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.UpdatedAt, now), cancellationToken);
        if (updated != 1)
            throw new InvalidOperationException("The active business is not the configured local business.");
    }

    private async Task ValidateBranchesAsync(
        Guid businessId,
        IReadOnlyCollection<Guid> branchIds,
        CancellationToken cancellationToken)
    {
        var activeBranches = await db.Branches.AsNoTracking()
            .Where(x => x.BusinessId == businessId && x.IsActive && branchIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);
        if (activeBranches.Length != branchIds.Count)
            throw new InvalidOperationException("Every assigned branch must be active and belong to the active business.");
    }

    private async Task RevokeSessionsAsync(
        Guid businessId,
        Guid staffUserId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var sessions = await db.StaffSessions
            .Where(x => x.BusinessId == businessId
                && x.StaffUserId == staffUserId
                && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
            session.RevokedAtUtc = now;
    }

    private static string ValidateRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("A staff role is required.", nameof(role));
        var normalized = role.Trim();
        if (!StaffAuthorizationPolicy.IsRoleAllowed(normalized, StaffPermission.DashboardRead))
            throw new ArgumentException("The requested staff role is not supported.", nameof(role));
        return normalized;
    }

    private static Guid[] NormalizeBranchIds(IReadOnlyCollection<Guid>? branchIds)
    {
        ArgumentNullException.ThrowIfNull(branchIds);
        var normalized = branchIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (normalized.Length == 0)
            throw new ArgumentException("At least one active branch must be assigned.", nameof(branchIds));
        return normalized;
    }

    private static string NormalizeUserName(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("A username is required.", nameof(userName));
        var normalized = userName.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        if (normalized.Length is < 3 or > 100
            || normalized.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)))
            throw new ArgumentException("Username must contain 3 to 100 non-whitespace characters.", nameof(userName));
        return normalized;
    }

    private static string NormalizeDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("A display name is required.", nameof(displayName));
        var normalized = displayName.Trim().Normalize(NormalizationForm.FormKC);
        if (normalized.Length > 160 || normalized.Any(char.IsControl))
            throw new ArgumentException("Display name is too long or contains control characters.", nameof(displayName));
        return normalized;
    }

    private static void ValidatePassword(string? password)
    {
        if (password is null)
            throw new ArgumentNullException(nameof(password));
        if (password.Length < 12 || Encoding.UTF8.GetByteCount(password) > 1_024)
            throw new ArgumentException(
                "Passwords must contain at least 12 characters and no more than 1,024 UTF-8 bytes.",
                nameof(password));
    }

    private static bool IsAdministrator(string role) =>
        string.Equals(role, "Owner", StringComparison.Ordinal)
        || string.Equals(role, "Administrator", StringComparison.Ordinal);
}
