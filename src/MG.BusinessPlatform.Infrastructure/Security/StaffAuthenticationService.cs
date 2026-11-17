using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore;

using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Infrastructure.Security;

public sealed record StaffIdentity(
    Guid StaffUserId,
    Guid BusinessId,
    string UserName,
    string DisplayName,
    string Role,
    IReadOnlyList<Guid> BranchIds);

public sealed record StaffLoginSession(
    string Token,
    DateTimeOffset ExpiresAtUtc,
    StaffIdentity Identity);

/// <summary>
/// Persistence-backed staff credential and opaque-session primitives.
/// This service intentionally does not expose HTTP endpoints or trust caller-supplied
/// tenant/branch identifiers; callers must resolve the local business from trusted settings.
/// </summary>
public sealed class StaffAuthenticationService(
    BusinessDbContext db,
    StaffPasswordHasher passwordHasher)
{
    private static readonly Lazy<string> DummyPasswordHash = new(
        () => new StaffPasswordHasher().Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private const int MaximumFailedAttempts = 5;

    public async Task<Guid> CreateInitialOwnerAsync(
        Guid businessId,
        IReadOnlyCollection<Guid> branchIds,
        string userName,
        string displayName,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (businessId == Guid.Empty)
            throw new ArgumentException("Business identity is required.", nameof(businessId));
        ArgumentNullException.ThrowIfNull(branchIds);
        ValidatePasswordForCreation(password);
        var normalizedUserName = NormalizeUserName(userName);
        var normalizedDisplayName = NormalizeDisplayName(displayName);
        var requestedBranches = branchIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (requestedBranches.Length == 0)
            throw new ArgumentException("At least one branch must be assigned to the initial owner.", nameof(branchIds));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        // Acquire a SQLite write lock before checking the empty-user condition. This
        // prevents two concurrent first-run requests from creating separate owners.
        var settingsUpdated = await db.StoreSettings
            .Where(x => x.Id == 1 && x.BusinessId == businessId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.UpdatedAt, now), cancellationToken);
        if (settingsUpdated != 1)
            throw new InvalidOperationException("The requested business is not the configured local business.");

        var business = await db.Businesses.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == businessId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("The configured business is missing or inactive.");

        if (await db.StaffUsers.AnyAsync(x => x.BusinessId == business.Id, cancellationToken))
            throw new InvalidOperationException("An initial owner already exists. Use an authenticated administrator workflow to add staff.");

        var activeBranches = await db.Branches
            .Where(x => x.BusinessId == business.Id && x.IsActive && requestedBranches.Contains(x.Id))
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);
        if (activeBranches.Length != requestedBranches.Length)
            throw new InvalidOperationException("Every assigned branch must exist, be active, and belong to the configured business.");

        var user = new StaffUserRecord
        {
            Id = Guid.NewGuid(),
            BusinessId = business.Id,
            UserNameNormalized = normalizedUserName,
            DisplayName = normalizedDisplayName,
            PasswordHash = passwordHasher.Hash(password),
            Role = "Owner",
            IsActive = true,
            MustChangePassword = false,
            FailedLoginCount = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.StaffUsers.Add(user);
        foreach (var branchId in activeBranches)
        {
            db.StaffBranchAccess.Add(new StaffBranchAccessRecord
            {
                BusinessId = business.Id,
                StaffUserId = user.Id,
                BranchId = branchId,
                GrantedAtUtc = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return user.Id;
    }

    public async Task<StaffLoginSession?> AuthenticateAsync(
        Guid businessId,
        string? userName,
        string? password,
        string? clientLabel = null,
        CancellationToken cancellationToken = default)
    {
        if (businessId == Guid.Empty || string.IsNullOrEmpty(userName) || password is null)
        {
            _ = passwordHasher.Verify(password ?? string.Empty, DummyPasswordHash.Value);
            return null;
        }

        string normalizedUserName;
        try
        {
            normalizedUserName = NormalizeUserName(userName);
        }
        catch (ArgumentException)
        {
            _ = passwordHasher.Verify(password, DummyPasswordHash.Value);
            return null;
        }

        var user = await db.StaffUsers.SingleOrDefaultAsync(
            x => x.BusinessId == businessId && x.UserNameNormalized == normalizedUserName,
            cancellationToken);

        if (user is null)
        {
            _ = passwordHasher.Verify(password, DummyPasswordHash.Value);
            return null;
        }

        var passwordValid = passwordHasher.Verify(password, user.PasswordHash);
        var now = DateTimeOffset.UtcNow;
        if (!passwordValid || !user.IsActive || !await IsBusinessActiveAsync(businessId, cancellationToken))
        {
            if (user.IsActive && user.LockedUntilUtc <= now)
            {
                user.FailedLoginCount = Math.Min(user.FailedLoginCount + 1, MaximumFailedAttempts);
                user.LockedUntilUtc = user.FailedLoginCount >= MaximumFailedAttempts
                    ? now.Add(LockoutDuration)
                    : null;
                user.UpdatedAtUtc = now;
                await db.SaveChangesAsync(cancellationToken);
            }
            return null;
        }

        if (user.LockedUntilUtc is DateTimeOffset lockedUntil && lockedUntil > now)
            return null;

        var branchIds = await db.StaffBranchAccess.AsNoTracking()
            .Where(x => x.BusinessId == businessId && x.StaffUserId == user.Id)
            .OrderBy(x => x.BranchId)
            .Select(x => x.BranchId)
            .ToArrayAsync(cancellationToken);
        if (branchIds.Length == 0)
            return null;

        if (passwordHasher.NeedsRehash(user.PasswordHash))
            user.PasswordHash = passwordHasher.Hash(password);

        user.FailedLoginCount = 0;
        user.LockedUntilUtc = null;
        user.LastLoginAtUtc = now;
        user.UpdatedAtUtc = now;

        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        string token;
        try
        {
            token = Base64UrlEncode(tokenBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
        }

        var session = new StaffSessionRecord
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            StaffUserId = user.Id,
            TokenHash = HashSessionToken(token),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(SessionLifetime),
            ClientLabel = NormalizeClientLabel(clientLabel)
        };
        db.StaffSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        return new StaffLoginSession(
            token,
            session.ExpiresAtUtc,
            new StaffIdentity(user.Id, businessId, user.UserNameNormalized,
                user.DisplayName, user.Role, branchIds));
    }

    public async Task<StaffIdentity?> ValidateSessionAsync(
        Guid businessId,
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (businessId == Guid.Empty || !TryHashSessionToken(token, out var tokenHash))
            return null;

        var now = DateTimeOffset.UtcNow;
        var session = await db.StaffSessions.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.BusinessId == businessId
                    && x.TokenHash == tokenHash
                    && x.RevokedAtUtc == null
                    && x.ExpiresAtUtc > now,
                cancellationToken);
        if (session is null || !await IsBusinessActiveAsync(businessId, cancellationToken))
            return null;

        var user = await db.StaffUsers.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.BusinessId == businessId
                    && x.Id == session.StaffUserId
                    && x.IsActive
                    && (x.LockedUntilUtc == null || x.LockedUntilUtc <= now),
                cancellationToken);
        if (user is null)
            return null;

        var branchIds = await db.StaffBranchAccess.AsNoTracking()
            .Where(x => x.BusinessId == businessId && x.StaffUserId == user.Id)
            .OrderBy(x => x.BranchId)
            .Select(x => x.BranchId)
            .ToArrayAsync(cancellationToken);
        return branchIds.Length == 0
            ? null
            : new StaffIdentity(user.Id, businessId, user.UserNameNormalized,
                user.DisplayName, user.Role, branchIds);
    }

    public async Task<bool> RevokeSessionAsync(
        Guid businessId,
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (businessId == Guid.Empty || !TryHashSessionToken(token, out var tokenHash))
            return false;

        var now = DateTimeOffset.UtcNow;
        var session = await db.StaffSessions.SingleOrDefaultAsync(
            x => x.BusinessId == businessId && x.TokenHash == tokenHash && x.RevokedAtUtc == null,
            cancellationToken);
        if (session is null)
            return false;

        session.RevokedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> RevokeAllSessionsAsync(
        Guid businessId,
        Guid staffUserId,
        CancellationToken cancellationToken = default)
    {
        if (businessId == Guid.Empty || staffUserId == Guid.Empty)
            return 0;

        var now = DateTimeOffset.UtcNow;
        return await db.StaffSessions
            .Where(x => x.BusinessId == businessId
                && x.StaffUserId == staffUserId
                && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAtUtc, now), cancellationToken);
    }

    private async Task<bool> IsBusinessActiveAsync(Guid businessId, CancellationToken cancellationToken) =>
        await db.Businesses.AsNoTracking()
            .AnyAsync(x => x.Id == businessId && x.IsActive, cancellationToken);

    private static string NormalizeUserName(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("User name is required.", nameof(userName));

        var normalized = userName.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        if (normalized.Length is < 3 or > 100
            || normalized.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)))
            throw new ArgumentException("User name must contain 3 to 100 non-whitespace characters.", nameof(userName));
        return normalized;
    }

    private static string NormalizeDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name is required.", nameof(displayName));
        var normalized = displayName.Trim().Normalize(NormalizationForm.FormKC);
        if (normalized.Length > 160 || normalized.Any(char.IsControl))
            throw new ArgumentException("Display name is too long or contains control characters.", nameof(displayName));
        return normalized;
    }

    private static string NormalizeClientLabel(string? clientLabel)
    {
        if (string.IsNullOrWhiteSpace(clientLabel))
            return string.Empty;
        var normalized = clientLabel.Trim();
        return normalized.Length <= 120 ? normalized : normalized[..120];
    }

    private static void ValidatePasswordForCreation(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var byteCount = Encoding.UTF8.GetByteCount(password);
        if (password.Length < 12 || byteCount > 1024)
            throw new ArgumentException("New staff passwords must contain at least 12 characters and no more than 1,024 UTF-8 bytes.", nameof(password));
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryHashSessionToken(string? token, out string tokenHash)
    {
        tokenHash = string.Empty;
        if (token is null || token.Length != 43
            || token.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            return false;

        try
        {
            var bytes = Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=");
            if (bytes.Length != 32)
            {
                CryptographicOperations.ZeroMemory(bytes);
                return false;
            }
            tokenHash = Convert.ToHexString(SHA256.HashData(bytes));
            CryptographicOperations.ZeroMemory(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string HashSessionToken(string token)
    {
        var bytes = Encoding.ASCII.GetBytes(token);
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
