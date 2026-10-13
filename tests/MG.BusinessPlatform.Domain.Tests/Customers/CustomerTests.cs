using MG.BusinessPlatform.Domain.Customers;

namespace MG.BusinessPlatform.Domain.Tests.Customers;

public sealed class CustomerTests
{
    [Fact]
    public void Constructor_NormalizesOptionalFields()
    {
        var customer = new Customer(Guid.NewGuid(), "  Ada Lovelace ", " 123 ", " ADA@EXAMPLE.COM ", " ", null);
        Assert.Equal("Ada Lovelace", customer.DisplayName);
        Assert.Equal("123", customer.Phone);
        Assert.Equal("ada@example.com", customer.Email);
        Assert.Null(customer.Address);
        Assert.Null(customer.TaxIdentifier);
    }

    [Fact]
    public void Constructor_RejectsEmptyIdentifier()
    {
        Assert.Throws<ArgumentException>(() => new Customer(Guid.Empty, "Customer"));
    }

    [Fact]
    public void Constructor_RejectsOversizedContactFields()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Customer(Guid.NewGuid(), "Customer", phone: new string('1', 41)));
    }
}
