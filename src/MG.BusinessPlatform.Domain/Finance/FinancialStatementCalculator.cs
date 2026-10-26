namespace MG.BusinessPlatform.Domain.Finance;

public sealed record FinancialStatementLine(
    string AccountCode,
    string AccountName,
    AccountType AccountType,
    Money Balance);

public sealed class ProfitAndLossReport
{
    public DateOnly FromDate { get; }
    public DateOnly ToDate { get; }
    public string CurrencyCode { get; }
    public IReadOnlyList<FinancialStatementLine> RevenueLines { get; }
    public IReadOnlyList<FinancialStatementLine> ExpenseLines { get; }
    public Money TotalRevenue { get; }
    public Money TotalExpenses { get; }
    public Money NetProfit { get; }

    internal ProfitAndLossReport(DateOnly fromDate, DateOnly toDate, string currencyCode,
        IReadOnlyList<FinancialStatementLine> revenueLines, IReadOnlyList<FinancialStatementLine> expenseLines)
    {
        FromDate = fromDate;
        ToDate = toDate;
        CurrencyCode = currencyCode;
        RevenueLines = revenueLines;
        ExpenseLines = expenseLines;
        TotalRevenue = revenueLines.Aggregate(Money.Zero(currencyCode), (sum, line) => sum + line.Balance);
        TotalExpenses = expenseLines.Aggregate(Money.Zero(currencyCode), (sum, line) => sum + line.Balance);
        NetProfit = TotalRevenue - TotalExpenses;
    }
}

public sealed class BalanceSheetReport
{
    public DateOnly AsOfDate { get; }
    public string CurrencyCode { get; }
    public IReadOnlyList<FinancialStatementLine> AssetLines { get; }
    public IReadOnlyList<FinancialStatementLine> LiabilityLines { get; }
    public IReadOnlyList<FinancialStatementLine> EquityLines { get; }
    public Money TotalAssets { get; }
    public Money TotalLiabilities { get; }
    public Money TotalEquity { get; }
    public Money AccumulatedEarnings { get; }
    public Money TotalLiabilitiesAndEquity { get; }
    public bool IsBalanced => TotalAssets == TotalLiabilitiesAndEquity;

    internal BalanceSheetReport(DateOnly asOfDate, string currencyCode,
        IReadOnlyList<FinancialStatementLine> assetLines,
        IReadOnlyList<FinancialStatementLine> liabilityLines,
        IReadOnlyList<FinancialStatementLine> equityLines,
        Money accumulatedEarnings)
    {
        AsOfDate = asOfDate;
        CurrencyCode = currencyCode;
        AssetLines = assetLines;
        LiabilityLines = liabilityLines;
        EquityLines = equityLines;
        AccumulatedEarnings = accumulatedEarnings;
        TotalAssets = assetLines.Aggregate(Money.Zero(currencyCode), (sum, line) => sum + line.Balance);
        TotalLiabilities = liabilityLines.Aggregate(Money.Zero(currencyCode), (sum, line) => sum + line.Balance);
        TotalEquity = equityLines.Aggregate(Money.Zero(currencyCode), (sum, line) => sum + line.Balance);
        TotalLiabilitiesAndEquity = TotalLiabilities + TotalEquity + accumulatedEarnings;
    }
}

public static class FinancialStatementCalculator
{
    public static ProfitAndLossReport CalculateProfitAndLoss(
        IEnumerable<Account> accounts,
        IEnumerable<JournalEntry> journalEntries,
        DateOnly fromDate,
        DateOnly toDate,
        string currencyCode)
    {
        if (fromDate > toDate) throw new ArgumentException("Start date must not be after end date.", nameof(fromDate));
        var totals = CalculateAccountTotals(accounts, journalEntries, fromDate, toDate, currencyCode);
        var revenue = new List<FinancialStatementLine>();
        var expenses = new List<FinancialStatementLine>();

        foreach (var (account, debit, credit) in totals)
        {
            if (account.Type == AccountType.Revenue)
                revenue.Add(new(account.Code, account.Name, account.Type, new Money(checked(credit - debit), currencyCode)));
            else if (account.Type == AccountType.Expense)
                expenses.Add(new(account.Code, account.Name, account.Type, new Money(checked(debit - credit), currencyCode)));
        }

        return new ProfitAndLossReport(fromDate, toDate, new Money(0m, currencyCode).CurrencyCode,
            Array.AsReadOnly(revenue.OrderBy(x => x.AccountCode, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(expenses.OrderBy(x => x.AccountCode, StringComparer.Ordinal).ToArray()));
    }

    public static BalanceSheetReport CalculateBalanceSheet(
        IEnumerable<Account> accounts,
        IEnumerable<JournalEntry> journalEntries,
        DateOnly asOfDate,
        string currencyCode)
    {
        var totals = CalculateAccountTotals(accounts, journalEntries, DateOnly.MinValue, asOfDate, currencyCode);
        var assets = new List<FinancialStatementLine>();
        var liabilities = new List<FinancialStatementLine>();
        var equity = new List<FinancialStatementLine>();
        var revenue = Money.Zero(currencyCode);
        var expenses = Money.Zero(currencyCode);

        foreach (var (account, debit, credit) in totals)
        {
            switch (account.Type)
            {
                case AccountType.Asset:
                    assets.Add(new(account.Code, account.Name, account.Type, new Money(checked(debit - credit), currencyCode)));
                    break;
                case AccountType.Liability:
                    liabilities.Add(new(account.Code, account.Name, account.Type, new Money(checked(credit - debit), currencyCode)));
                    break;
                case AccountType.Equity:
                    equity.Add(new(account.Code, account.Name, account.Type, new Money(checked(credit - debit), currencyCode)));
                    break;
                case AccountType.Revenue:
                    revenue += new Money(checked(credit - debit), currencyCode);
                    break;
                case AccountType.Expense:
                    expenses += new Money(checked(debit - credit), currencyCode);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(accounts), "Unsupported account classification.");
            }
        }

        var accumulatedEarnings = revenue - expenses;
        return new BalanceSheetReport(asOfDate, new Money(0m, currencyCode).CurrencyCode,
            Array.AsReadOnly(assets.OrderBy(x => x.AccountCode, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(liabilities.OrderBy(x => x.AccountCode, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(equity.OrderBy(x => x.AccountCode, StringComparer.Ordinal).ToArray()),
            accumulatedEarnings);
    }

    private static IReadOnlyList<(Account Account, decimal Debits, decimal Credits)> CalculateAccountTotals(
        IEnumerable<Account> accounts,
        IEnumerable<JournalEntry> journalEntries,
        DateOnly fromDate,
        DateOnly toDate,
        string currencyCode)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(journalEntries);
        var currency = Money.Zero(currencyCode).CurrencyCode;
        var accountMap = new Dictionary<string, Account>(StringComparer.Ordinal);
        foreach (var account in accounts)
            if (!accountMap.TryAdd(account.Code, account))
                throw new InvalidOperationException($"Duplicate account code: {account.Code}.");

        var totals = accountMap.Keys.ToDictionary(code => code, _ => (Debits: 0m, Credits: 0m), StringComparer.Ordinal);
        foreach (var entry in journalEntries)
        {
            if (entry.EntryDate < fromDate || entry.EntryDate > toDate
                || !string.Equals(entry.CurrencyCode, currency, StringComparison.Ordinal))
                continue;

            foreach (var line in entry.Lines)
            {
                if (!accountMap.TryGetValue(line.AccountCode, out var account))
                    throw new InvalidOperationException($"Journal line references unknown account code {line.AccountCode}.");
                var current = totals[line.AccountCode];
                totals[line.AccountCode] = line.Side == EntrySide.Debit
                    ? (checked(current.Debits + line.Amount.Amount), current.Credits)
                    : (current.Debits, checked(current.Credits + line.Amount.Amount));
            }
        }

        return accountMap.Values
            .OrderBy(account => account.Code, StringComparer.Ordinal)
            .Select(account => (account, totals[account.Code].Debits, totals[account.Code].Credits))
            .ToArray();
    }
}
