using MG.BusinessPlatform.Licensing;

namespace MG.BusinessPlatform.Domain.Tests.Licensing;

public sealed class LicenseCryptographyTests
{
    [Fact]
    public void SignAndVerify_AcceptsValidLicenseForMatchingInstallation()
    {
        var keys = LicenseCryptography.GenerateKeyPair(2048);
        var payload = CreatePayload(DateTimeOffset.UtcNow.AddDays(30));
        var envelope = LicenseCryptography.Sign(payload, keys.PrivateKeyPem);
        var json = LicenseCryptography.SerializeEnvelope(envelope);
        var decoded = LicenseCryptography.DeserializeEnvelope(json);

        var result = LicenseCryptography.Verify(decoded, keys.PublicKeyPem,
            payload.SoftwareSerial.ToLowerInvariant(), payload.HardwareSerial.ToLowerInvariant(), DateTimeOffset.UtcNow);

        Assert.True(result.IsValid);
        Assert.Equal(LicenseValidationStatus.Valid, result.Status);
        Assert.Equal(payload.LicenseId, result.Payload!.LicenseId);
    }

    [Fact]
    public void Verify_RejectsTamperedPayload()
    {
        var keys = LicenseCryptography.GenerateKeyPair(2048);
        var payload = CreatePayload(DateTimeOffset.UtcNow.AddDays(30));
        var signed = LicenseCryptography.Sign(payload, keys.PrivateKeyPem);
        var tampered = signed with { Payload = signed.Payload with { CustomerName = "Tampered customer" } };

        var result = LicenseCryptography.Verify(tampered, keys.PublicKeyPem,
            payload.SoftwareSerial, payload.HardwareSerial, DateTimeOffset.UtcNow);

        Assert.False(result.IsValid);
        Assert.Equal(LicenseValidationStatus.InvalidSignature, result.Status);
    }

    [Fact]
    public void Verify_RejectsTamperedExpiryBeforeTrustingExpiryClaim()
    {
        var keys = LicenseCryptography.GenerateKeyPair(2048);
        var payload = CreatePayload(DateTimeOffset.UtcNow.AddDays(30));
        var signed = LicenseCryptography.Sign(payload, keys.PrivateKeyPem);
        var tampered = signed with
        {
            Payload = signed.Payload with { ExpiresAtUtc = payload.ExpiresAtUtc.AddYears(10) }
        };

        var result = LicenseCryptography.Verify(tampered, keys.PublicKeyPem,
            payload.SoftwareSerial, payload.HardwareSerial, DateTimeOffset.UtcNow);

        Assert.Equal(LicenseValidationStatus.InvalidSignature, result.Status);
    }

    [Fact]
    public void Verify_RejectsTamperedSerialBeforeTrustingSerialClaim()
    {
        var keys = LicenseCryptography.GenerateKeyPair(2048);
        var payload = CreatePayload(DateTimeOffset.UtcNow.AddDays(30));
        var signed = LicenseCryptography.Sign(payload, keys.PrivateKeyPem);
        var tampered = signed with
        {
            Payload = signed.Payload with { HardwareSerial = "MGH-ATTACKER-EDITED" }
        };

        var result = LicenseCryptography.Verify(tampered, keys.PublicKeyPem,
            payload.SoftwareSerial, payload.HardwareSerial, DateTimeOffset.UtcNow);

        Assert.Equal(LicenseValidationStatus.InvalidSignature, result.Status);
    }

    [Fact]
    public void Verify_RejectsWrongHardwareSerial()
    {
        var keys = LicenseCryptography.GenerateKeyPair(2048);
        var payload = CreatePayload(DateTimeOffset.UtcNow.AddDays(30));
        var envelope = LicenseCryptography.Sign(payload, keys.PrivateKeyPem);

        var result = LicenseCryptography.Verify(envelope, keys.PublicKeyPem,
            payload.SoftwareSerial, "MGH-WRONG-HARDWARE", DateTimeOffset.UtcNow);

        Assert.Equal(LicenseValidationStatus.WrongHardwareSerial, result.Status);
    }

    [Fact]
    public void Verify_RejectsExpiredLicense()
    {
        var keys = LicenseCryptography.GenerateKeyPair(2048);
        var now = DateTimeOffset.UtcNow;
        var payload = CreatePayload(now.AddMinutes(-1)) with
        {
            IssuedAtUtc = now.AddDays(-2),
            ExpiresAtUtc = now.AddSeconds(-1)
        };
        var envelope = LicenseCryptography.Sign(payload, keys.PrivateKeyPem);

        var result = LicenseCryptography.Verify(envelope, keys.PublicKeyPem,
            payload.SoftwareSerial, payload.HardwareSerial, now);

        Assert.Equal(LicenseValidationStatus.Expired, result.Status);
    }

    [Fact]
    public void Verify_ReportsMissingPublicKey()
    {
        var keys = LicenseCryptography.GenerateKeyPair(2048);
        var payload = CreatePayload(DateTimeOffset.UtcNow.AddDays(30));
        var envelope = LicenseCryptography.Sign(payload, keys.PrivateKeyPem);

        var result = LicenseCryptography.Verify(envelope, null,
            payload.SoftwareSerial, payload.HardwareSerial, DateTimeOffset.UtcNow);

        Assert.Equal(LicenseValidationStatus.MissingPublicKey, result.Status);
    }

    [Fact]
    public void Sign_RejectsUnsupportedModules()
    {
        var keys = LicenseCryptography.GenerateKeyPair(2048);
        var payload = CreatePayload(DateTimeOffset.UtcNow.AddDays(30)) with { Modules = ["RootAccess"] };

        Assert.Throws<ArgumentException>(() => LicenseCryptography.Sign(payload, keys.PrivateKeyPem));
    }

    [Fact]
    public void Verify_RejectsEnvelopeWithoutPayload()
    {
        var result = LicenseCryptography.Verify(
            new LicenseEnvelope(null!, "AA=="), "not-used", "MGS-SOFTWARE", "MGH-HARDWARE", DateTimeOffset.UtcNow);

        Assert.Equal(LicenseValidationStatus.InvalidFormat, result.Status);
    }

    [Fact]
    public void Verify_ReportsMalformedPublicKeyWithoutThrowing()
    {
        var keys = LicenseCryptography.GenerateKeyPair(2048);
        var payload = CreatePayload(DateTimeOffset.UtcNow.AddDays(30));
        var envelope = LicenseCryptography.Sign(payload, keys.PrivateKeyPem);

        var result = LicenseCryptography.Verify(envelope, "not-a-valid-pem",
            payload.SoftwareSerial, payload.HardwareSerial, DateTimeOffset.UtcNow);

        Assert.Equal(LicenseValidationStatus.InvalidSignature, result.Status);
    }

    private static LicensePayload CreatePayload(DateTimeOffset expiry) => new()
    {
        LicenseId = Guid.NewGuid(),
        CustomerName = "Test Customer",
        SoftwareSerial = "MGS-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
        HardwareSerial = "MGH-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
        IssuedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        ExpiresAtUtc = expiry,
        LicenseType = "Full",
        Modules = ["Core", "POS", "Accounting"],
        MaxBranches = 2,
        MaxUsers = 10
    };
}
