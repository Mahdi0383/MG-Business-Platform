using MG.BusinessPlatform.Domain.Finance;

namespace MG.BusinessPlatform.Domain.Tests.Finance;

public sealed class AccountTests
{
    [Theory]
    [InlineData(AccountType.Asset, EntrySide.Debit)]
    [InlineData(AccountType.Expense, EntrySide.Debit)]
    [InlineData(AccountType.Liability, EntrySide.Credit)]
    [InlineData(AccountType.Equity, EntrySide.Credit)]
    [InlineData(AccountType.Revenue, EntrySide.Credit)]
    public void NormalBalance_MatchesAccountClassification(AccountType type, EntrySide expected)
    {
        var account = new Account("1000", "Sample", type);
        Assert.Equal(expected, account.NormalBalance);
    }

    [Fact]
    public void Constructor_NormalizesWhitespace()
    {
        var account = new Account(" 1000 ", " Cash ", AccountType.Asset, " 100 ");

        Assert.Equal("1000", account.Code);
        Assert.Equal("Cash", account.Name);
        Assert.Equal("100", account.ParentCode);
    }

    [Theory]
    [InlineData("", "Cash")]
    [InlineData("1000", " ")]
    public void Constructor_RejectsMissingCodeOrName(string code, string name)
    {
        Assert.Throws<ArgumentException>(() => new Account(code, name, AccountType.Asset));
    }

    [Fact]
    public void Constructor_RejectsSelfParent()
    {
        Assert.Throws<ArgumentException>(() => new Account("1000", "Cash", AccountType.Asset, "1000"));
    }

    [Fact]
    public void Constructor_RejectsUndefinedAccountType()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Account("1000", "Cash", (AccountType)99));
    }
}
