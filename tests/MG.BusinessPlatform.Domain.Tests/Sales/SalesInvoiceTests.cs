using MG.BusinessPlatform.Domain.Finance;
using MG.BusinessPlatform.Domain.Sales;

namespace MG.BusinessPlatform.Domain.Tests.Sales;

public sealed class SalesInvoiceTests
{
    [Fact]
    public void Line_CalculatesGrossDiscountAndNetAmounts()
    {
        var line = Line(2m, 125m, 0.1m);

        Assert.Equal(new Money(250m, "IRR"), line.GrossAmount);
        Assert.Equal(new Money(25m, "IRR"), line.DiscountAmount);
        Assert.Equal(new Money(225m, "IRR"), line.NetAmount);
    }

    [Fact]
    public void Invoice_SumsNetLineAmounts()
    {
        var invoice = Invoice([Line(2m, 100m, 0.1m), Line(1m, 50m)]);

        Assert.Equal(new Money(230m, "IRR"), invoice.Total);
        Assert.Equal(2, invoice.Lines.Count);
    }

    [Fact]
    public void Invoice_RejectsMixedCurrencies()
    {
        Assert.Throws<InvalidOperationException>(() => Invoice([Line(1m, 100m), Line(1m, 100m, currency: "USD")]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Line_RejectsNonPositiveQuantity(decimal quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Line(quantity, 100m));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Line_RejectsDiscountOutsideRange(decimal discount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Line(1m, 100m, discount));
    }

    [Fact]
    public void Invoice_RequiresAtLeastOneLine()
    {
        Assert.Throws<ArgumentException>(() => Invoice([]));
    }

    private static SalesInvoiceLine Line(decimal quantity, decimal price, decimal discount = 0m, string currency = "IRR") =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Test item", quantity, "EA", new Money(price, currency), discount);

    private static SalesInvoice Invoice(IEnumerable<SalesInvoiceLine> lines) =>
        new(Guid.NewGuid(), "INV-001", Guid.NewGuid(), null, new DateOnly(2026, 10, 13), InvoiceStatus.Draft, lines);
}
