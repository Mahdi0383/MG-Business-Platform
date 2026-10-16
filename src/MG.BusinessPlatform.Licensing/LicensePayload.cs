namespace MG.BusinessPlatform.Licensing;

public sealed record LicensePayload
{
    public string ProductId { get; init; } = "MG-Business-Platform";
    public Guid LicenseId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string SoftwareSerial { get; init; } = string.Empty;
    public string HardwareSerial { get; init; } = string.Empty;
    public DateTimeOffset IssuedAtUtc { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public string LicenseType { get; init; } = "Full";
    public string[] Modules { get; init; } = [];
    public int MaxBranches { get; init; } = 1;
    public int MaxUsers { get; init; } = 1;

    public static readonly string[] SupportedModules =
    [
        "Core", "Accounting", "Inventory", "POS", "Storefront",
        "Cafe", "GameCafe", "Reservations", "CustomerClub", "Reports", "MultiBranch"
    ];

    public LicensePayload Validate()
    {
        if (!string.Equals(ProductId, "MG-Business-Platform", StringComparison.Ordinal))
            throw new ArgumentException("Unsupported product ID.", nameof(ProductId));
        if (LicenseId == Guid.Empty)
            throw new ArgumentException("License ID is required.", nameof(LicenseId));
        if (string.IsNullOrWhiteSpace(CustomerName) || CustomerName.Trim().Length > 200)
            throw new ArgumentException("Customer name is required and must not exceed 200 characters.", nameof(CustomerName));
        if (string.IsNullOrWhiteSpace(SoftwareSerial) || SoftwareSerial.Trim().Length > 100)
            throw new ArgumentException("Software serial is required.", nameof(SoftwareSerial));
        if (string.IsNullOrWhiteSpace(HardwareSerial) || HardwareSerial.Trim().Length > 100)
            throw new ArgumentException("Hardware serial is required.", nameof(HardwareSerial));
        if (IssuedAtUtc.Offset != TimeSpan.Zero || ExpiresAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("License timestamps must be UTC.");
        if (ExpiresAtUtc <= IssuedAtUtc)
            throw new ArgumentException("License expiry must be after issue time.", nameof(ExpiresAtUtc));
        if (LicenseType is not ("Full" or "Trial"))
            throw new ArgumentException("License type must be Full or Trial.", nameof(LicenseType));
        if (MaxBranches is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(MaxBranches));
        if (MaxUsers is < 1 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(MaxUsers));
        if (Modules is null || Modules.Length == 0)
            throw new ArgumentException("At least one module entitlement is required.", nameof(Modules));
        if (Modules.Any(module => !SupportedModules.Contains(module, StringComparer.Ordinal)))
            throw new ArgumentException("License contains an unsupported module.", nameof(Modules));
        if (Modules.Distinct(StringComparer.Ordinal).Count() != Modules.Length)
            throw new ArgumentException("License module entitlements cannot contain duplicates.", nameof(Modules));
        if (!Modules.Contains("Core", StringComparer.Ordinal))
            throw new ArgumentException("Every license must include the Core module.", nameof(Modules));

        return this with
        {
            CustomerName = CustomerName.Trim(),
            SoftwareSerial = SoftwareSerial.Trim().ToUpperInvariant(),
            HardwareSerial = HardwareSerial.Trim().ToUpperInvariant(),
            Modules = Modules.OrderBy(module => module, StringComparer.Ordinal).ToArray()
        };
    }
}

public sealed record LicenseEnvelope(LicensePayload Payload, string SignatureBase64);

public enum LicenseValidationStatus
{
    Valid = 1,
    MissingLicense = 2,
    MissingPublicKey = 3,
    InvalidFormat = 4,
    InvalidSignature = 5,
    WrongProduct = 6,
    WrongSoftwareSerial = 7,
    WrongHardwareSerial = 8,
    NotYetValid = 9,
    Expired = 10
}

public sealed record LicenseValidationResult(
    LicenseValidationStatus Status,
    string Message,
    LicensePayload? Payload = null)
{
    public bool IsValid => Status == LicenseValidationStatus.Valid;
}
