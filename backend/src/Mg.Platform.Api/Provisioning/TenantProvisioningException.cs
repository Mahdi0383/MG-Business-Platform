namespace Mg.Platform.Api.Provisioning;

/// <summary>A failure with a stable, non-sensitive code suitable for storage.</summary>
public sealed class TenantProvisioningException(
    string safeErrorCode,
    bool permanent = false) : Exception("Tenant provisioning operation failed.")
{
    public string SafeErrorCode { get; } = Validate(safeErrorCode);
    public bool Permanent { get; } = permanent;

    private static string Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80
            || value.Any(character => !(char.IsAsciiLetterUpper(character)
                || char.IsAsciiDigit(character) || character == '_')))
            throw new ArgumentException("Error code must be a safe uppercase identifier.", nameof(value));
        return value;
    }
}
