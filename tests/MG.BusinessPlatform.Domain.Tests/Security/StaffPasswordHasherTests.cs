using MG.BusinessPlatform.Infrastructure.Security;

namespace MG.BusinessPlatform.Domain.Tests.Security;

public sealed class StaffPasswordHasherTests
{
    private readonly StaffPasswordHasher _hasher = new();

    [Fact]
    public void Hash_UsesRandomSaltAndDoesNotContainThePassword()
    {
        const string password = "correct horse battery staple 2026";
        var first = _hasher.Hash(password);
        var second = _hasher.Hash(password);

        Assert.NotEqual(first, second);
        Assert.DoesNotContain(password, first, StringComparison.Ordinal);
        Assert.StartsWith("pbkdf2-sha256$600000$", first, StringComparison.Ordinal);
        Assert.True(_hasher.Verify(password, first));
        Assert.True(_hasher.Verify(password, second));
    }

    [Fact]
    public void Verify_RejectsIncorrectPassword()
    {
        var hash = _hasher.Hash("a sufficiently long passphrase");

        Assert.False(_hasher.Verify("a different passphrase", hash));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-password-hash")]
    [InlineData("pbkdf2-sha256$1$AA==$AA==")]
    [InlineData("pbkdf2-sha256$999999999$AA==$AA==")]
    [InlineData("pbkdf2-sha256$600000$not-base64$not-base64")]
    [InlineData("pbkdf2-sha256$600000$AA==$AA==")]
    public void Verify_FailsClosedForMalformedOrUnsafeHash(string? encodedHash)
    {
        Assert.False(_hasher.Verify("a sufficiently long passphrase", encodedHash));
        Assert.True(_hasher.NeedsRehash(encodedHash));
    }

    [Fact]
    public void Hash_RejectsEmptyAndOversizedPasswords()
    {
        Assert.Throws<ArgumentException>(() => _hasher.Hash(string.Empty));
        Assert.Throws<ArgumentException>(() => _hasher.Hash(new string('x', 1_025)));
    }

    [Fact]
    public void NeedsRehash_IsFalseForCurrentHash()
    {
        var hash = _hasher.Hash("a sufficiently long passphrase");

        Assert.False(_hasher.NeedsRehash(hash));
    }
}
