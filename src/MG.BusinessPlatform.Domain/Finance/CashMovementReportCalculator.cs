namespace MG.BusinessPlatform.Domain.Finance;

public sealed record CashMovementLine(
    string AccountCode,
    string AccountName,
    Money OpeningBalance,
    Money Inflows,
    Money Outflows,
    Money ClosingBalance);

/// <summary>
/// A period movement summary for explicitly selected cash and bank ledger accounts.
/// This is a cash/bank movement report, not a classified statutory cash-flow statement.
/// </summary>
public sealed class CashMovementReport
{
    public DateOnly FromDate { get; }
    public DateOnly ToDate { get; }
    public string CurrencyCode { get; }
    public IReadOnlyList<CashMovementLine> Lines { get; }
    public Money OpeningBalance { get; }
    public Money TotalInflows { get; }
    public Money TotalOutflows { get; }
    public Money NetMovement { get; }
    public Money ClosingBalance { get; }

    internal CashMovementReport(DateOnly fromDate, DateOnly toDate, string currencyCode,
        IReadOnlyList<CashMovementLine> lines)
    {
        FromDate = fromDate;
        ToDate = toDate;
        CurrencyCode = Money.Zero(currencyCode).CurrencyCode;
        Lines = lines;
        OpeningBalance = lines.Aggregate(Money.Zero(CurrencyCode), (sum, line) => sum + line.OpeningBalance);
        TotalInflows = lines.Aggregate(Money.Zero(CurrencyCode), (sum, line) => sum + line.Inflows);
        TotalOutflows = lines.Aggregate(Money.Zero(CurrencyCode), (sum, line) => sum + line.Outflows);
        NetMovement = TotalInflows - TotalOutflows;
        ClosingBalance = OpeningBalance + NetMovement;
    }
}

public static class CashMovementReportCalculator
{
    /// <summary>
    /// Calculates opening balances, debit-side inflows, credit-side outflows and closing
    /// balances for cash/bank accounts. The caller explicitly supplies the cash account codes.
    /// </summary>
    public static CashMovementReport Calculate(
        IEnumerable<Account> accounts,
        IEnumerable<JournalEntry> journalEntries,
        DateOnly fromDate,
        DateOnly toDate,
        string currencyCode,
        IEnumerable<string> cashAccountCodes)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(journalEntries);
        ArgumentNullException.ThrowIfNull(cashAccountCodes);
        if (fromDate > toDate)
            throw new ArgumentException("Start date must not be after end date.", nameof(fromDate));

        var currency = Money.Zero(currencyCode).CurrencyCode;
        var accountMap = new Dictionary<string, Account>(StringComparer.Ordinal);
        foreach (var account in accounts)
            if (!accountMap.TryAdd(account.Code, account))
                throw new InvalidOperationException($"Duplicate account code: {account.Code}.");

        var selectedCodes = cashAccountCodes
            .Select(code => string.IsNullOrWhiteSpace(code) ? throw new ArgumentException("Cash account codes cannot be empty.", nameof(cashAccountCodes)) : code.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
        if (selectedCodes.Length == 0)
            throw new ArgumentException("Select at least one cash or bank account.", nameof(cashAccountCodes));

        var selectedAccounts = new List<Account>(selectedCodes.Length);
        foreach (var code in selectedCodes)
        {
            if (!accountMap.TryGetValue(code, out var account))
                throw new InvalidOperationException($"Cash/bank account code {code} does not exist.");
            if (account.Type != AccountType.Asset || !account.IsPostingAllowed)
                throw new InvalidOperationException($"Cash/bank account code {code} must be a posting-enabled asset account.");
            selectedAccounts.Add(account);
        }

        var totals = selectedCodes.ToDictionary(code => code,
            _ => (OpeningDebits: 0m, OpeningCredits: 0m, Inflows: 0m, Outflows: 0m),
            StringComparer.Ordinal);

        foreach (var entry in journalEntries)
        {
            if (!string.Equals(entry.CurrencyCode, currency, StringComparison.Ordinal)
                || entry.EntryDate > toDate)
                continue;

            foreach (var line in entry.Lines)
            {
                if (!totals.TryGetValue(line.AccountCode, out var current))
                    continue;

                if (entry.EntryDate < fromDate)
                {
                    totals[line.AccountCode] = line.Side == EntrySide.Debit
                        ? current with { OpeningDebits = checked(current.OpeningDebits + line.Amount.Amount) }
                        : current with { OpeningCredits = checked(current.OpeningCredits + line.Amount.Amount) };
                }
                else
                {
                    totals[line.AccountCode] = line.Side == EntrySide.Debit
                        ? current with { Inflows = checked(current.Inflows + line.Amount.Amount) }
                        : current with { Outflows = checked(current.Outflows + line.Amount.Amount) };
                }
            }
        }

        var lines = selectedAccounts.Select(account =>
        {
            var total = totals[account.Code];
            var opening = checked(total.OpeningDebits - total.OpeningCredits);
            var closing = checked(checked(opening + total.Inflows) - total.Outflows);
            return new CashMovementLine(account.Code, account.Name,
                new Money(opening, currency), new Money(total.Inflows, currency),
                new Money(total.Outflows, currency), new Money(closing, currency));
        }).ToArray();

        return new CashMovementReport(fromDate, toDate, currency, Array.AsReadOnly(lines));
    }
}
