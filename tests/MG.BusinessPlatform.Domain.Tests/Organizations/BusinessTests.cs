using MG.BusinessPlatform.Domain.Organizations;

namespace MG.BusinessPlatform.Domain.Tests.Organizations;

public sealed class BusinessTests
{
    [Fact]
    public void Business_NormalizesCurrency()
    {
        var business = new Business(Guid.NewGuid(), "Legal", "Shop", " irr ", "Asia/Tehran");
        Assert.Equal("IRR", business.DefaultCurrencyCode);
    }

    [Fact]
    public void Business_RejectsInvalidCurrency()
    {
        Assert.Throws<ArgumentException>(() => new Business(Guid.NewGuid(), "Legal", "Shop", "IR1", "UTC"));
    }

    [Fact]
    public void Branch_NormalizesCodeAndOptionalFields()
    {
        var branch = new Branch(Guid.NewGuid(), Guid.NewGuid(), " main ", " Main Store ", " ", null);
        Assert.Equal("MAIN", branch.Code);
        Assert.Equal("Main Store", branch.Name);
        Assert.Null(branch.Address);
        Assert.Null(branch.Phone);
    }

    [Fact]
    public void Branch_RequiresBusinessIdentity()
    {
        Assert.Throws<ArgumentException>(() => new Branch(Guid.NewGuid(), Guid.Empty, "A", "Branch"));
    }
}
