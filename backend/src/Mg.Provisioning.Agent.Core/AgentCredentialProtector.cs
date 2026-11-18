using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Mg.Provisioning.Agent.Core;

/// <summary>
/// Authenticated encrypted storage for an agent's stable per-tenant application credential.
/// The agent database stores only the ciphertext, nonce, tag and key ID.
/// </summary>
public sealed class AgentCredentialProtector : IDisposable
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MaxConnectionStringLength = 4096;
    private static readonly Regex KeyIdPattern = new(
        "^[A-Za-z0-9_-]{1,80}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly Dictionary<string, byte[]> keys;
    private readonly string currentKeyId;
    private bool disposed;

    public AgentCredentialProtector(
        string currentKeyId,
        IReadOnlyDictionary<string, string> base64Keys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentKeyId);
        ArgumentNullException.ThrowIfNull(base64Keys);
        if (!KeyIdPattern.IsMatch(currentKeyId))
            throw new ArgumentException("Credential encryption key ID is invalid.", nameof(currentKeyId));

        this.currentKeyId = currentKeyId;
        keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        try
        {
            foreach (var (keyId, encodedKey) in base64Keys)
            {
                if (!KeyIdPattern.IsMatch(keyId))
                    throw new ArgumentException("Credential encryption key ID is invalid.", nameof(base64Keys));
                if (string.IsNullOrWhiteSpace(encodedKey))
                    throw new ArgumentException("Credential encryption key material is missing.", nameof(base64Keys));

                byte[] key;
                try
                {
                    key = Convert.FromBase64String(encodedKey);
                }
                catch (FormatException)
                {
                    throw new ArgumentException("Credential encryption key material is not valid base64.", nameof(base64Keys));
                }

                if (key.Length != KeySize)
                {
                    CryptographicOperations.ZeroMemory(key);
                    throw new ArgumentException("Credential encryption keys must contain exactly 256 bits.", nameof(base64Keys));
                }

                if (!keys.TryAdd(keyId, key))
                {
                    CryptographicOperations.ZeroMemory(key);
                    throw new ArgumentException("Credential encryption key IDs must be unique.", nameof(base64Keys));
                }
            }

            if (!keys.ContainsKey(currentKeyId))
                throw new ArgumentException("Current credential encryption key is not present in the key ring.", nameof(currentKeyId));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public EncryptedTenantCredential Protect(
        string connectionString,
        Guid tenantId,
        Guid idempotencyKey,
        string databaseName)
    {
        ThrowIfDisposed();
        ValidateContext(connectionString, tenantId, idempotencyKey, databaseName);

        var plaintext = StrictUtf8.GetBytes(connectionString);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        var associatedData = BuildAssociatedData(tenantId, idempotencyKey, databaseName, currentKeyId);

        try
        {
            using var aes = new AesGcm(keys[currentKeyId], TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            return new EncryptedTenantCredential(currentKeyId, nonce, ciphertext, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    public string Unprotect(
        EncryptedTenantCredential encrypted,
        Guid tenantId,
        Guid idempotencyKey,
        string databaseName)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(encrypted);
        ValidateTenantContext(tenantId, idempotencyKey, databaseName);

        if (string.IsNullOrEmpty(encrypted.KeyId)
            || !KeyIdPattern.IsMatch(encrypted.KeyId)
            || !keys.TryGetValue(encrypted.KeyId, out var key)
            || encrypted.Nonce is null
            || encrypted.Tag is null
            || encrypted.Ciphertext is null
            || encrypted.Nonce.Length != NonceSize
            || encrypted.Tag.Length != TagSize
            || encrypted.Ciphertext.Length is < 1 or > MaxConnectionStringLength * 4)
            throw new InvalidOperationException("Stored tenant credential envelope is invalid.");

        var plaintext = new byte[encrypted.Ciphertext.Length];
        var associatedData = BuildAssociatedData(tenantId, idempotencyKey, databaseName, encrypted.KeyId);

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(encrypted.Nonce, encrypted.Ciphertext, encrypted.Tag, plaintext, associatedData);
            var connectionString = StrictUtf8.GetString(plaintext);
            if (connectionString.Length is < 1 or > MaxConnectionStringLength)
                throw new InvalidOperationException("Decrypted tenant credential has an invalid length.");
            return connectionString;
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Stored tenant credential authentication failed.");
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidOperationException("Stored tenant credential encoding is invalid.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        foreach (var key in keys.Values)
            CryptographicOperations.ZeroMemory(key);
        keys.Clear();
        disposed = true;
    }

    private static void ValidateContext(
        string connectionString, Guid tenantId, Guid idempotencyKey, string databaseName)
    {
        ValidateTenantContext(tenantId, idempotencyKey, databaseName);
        if (string.IsNullOrWhiteSpace(connectionString)
            || connectionString.Length > MaxConnectionStringLength)
            throw new ArgumentException("Tenant connection string is invalid.");
    }

    private static void ValidateTenantContext(
        Guid tenantId, Guid idempotencyKey, string databaseName)
    {
        if (tenantId == Guid.Empty || idempotencyKey == Guid.Empty)
            throw new ArgumentException("Tenant and idempotency identifiers must be non-empty.");
        var expectedDatabase = $"mg_tenant_{tenantId:N}";
        if (!string.Equals(databaseName, expectedDatabase, StringComparison.Ordinal))
            throw new ArgumentException("Tenant database name does not match its immutable tenant ID.");
    }

    private static byte[] BuildAssociatedData(
        Guid tenantId, Guid idempotencyKey, string databaseName, string keyId) =>
        Encoding.UTF8.GetBytes(
            $"mg-tenant-credential:v1\n{tenantId:N}\n{idempotencyKey:N}\n{databaseName}\n{keyId}");

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}

public sealed record EncryptedTenantCredential(
    string KeyId,
    byte[] Nonce,
    byte[] Ciphertext,
    byte[] Tag);
