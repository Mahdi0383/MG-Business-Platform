using MG.BusinessPlatform.Licensing;

public interface IStorefrontLicenseGuard
{
    LicenseValidationResult Validate();
}

public sealed class StorefrontLicenseGuard : IStorefrontLicenseGuard
{
    private readonly string? _dataDirectory = Environment.GetEnvironmentVariable("MG_BUSINESS_DATA_PATH");
    private readonly object _sync = new();
    private LicenseValidationResult? _cached;
    private DateTimeOffset _cachedAtUtc;

    public LicenseValidationResult Validate()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            var cacheIsFresh = _cached is not null
                && now - _cachedAtUtc < TimeSpan.FromSeconds(5)
                && (_cached.Payload is null || now < _cached.Payload.ExpiresAtUtc);
            if (cacheIsFresh)
                return _cached!;

            _cached = LicenseRuntimeValidator.ValidateDirectory(_dataDirectory, now);
            _cachedAtUtc = now;
            return _cached;
        }
    }
}
