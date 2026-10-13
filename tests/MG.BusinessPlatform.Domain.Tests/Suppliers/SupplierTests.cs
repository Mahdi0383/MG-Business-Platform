using MG.BusinessPlatform.Domain.Suppliers;

namespace MG.BusinessPlatform.Domain.Tests.Suppliers;

public sealed class SupplierTests
{
    [Fact]
    public void Constructor_NormalizesEmailAndTerms()
    {
        var supplier = new Supplier(Guid.NewGuid(), " Supplier ", email: " SALES@EXAMPLE.COM ", paymentTerms: " Net 30 ");
        Assert.Equal("Supplier", supplier.DisplayName);
        Assert.Equal("sales@example.com", supplier.Email);
        Assert.Equal("Net 30", supplier.PaymentTerms);
    }

    [Fact]
    public void Constructor_RejectsEmptyIdentifier()
    {
        Assert.Throws<ArgumentException>(() => new Supplier(Guid.Empty, "Supplier"));
    }
}
