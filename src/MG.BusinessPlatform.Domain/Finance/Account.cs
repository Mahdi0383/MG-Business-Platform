namespace MG.BusinessPlatform.Domain.Finance;

/// <summary>Standard financial statement classification for a general-ledger account.</summary>
public enum AccountType
{
    Asset = 1,
    Liability = 2,
    Equity = 3,
    Revenue = 4,
    Expense = 5
}

/// <summary>
/// A chart-of-accounts definition. Uniqueness, parent existence, and cycle detection
/// must be enforced transactionally by the persistence/application layer.
/// </summary>
public sealed record Account
{
    public string Code { get; }
    public string Name { get; }
    public AccountType Type { get; }
    public string? ParentCode { get; }
    public bool IsPostingAllowed { get; }
    public bool IsActive { get; }

    public EntrySide NormalBalance => Type is AccountType.Asset or AccountType.Expense
        ? EntrySide.Debit
        : EntrySide.Credit;

    public Account(string code, string name, AccountType type, string? parentCode = null,
        bool isPostingAllowed = true, bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("An account code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("An account name is required.", nameof(name));
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));

        Code = code.Trim();
        Name = name.Trim();
        Type = type;
        ParentCode = string.IsNullOrWhiteSpace(parentCode) ? null : parentCode.Trim();
        if (ParentCode is not null && string.Equals(Code, ParentCode, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An account cannot be its own parent.", nameof(parentCode));
        IsPostingAllowed = isPostingAllowed;
        IsActive = isActive;
    }
}
