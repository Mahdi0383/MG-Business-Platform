namespace MG.BusinessPlatform.Domain.Finance;

public sealed record GeneralLedgerLine(
    Guid EntryId,
    DateOnly EntryDate,
    string Description,
    EntrySide Side,
    Money Debit,
    Money Credit,
    Money RunningBalance);

public sealed class GeneralLedgerReport
{
    public string AccountCode { get; }
    public string AccountName { get; }
    public AccountType AccountType { get; }
    public DateOnly FromDate { get; }
    public DateOnly ToDate { get; }
    public string CurrencyCode { get; }
    public Money OpeningBalance { get; }
    public IReadOnlyList<GeneralLedgerLine> Lines { get; }
    public Money PeriodDebits { get; }
    public Money PeriodCredits { get; }
    public Money ClosingBalance { get; }

    internal GeneralLedgerReport(Account account, DateOnly fromDate, DateOnly toDate, string currencyCode,
        Money openingBalance, IReadOnlyList<GeneralLedgerLine> lines)
    {
        AccountCode = account.Code;
        AccountName = account.Name;
        AccountType = account.Type;
        FromDate = fromDate;
        ToDate = toDate;
        CurrencyCode = currencyCode;
        OpeningBalance = openingBalance;
        Lines = lines;
        PeriodDebits = lines.Aggregate(Money.Zero(currencyCode), (sum, line) => sum + line.Debit);
        PeriodCredits = lines.Aggregate(Money.Zero(currencyCode), (sum, line) => sum + line.Credit);
        ClosingBalance = lines.Count == 0 ? openingBalance : lines[^1].RunningBalance;
    }
}

public static class GeneralLedgerCalculator
{
    public static GeneralLedgerReport Calculate(
        IEnumerable<Account> accounts,
        IEnumerable<JournalEntry> journalEntries,
        string accountCode,
        DateOnly fromDate,
        DateOnly toDate,
        string currencyCode)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(journalEntries);
        if (string.IsNullOrWhiteSpace(accountCode))
            throw new ArgumentException("An account code is required.", nameof(accountCode));
        if (fromDate > toDate)
            throw new ArgumentException("Start date must not be after end date.", nameof(fromDate));

        var currency = Money.Zero(currencyCode).CurrencyCode;
        var accountMap = new Dictionary<string, Account>(StringComparer.Ordinal);
        foreach (var account in accounts)
            if (!accountMap.TryAdd(account.Code, account))
                throw new InvalidOperationException($"Duplicate account code: {account.Code}.");

        if (!accountMap.TryGetValue(accountCode.Trim(), out var selectedAccount))
            throw new KeyNotFoundException($"Account code {accountCode.Trim()} was not found.");
        if (!selectedAccount.IsActive || !selectedAccount.IsPostingAllowed)
            throw new InvalidOperationException($"Account {selectedAccount.Code} is inactive or does not allow postings.");

        var relevantEntries = journalEntries
            .Where(entry => entry.EntryDate <= toDate
                && string.Equals(entry.CurrencyCode, currency, StringComparison.Ordinal))
            .OrderBy(entry => entry.EntryDate)
            .ThenBy(entry => entry.Id)
            .ToArray();

        var opening = Money.Zero(currency);
        foreach (var entry in relevantEntries.Where(entry => entry.EntryDate < fromDate))
        {
            foreach (var line in entry.Lines.Where(line => string.Equals(line.AccountCode, selectedAccount.Code, StringComparison.Ordinal)))
                opening += SignedMovement(selectedAccount, line.Side, line.Amount);
        }

        var lines = new List<GeneralLedgerLine>();
        var running = opening;
        foreach (var entry in relevantEntries.Where(entry => entry.EntryDate >= fromDate))
        {
            foreach (var line in entry.Lines.Where(line => string.Equals(line.AccountCode, selectedAccount.Code, StringComparison.Ordinal)))
            {
                running += SignedMovement(selectedAccount, line.Side, line.Amount);
                lines.Add(new GeneralLedgerLine(
                    entry.Id,
                    entry.EntryDate,
                    entry.Description,
                    line.Side,
                    line.Side == EntrySide.Debit ? line.Amount : Money.Zero(currency),
                    line.Side == EntrySide.Credit ? line.Amount : Money.Zero(currency),
                    running));
            }
        }

        return new GeneralLedgerReport(selectedAccount, fromDate, toDate, currency, opening,
            Array.AsReadOnly(lines.ToArray()));
    }

    private static Money SignedMovement(Account account, EntrySide side, Money amount) =>
        side == account.NormalBalance ? amount : new Money(checked(-amount.Amount), amount.CurrencyCode);
}
