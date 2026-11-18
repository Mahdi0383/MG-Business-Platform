using System.Security.Cryptography;
using Mg.Provisioning.Agent.Core;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class AgentCredentialProtectorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid IdempotencyKey = Guid.NewGuid();
    private static readonly string DatabaseName = $"mg_tenant_{TenantId:N}";
    private const string ConnectionString =
        "Host=tenant-db.internal;Database=mg_tenant_placeholder;Username=tenant_app;Password=not-a-real-test-secret;SSL Mode=VerifyFull";

    [Fact]
    public void ProtectAndUnprotectRoundTripsStableCredential()
    {
        using var protector = CreateProtector("key-v1", ("key-v1", RandomKey()));
        var credential = ConnectionString.Replace("mg_tenant_placeholder", DatabaseName, StringComparison.Ordinal);

        var encrypted = protector.Protect(credential, TenantId, IdempotencyKey, DatabaseName);
        var decrypted = protector.Unprotect(encrypted, TenantId, IdempotencyKey, DatabaseName);

        Assert.Equal(credential, decrypted);
        Assert.DoesNotContain(credential, Convert.ToBase64String(encrypted.Ciphertext), StringComparison.Ordinal);
    }

    [Fact]
    public void AuthenticatedDataBindsCredentialToTenantIdempotencyAndDatabase()
    {
        using var protector = CreateProtector("key-v1", ("key-v1", RandomKey()));
        var encrypted = protector.Protect(
            ConnectionString.Replace("mg_tenant_placeholder", DatabaseName, StringComparison.Ordinal),
            TenantId, IdempotencyKey, DatabaseName);

        Assert.Throws<InvalidOperationException>(() =>
            protector.Unprotect(encrypted, Guid.NewGuid(), IdempotencyKey, DatabaseName));
        Assert.Throws<InvalidOperationException>(() =>
            protector.Unprotect(encrypted, TenantId, Guid.NewGuid(), DatabaseName));
        Assert.Throws<ArgumentException>(() =>
            protector.Unprotect(encrypted, TenantId, IdempotencyKey, "mg_tenant_wrong"));
    }

    [Fact]
    public void RejectsCiphertextTampering()
    {
        using var protector = CreateProtector("key-v1", ("key-v1", RandomKey()));
        var encrypted = protector.Protect(
            ConnectionString.Replace("mg_tenant_placeholder", DatabaseName, StringComparison.Ordinal),
            TenantId, IdempotencyKey, DatabaseName);
        encrypted.Ciphertext[0] ^= 0x40;

        Assert.Throws<InvalidOperationException>(() =>
            protector.Unprotect(encrypted, TenantId, IdempotencyKey, DatabaseName));
    }

    [Fact]
    public void KeyRotationKeepsOldCredentialDecryptableAndEncryptsWithCurrentKey()
    {
        var oldKey = RandomKey();
        var newKey = RandomKey();
        EncryptedTenantCredential oldEnvelope;

        using (var oldProtector = CreateProtector("key-v1", ("key-v1", oldKey)))
        {
            oldEnvelope = oldProtector.Protect(
                ConnectionString.Replace("mg_tenant_placeholder", DatabaseName, StringComparison.Ordinal),
                TenantId, IdempotencyKey, DatabaseName);
        }

        using var rotated = CreateProtector("key-v2", ("key-v1", oldKey), ("key-v2", newKey));
        var decrypted = rotated.Unprotect(oldEnvelope, TenantId, IdempotencyKey, DatabaseName);
        var newEnvelope = rotated.Protect(decrypted, TenantId, IdempotencyKey, DatabaseName);

        Assert.Equal("key-v2", newEnvelope.KeyId);
        Assert.Equal(decrypted, rotated.Unprotect(newEnvelope, TenantId, IdempotencyKey, DatabaseName));
    }

    [Fact]
    public void RejectsMissingCurrentKeyAndNon256BitKeys()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateProtector("missing", ("key-v1", RandomKey())));
        Assert.Throws<ArgumentException>(() =>
            CreateProtector("key-v1", ("key-v1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)))));
    }

    private static AgentCredentialProtector CreateProtector(
        string currentKeyId, params (string Id, string Key)[] entries) =>
        new(currentKeyId, entries.ToDictionary(entry => entry.Id, entry => entry.Key));

    private static string RandomKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
