namespace MG.BusinessPlatform.Domain.Finance;

/// <summary>
/// Represents a monetary amount in one explicit currency.
/// This type deliberately does not choose tax, exchange-rate, or rounding policies;
/// those belong to explicit business rules at the point where they are required.
/// </summary>
public readonly record struct Money
{
    public decimal Amount { get; }
    public string CurrencyCode { get; }

    public Money(decimal amount, string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
            throw new ArgumentException("A currency code is required.", nameof(currencyCode));

        var normalizedCode = currencyCode.Trim().ToUpperInvariant();
        if (normalizedCode.Length != 3 || normalizedCode.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Currency code must be a three-letter ISO-style code.", nameof(currencyCode));

        Amount = amount;
        CurrencyCode = normalizedCode;
    }

    public static Money Zero(string currencyCode) => new(0m, currencyCode);

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(checked(left.Amount + right.Amount), left.CurrencyCode);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(checked(left.Amount - right.Amount), left.CurrencyCode);
    }

    public Money Multiply(decimal factor) =>
        new(checked(Amount * factor), CurrencyCode);

    public bool IsNegative => Amount < 0m;
    public bool IsZero => Amount == 0m;

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (!string.Equals(left.CurrencyCode, right.CurrencyCode, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Cannot combine {left.CurrencyCode} and {right.CurrencyCode} amounts without an explicit currency conversion.");
    }
}
