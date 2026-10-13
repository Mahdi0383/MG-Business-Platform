using MG.BusinessPlatform.Domain.Finance;
using MG.BusinessPlatform.Domain.Purchasing;

namespace MG.BusinessPlatform.Domain.Tests.Purchasing;

public sealed class PurchaseInvoiceTests
{
    [Fact]
    public void Invoice_CalculatesNetPurchaseCost()
    {
        var line = new PurchaseInvoiceLine(Guid.NewGuid(), Guid.NewGuid(), "Beans", 10m, "kg", new Money(120m, "IRR"), 0.05m);
        var invoice = new PurchaseInvoice(Guid.NewGuid(), "PUR-001", Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 10, 13), PurchaseInvoiceStatus.Received, [line]);

        Assert.Equal(new Money(1200m, "IRR"), line.GrossAmount);
        Assert.Equal(new Money(60m, "IRR"), line.DiscountAmount);
        Assert.Equal(new Money(1140m, "IRR"), invoice.Total);
    }

    [Fact]
    public void Invoice_RejectsMixedCurrencies()
    {
        var first = Line("IRR");
        var second = Line("USD");
        Assert.Throws<InvalidOperationException>(() => new PurchaseInvoice(Guid.NewGuid(), "PUR-2",
            Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), PurchaseInvoiceStatus.Draft, [first, second]));
    }

    [Fact]
    public void Line_RejectsInvalidDiscountAndQuantity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PurchaseInvoiceLine(Guid.NewGuid(), Guid.NewGuid(),
            "Item", 0m, "EA", new Money(1m, "IRR")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PurchaseInvoiceLine(Guid.NewGuid(), Guid.NewGuid(),
            "Item", 1m, "EA", new Money(1m, "IRR"), 1.1m));
    }

    private static PurchaseInvoiceLine Line(string currency) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Item", 1m, "EA", new Money(10m, currency));
}
