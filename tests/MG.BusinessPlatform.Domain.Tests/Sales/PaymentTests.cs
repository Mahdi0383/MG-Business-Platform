using MG.BusinessPlatform.Domain.Finance;
using MG.BusinessPlatform.Domain.Sales;

namespace MG.BusinessPlatform.Domain.Tests.Sales;

public sealed class PaymentTests
{
    [Fact]
    public void Allocation_AcceptsCompatibleIncomingPayment()
    {
        var branch = Guid.NewGuid();
        var invoice = MakeInvoice(branch, 100m);
        var payment = new Payment(Guid.NewGuid(), branch, DateTimeOffset.UtcNow, PaymentDirection.Incoming,
            PaymentMethod.Card, new Money(80m, "IRR"));
        var allocation = new PaymentAllocation(Guid.NewGuid(), payment.Id, invoice.Id, new Money(80m, "IRR"));

        allocation.EnsureCompatible(payment, invoice);
    }

    [Fact]
    public void Allocation_RejectsOutgoingPayment()
    {
        var branch = Guid.NewGuid();
        var invoice = MakeInvoice(branch, 100m);
        var payment = new Payment(Guid.NewGuid(), branch, DateTimeOffset.UtcNow, PaymentDirection.Outgoing,
            PaymentMethod.Cash, new Money(20m, "IRR"));
        var allocation = new PaymentAllocation(Guid.NewGuid(), payment.Id, invoice.Id, new Money(20m, "IRR"));

        Assert.Throws<InvalidOperationException>(() => allocation.EnsureCompatible(payment, invoice));
    }

    [Fact]
    public void Allocation_RejectsCrossBranchPayment()
    {
        var invoice = MakeInvoice(Guid.NewGuid(), 100m);
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, PaymentDirection.Incoming,
            PaymentMethod.Cash, new Money(20m, "IRR"));
        var allocation = new PaymentAllocation(Guid.NewGuid(), payment.Id, invoice.Id, new Money(20m, "IRR"));

        Assert.Throws<InvalidOperationException>(() => allocation.EnsureCompatible(payment, invoice));
    }

    [Fact]
    public void Allocation_RejectsCurrencyMismatch()
    {
        var branch = Guid.NewGuid();
        var invoice = MakeInvoice(branch, 100m);
        var payment = new Payment(Guid.NewGuid(), branch, DateTimeOffset.UtcNow, PaymentDirection.Incoming,
            PaymentMethod.Cash, new Money(20m, "USD"));
        var allocation = new PaymentAllocation(Guid.NewGuid(), payment.Id, invoice.Id, new Money(20m, "USD"));

        Assert.Throws<InvalidOperationException>(() => allocation.EnsureCompatible(payment, invoice));
    }

    [Fact]
    public void Allocation_RejectsAmountGreaterThanPayment()
    {
        var branch = Guid.NewGuid();
        var invoice = MakeInvoice(branch, 100m);
        var payment = new Payment(Guid.NewGuid(), branch, DateTimeOffset.UtcNow, PaymentDirection.Incoming,
            PaymentMethod.Cash, new Money(20m, "IRR"));
        var allocation = new PaymentAllocation(Guid.NewGuid(), payment.Id, invoice.Id, new Money(21m, "IRR"));

        Assert.Throws<InvalidOperationException>(() => allocation.EnsureCompatible(payment, invoice));
    }

    private static SalesInvoice MakeInvoice(Guid branch, decimal total) =>
        new(Guid.NewGuid(), "INV-10", branch, null, new DateOnly(2026, 10, 13), InvoiceStatus.Issued,
            [new SalesInvoiceLine(Guid.NewGuid(), Guid.NewGuid(), "Item", 1m, "EA", new Money(total, "IRR"))]);
}
