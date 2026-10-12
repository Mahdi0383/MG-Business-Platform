using MG.BusinessPlatform.Domain.Finance;

namespace MG.BusinessPlatform.Domain.Tests.Finance;

public sealed class MoneyTests
{
    [Fact]
    public void Constructor_NormalizesCurrencyCode()
    {
        var money = new Money(12.50m, " usd ");

        Assert.Equal(12.50m, money.Amount);
        Assert.Equal("USD", money.CurrencyCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("12A")]
    public void Constructor_RejectsInvalidCurrencyCode(string currencyCode)
    {
        Assert.Throws<ArgumentException>(() => new Money(1m, currencyCode));
    }

    [Fact]
    public void Addition_PreservesExactDecimalAmount()
    {
        var result = new Money(0.1m, "USD") + new Money(0.2m, "USD");

        Assert.Equal(0.3m, result.Amount);
        Assert.Equal("USD", result.CurrencyCode);
    }

    [Fact]
    public void Addition_RejectsDifferentCurrencies()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _ = new Money(10m, "USD") + new Money(10m, "EUR"));
    }

    [Fact]
    public void Subtraction_RejectsDifferentCurrencies()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _ = new Money(10m, "USD") - new Money(1m, "EUR"));
    }

    [Fact]
    public void Multiply_UsesDecimalArithmetic()
    {
        var result = new Money(19.99m, "USD").Multiply(3m);

        Assert.Equal(59.97m, result.Amount);
    }

    [Fact]
    public void Zero_UsesRequestedCurrency()
    {
        Assert.Equal(new Money(0m, "IRR"), Money.Zero("irr"));
    }

    [Fact]
    public void Addition_ThrowsOnDecimalOverflow()
    {
        Assert.Throws<OverflowException>(() =>
            _ = new Money(decimal.MaxValue, "USD") + new Money(1m, "USD"));
    }
}
