using System.Security.Cryptography;
using System.Text;

namespace MG.BusinessPlatform.Infrastructure.Security;

/// <summary>
/// Creates versioned PBKDF2-SHA256 password hashes for staff credentials.
/// The encoded value contains only algorithm parameters, a random salt, and the derived key.
/// </summary>
public sealed class StaffPasswordHasher
{
    private const string Algorithm = "pbkdf2-sha256";
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int MinimumIterations = 100_000;
    private const int MaximumIterations = 2_000_000;
    private const int PasswordMaximumBytes = 1_024;

    public const int CurrentIterations = 600_000;

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        if (passwordBytes.Length is 0 or > PasswordMaximumBytes)
            throw new ArgumentException($"Password must contain between 1 and {PasswordMaximumBytes} UTF-8 bytes.", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        try
        {
            var derivedKey = Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes, salt, CurrentIterations, HashAlgorithmName.SHA256, HashSizeBytes);
            return string.Join('$',
                Algorithm,
                CurrentIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToBase64String(salt),
                Convert.ToBase64String(derivedKey));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    /// <summary>
    /// Returns false for malformed, unsupported, or out-of-policy hashes.
    /// Iteration counts are bounded before running PBKDF2 to prevent attacker-controlled CPU exhaustion.
    /// </summary>
    public bool Verify(string password, string? encodedHash)
    {
        if (password is null || encodedHash is null)
            return false;

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        if (passwordBytes.Length is 0 or > PasswordMaximumBytes)
            return false;

        try
        {
            if (!TryParse(encodedHash, out var iterations, out var salt, out var expectedHash))
                return false;

            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes, salt, iterations, HashAlgorithmName.SHA256, HashSizeBytes);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    /// <summary>Signals when a successfully verified credential should be re-hashed.</summary>
    public bool NeedsRehash(string? encodedHash)
    {
        return !TryParse(encodedHash, out var iterations, out _, out _)
            || iterations < CurrentIterations;
    }

    private static bool TryParse(
        string encodedHash,
        out int iterations,
        out byte[] salt,
        out byte[] expectedHash)
    {
        iterations = 0;
        salt = [];
        expectedHash = [];

        var parts = encodedHash.Split('$');
        if (parts.Length != 4
            || !string.Equals(parts[0], Algorithm, StringComparison.Ordinal)
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out iterations)
            || iterations is < MinimumIterations or > MaximumIterations)
            return false;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedHash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length == SaltSizeBytes && expectedHash.Length == HashSizeBytes;
    }
}
