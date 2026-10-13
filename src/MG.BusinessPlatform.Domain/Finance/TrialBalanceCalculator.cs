namespace MG.BusinessPlatform.Domain.Finance;

public sealed record TrialBalanceLine(
    string AccountCode,
    string AccountName,
    AccountType AccountType,
    Money DebitTurnover,
    Money CreditTurnover,
    Money DebitBalance,
    Money CreditBalance);

public sealed class TrialBalanceReport
{
    public DateOnly AsOfDate { get; }
    public string CurrencyCode { get; }
    public IReadOnlyList<TrialBalanceLine> Lines { get; }
    public Money TotalDebitTurnover { get; }
    public Money TotalCreditTurnover { get; }
    public Money TotalDebitBalance { get; }
    public Money TotalCreditBalance { get; }
    public bool IsBalanced => TotalDebitTurnover == TotalCreditTurnover
        && TotalDebitBalance == TotalCreditBalance;

    internal TrialBalanceReport(DateOnly asOfDate, string currencyCode, IReadOnlyList<TrialBalanceLine> lines)
    {
        AsOfDate = asOfDate;
        CurrencyCode = currencyCode;
        Lines = lines;
        TotalDebitTurnover = lines.Aggregate(Money.Zero(currencyCode), (sum, line) => sum + line.DebitTurnover);
        TotalCreditTurnover = lines.Aggregate(Money.Zero(currencyCode), (sum, line) => sum + line.CreditTurnover);
        TotalDebitBalance = lines.Aggregate(Money.Zero(currencyCode), (sum, line) => sum + line.DebitBalance);
        TotalCreditBalance = lines.Aggregate(Money.Zero(currencyCode), (sum, line) => sum + line.CreditBalance);
    }
}

public static class TrialBalanceCalculator
{
    public static TrialBalanceReport Calculate(
        IEnumerable<Account> accounts,
        IEnumerable<JournalEntry> journalEntries,
        DateOnly asOfDate,
        string currencyCode)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(journalEntries);
        var zero = Money.Zero(currencyCode);
        var accountMap = new Dictionary<string, Account>(StringComparer.Ordinal);
        foreach (var account in accounts)
        {
            if (!accountMap.TryAdd(account.Code, account))
                throw new InvalidOperationException($"Duplicate account code: {account.Code}.");
        }

        var totals = accountMap.Keys.ToDictionary(
            code => code,
            _ => (Debits: zero, Credits: zero),
            StringComparer.Ordinal);

        foreach (var entry in journalEntries)
        {
            if (entry.EntryDate > asOfDate || !string.Equals(entry.CurrencyCode, zero.CurrencyCode, StringComparison.Ordinal))
                continue;

            foreach (var line in entry.Lines)
            {
                if (!accountMap.TryGetValue(line.AccountCode, out var account))
                    throw new InvalidOperationException($"Journal line references unknown account code {line.AccountCode}.");
                if (!account.IsPostingAllowed)
                    throw new InvalidOperationException($"Journal line references non-posting account code {line.AccountCode}.");

                var current = totals[line.AccountCode];
                totals[line.AccountCode] = line.Side == EntrySide.Debit
                    ? (current.Debits + line.Amount, current.Credits)
                    : (current.Debits, current.Credits + line.Amount);
            }
        }

        var lines = accountMap.Values
            .OrderBy(account => account.Code, StringComparer.Ordinal)
            .Select(account =>
            {
                var total = totals[account.Code];
                var net = total.Debits - total.Credits;
                return new TrialBalanceLine(
                    account.Code,
                    account.Name,
                    account.Type,
                    total.Debits,
                    total.Credits,
                    net.Amount > 0m ? net : zero,
                    net.Amount < 0m ? new Money(-net.Amount, zero.CurrencyCode) : zero);
            })
            .ToArray();

        return new TrialBalanceReport(asOfDate, zero.CurrencyCode, Array.AsReadOnly(lines));
    }
}
