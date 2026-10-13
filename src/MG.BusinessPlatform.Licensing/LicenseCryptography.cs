using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MG.BusinessPlatform.Licensing;

public static class LicenseCryptography
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private const string TrustedPublicKeyResourceName = "MG.BusinessPlatform.Licensing.TrustedLicensePublicKey.pem";

    public static string GetTrustedPublicKeyPem()
    {
        using var stream = typeof(LicenseCryptography).Assembly.GetManifestResourceStream(TrustedPublicKeyResourceName)
            ?? throw new InvalidOperationException("The trusted license public key is not embedded in this build.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var pem = reader.ReadToEnd();
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        if (rsa.KeySize < 2048)
            throw new CryptographicException("The embedded trusted public key is too small.");
        return pem;
    }

    public static bool PublicKeysMatch(string candidatePublicKeyPem, string trustedPublicKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePublicKeyPem);
        ArgumentException.ThrowIfNullOrWhiteSpace(trustedPublicKeyPem);
        using var candidate = RSA.Create();
        using var trusted = RSA.Create();
        candidate.ImportFromPem(candidatePublicKeyPem);
        trusted.ImportFromPem(trustedPublicKeyPem);
        return candidate.KeySize >= 2048 && trusted.KeySize >= 2048
            && CryptographicOperations.FixedTimeEquals(
                candidate.ExportSubjectPublicKeyInfo(), trusted.ExportSubjectPublicKeyInfo());
    }

    public static bool PrivateKeyMatchesPublicKey(string privateKeyPem, string expectedPublicKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPublicKeyPem);
        using var privateKey = RSA.Create();
        using var publicKey = RSA.Create();
        privateKey.ImportFromPem(privateKeyPem);
        publicKey.ImportFromPem(expectedPublicKeyPem);
        return privateKey.KeySize >= 2048 && publicKey.KeySize >= 2048
            && CryptographicOperations.FixedTimeEquals(
                privateKey.ExportSubjectPublicKeyInfo(), publicKey.ExportSubjectPublicKeyInfo());
    }

    public static (string PrivateKeyPem, string PublicKeyPem) GenerateKeyPair(int keySize = 3072)
    {
        if (keySize is < 2048 or > 8192 || keySize % 256 != 0)
            throw new ArgumentOutOfRangeException(nameof(keySize));

        using var rsa = RSA.Create(keySize);
        return (rsa.ExportPkcs8PrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
    }

    public static LicenseEnvelope Sign(LicensePayload payload, string privateKeyPem)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);
        var validated = payload.Validate();
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        if (rsa.KeySize < 2048)
            throw new CryptographicException("License signing key must be at least 2048 bits.");

        var bytes = SerializePayload(validated);
        var signature = rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        return new LicenseEnvelope(validated, Convert.ToBase64String(signature));
    }

    public static string SerializeEnvelope(LicenseEnvelope envelope) =>
        JsonSerializer.Serialize(envelope, JsonOptions);

    public static LicenseEnvelope DeserializeEnvelope(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<LicenseEnvelope>(json, JsonOptions)
            ?? throw new InvalidDataException("License document is empty.");
    }

    public static LicenseValidationResult Verify(
        LicenseEnvelope envelope,
        string? publicKeyPem,
        string expectedSoftwareSerial,
        string expectedHardwareSerial,
        DateTimeOffset nowUtc)
    {
        if (envelope is null)
            return new(LicenseValidationStatus.InvalidFormat, "License document is missing.");
        if (string.IsNullOrWhiteSpace(publicKeyPem))
            return new(LicenseValidationStatus.MissingPublicKey, "Trusted license public key is not configured.");
        if (envelope.Payload is null || string.IsNullOrWhiteSpace(envelope.SignatureBase64))
            return new(LicenseValidationStatus.InvalidFormat, "License document must contain a payload and signature.");
        if (!publicKeyPem.Contains("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal)
            || publicKeyPem.Contains("PRIVATE KEY", StringComparison.Ordinal))
            return new(LicenseValidationStatus.InvalidSignature, "Verification requires a public-key PEM; private keys are never accepted by the application.");

        var payload = envelope.Payload;
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(envelope.SignatureBase64);
        }
        catch (FormatException ex)
        {
            return new(LicenseValidationStatus.InvalidFormat, $"License signature encoding is invalid: {ex.Message}");
        }

        // Authenticate the deserialized payload before semantic validation or
        // trusting any claim such as expiry, product ID, or device serial.
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            if (rsa.KeySize < 2048 || !rsa.VerifyData(SerializePayload(payload), signature,
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                return new(LicenseValidationStatus.InvalidSignature, "License signature is invalid.");
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return new(LicenseValidationStatus.InvalidSignature, $"License signature could not be verified: {ex.Message}");
        }

        try
        {
            payload = payload.Validate();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or FormatException)
        {
            return new(LicenseValidationStatus.InvalidFormat, $"Signed license payload is invalid: {ex.Message}");
        }

        if (!string.Equals(payload.ProductId, "MG-Business-Platform", StringComparison.Ordinal))
            return new(LicenseValidationStatus.WrongProduct, "License was issued for a different product.", payload);
        if (!string.Equals(payload.SoftwareSerial, NormalizeSerial(expectedSoftwareSerial), StringComparison.Ordinal))
            return new(LicenseValidationStatus.WrongSoftwareSerial, "Software serial does not match this installation.", payload);
        if (!string.Equals(payload.HardwareSerial, NormalizeSerial(expectedHardwareSerial), StringComparison.Ordinal))
            return new(LicenseValidationStatus.WrongHardwareSerial, "Hardware serial does not match this computer.", payload);
        if (nowUtc.ToUniversalTime() < payload.IssuedAtUtc)
            return new(LicenseValidationStatus.NotYetValid, "License is not active yet.", payload);
        if (nowUtc.ToUniversalTime() >= payload.ExpiresAtUtc)
            return new(LicenseValidationStatus.Expired, "License has expired. Business data has not been modified.", payload);

        return new(LicenseValidationStatus.Valid, "License is valid.", payload);
    }

    public static string SerializePayloadForSignature(LicensePayload payload) =>
        Encoding.UTF8.GetString(SerializePayload(payload.Validate()));

    private static byte[] SerializePayload(LicensePayload payload) =>
        JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);

    private static string NormalizeSerial(string value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();
}
