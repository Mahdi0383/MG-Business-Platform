namespace MG.BusinessPlatform.Domain.Organizations;

public sealed record Business
{
    public Guid Id { get; }
    public string LegalName { get; }
    public string DisplayName { get; }
    public string DefaultCurrencyCode { get; }
    public string TimeZoneId { get; }
    public bool IsActive { get; }

    public Business(Guid id, string legalName, string displayName, string defaultCurrencyCode,
        string timeZoneId, bool isActive = true)
    {
        if (id == Guid.Empty) throw new ArgumentException("Business ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(legalName)) throw new ArgumentException("Legal name is required.", nameof(legalName));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Display name is required.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(defaultCurrencyCode)) throw new ArgumentException("Default currency is required.", nameof(defaultCurrencyCode));
        var currency = defaultCurrencyCode.Trim().ToUpperInvariant();
        if (currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Currency code must contain three ASCII letters.", nameof(defaultCurrencyCode));
        if (string.IsNullOrWhiteSpace(timeZoneId)) throw new ArgumentException("Time zone ID is required.", nameof(timeZoneId));

        Id = id;
        LegalName = legalName.Trim();
        DisplayName = displayName.Trim();
        DefaultCurrencyCode = currency;
        TimeZoneId = timeZoneId.Trim();
        IsActive = isActive;
    }
}

public sealed record Branch
{
    public Guid Id { get; }
    public Guid BusinessId { get; }
    public string Code { get; }
    public string Name { get; }
    public string? Address { get; }
    public string? Phone { get; }
    public bool IsActive { get; }

    public Branch(Guid id, Guid businessId, string code, string name,
        string? address = null, string? phone = null, bool isActive = true)
    {
        if (id == Guid.Empty) throw new ArgumentException("Branch ID is required.", nameof(id));
        if (businessId == Guid.Empty) throw new ArgumentException("Business ID is required.", nameof(businessId));
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Branch code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Branch name is required.", nameof(name));

        Id = id;
        BusinessId = businessId;
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        IsActive = isActive;
    }
}
