namespace MG.BusinessPlatform.Domain.Finance;

/// <summary>
/// The side of a double-entry journal line.
/// </summary>
public enum EntrySide
{
    Debit = 1,
    Credit = 2
}

/// <summary>
/// One posting to a general-ledger account.
/// </summary>
public sealed record JournalLine
{
    public string AccountCode { get; }
    public string AccountName { get; }
    public EntrySide Side { get; }
    public Money Amount { get; }
    public string? Memo { get; }

    public JournalLine(string accountCode, string accountName, EntrySide side, Money amount, string? memo = null)
    {
        if (string.IsNullOrWhiteSpace(accountCode))
            throw new ArgumentException("An account code is required.", nameof(accountCode));
        if (string.IsNullOrWhiteSpace(accountName))
            throw new ArgumentException("An account name is required.", nameof(accountName));
        if (side is not EntrySide.Debit and not EntrySide.Credit)
            throw new ArgumentOutOfRangeException(nameof(side), "A journal line must be debit or credit.");
        if (amount.Amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "Journal line amounts must be greater than zero.");

        AccountCode = accountCode.Trim();
        AccountName = accountName.Trim();
        Side = side;
        Amount = amount;
        Memo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim();
    }
}

/// <summary>
/// An immutable, balanced journal entry. Posting, account existence, and period
/// authorization are enforced by the application/persistence layer.
/// </summary>
public sealed class JournalEntry
{
    private readonly IReadOnlyList<JournalLine> _lines;

    public Guid Id { get; }
    public DateOnly EntryDate { get; }
    public string Description { get; }
    public string CurrencyCode { get; }
    public IReadOnlyList<JournalLine> Lines => _lines;
    public Money TotalDebits { get; }
    public Money TotalCredits { get; }

    public JournalEntry(Guid id, DateOnly entryDate, string description, IEnumerable<JournalLine> lines)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A journal entry ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("A journal description is required.", nameof(description));
        ArgumentNullException.ThrowIfNull(lines);

        var materialized = lines.ToArray();
        if (materialized.Length < 2)
            throw new ArgumentException("A journal entry requires at least two lines.", nameof(lines));
        if (materialized.Any(line => line is null))
            throw new ArgumentException("Journal lines cannot contain null values.", nameof(lines));

        var currency = materialized[0].Amount.CurrencyCode;
        if (materialized.Any(line => !string.Equals(line.Amount.CurrencyCode, currency, StringComparison.Ordinal)))
            throw new InvalidOperationException("A journal entry must use one currency. Post explicit exchange transactions for currency conversion.");

        var debits = materialized.Where(line => line.Side == EntrySide.Debit)
            .Aggregate(0m, (total, line) => checked(total + line.Amount.Amount));
        var credits = materialized.Where(line => line.Side == EntrySide.Credit)
            .Aggregate(0m, (total, line) => checked(total + line.Amount.Amount));

        if (debits <= 0m || credits <= 0m)
            throw new ArgumentException("A journal entry must contain at least one debit and one credit.", nameof(lines));
        if (debits != credits)
            throw new InvalidOperationException($"Journal entry is not balanced: debits={debits}, credits={credits} {currency}.");

        Id = id;
        EntryDate = entryDate;
        Description = description.Trim();
        CurrencyCode = currency;
        TotalDebits = new Money(debits, currency);
        TotalCredits = new Money(credits, currency);
        _lines = Array.AsReadOnly(materialized);
    }
}
