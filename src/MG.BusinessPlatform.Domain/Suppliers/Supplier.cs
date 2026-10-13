namespace MG.BusinessPlatform.Domain.Suppliers;

public sealed record Supplier
{
    public Guid Id { get; }
    public string DisplayName { get; }
    public string? Phone { get; }
    public string? Email { get; }
    public string? Address { get; }
    public string? TaxIdentifier { get; }
    public string? PaymentTerms { get; }
    public bool IsActive { get; }

    public Supplier(Guid id, string displayName, string? phone = null, string? email = null,
        string? address = null, string? taxIdentifier = null, string? paymentTerms = null, bool isActive = true)
    {
        if (id == Guid.Empty) throw new ArgumentException("Supplier ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Supplier name is required.", nameof(displayName));
        if (displayName.Trim().Length > 200) throw new ArgumentOutOfRangeException(nameof(displayName));
        if (phone?.Length > 40) throw new ArgumentOutOfRangeException(nameof(phone));
        if (email?.Length > 254) throw new ArgumentOutOfRangeException(nameof(email));
        if (address?.Length > 1000) throw new ArgumentOutOfRangeException(nameof(address));
        if (taxIdentifier?.Length > 80) throw new ArgumentOutOfRangeException(nameof(taxIdentifier));
        if (paymentTerms?.Length > 500) throw new ArgumentOutOfRangeException(nameof(paymentTerms));

        Id = id;
        DisplayName = displayName.Trim();
        Phone = NormalizeOptional(phone);
        Email = NormalizeOptional(email)?.ToLowerInvariant();
        Address = NormalizeOptional(address);
        TaxIdentifier = NormalizeOptional(taxIdentifier);
        PaymentTerms = NormalizeOptional(paymentTerms);
        IsActive = isActive;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
