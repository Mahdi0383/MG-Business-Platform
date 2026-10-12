using MG.BusinessPlatform.Domain.Finance;

namespace MG.BusinessPlatform.Domain.Sales;

public enum PaymentMethod
{
    Cash = 1,
    Card = 2,
    BankTransfer = 3,
    Online = 4,
    Credit = 5,
    Other = 6
}

public enum PaymentDirection
{
    Incoming = 1,
    Outgoing = 2
}

/// <summary>A recorded money movement. Reconciliation and ledger posting happen transactionally in the application layer.</summary>
public sealed record Payment
{
    public Guid Id { get; }
    public Guid BranchId { get; }
    public DateTimeOffset OccurredAt { get; }
    public PaymentDirection Direction { get; }
    public PaymentMethod Method { get; }
    public Money Amount { get; }
    public string? Reference { get; }
    public string? Note { get; }

    public Payment(Guid id, Guid branchId, DateTimeOffset occurredAt, PaymentDirection direction,
        PaymentMethod method, Money amount, string? reference = null, string? note = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Payment ID is required.", nameof(id));
        if (branchId == Guid.Empty) throw new ArgumentException("Branch ID is required.", nameof(branchId));
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (!Enum.IsDefined(method)) throw new ArgumentOutOfRangeException(nameof(method));
        if (amount.Amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be positive.");

        Id = id;
        BranchId = branchId;
        OccurredAt = occurredAt;
        Direction = direction;
        Method = method;
        Amount = amount;
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}

/// <summary>Allocates part of a payment to an invoice; total-allocation limits require transactional validation.</summary>
public sealed record PaymentAllocation
{
    public Guid Id { get; }
    public Guid PaymentId { get; }
    public Guid InvoiceId { get; }
    public Money Amount { get; }

    public PaymentAllocation(Guid id, Guid paymentId, Guid invoiceId, Money amount)
    {
        if (id == Guid.Empty) throw new ArgumentException("Allocation ID is required.", nameof(id));
        if (paymentId == Guid.Empty) throw new ArgumentException("Payment ID is required.", nameof(paymentId));
        if (invoiceId == Guid.Empty) throw new ArgumentException("Invoice ID is required.", nameof(invoiceId));
        if (amount.Amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount), "Allocation amount must be positive.");

        Id = id;
        PaymentId = paymentId;
        InvoiceId = invoiceId;
        Amount = amount;
    }

    public void EnsureCompatible(Payment payment, SalesInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(invoice);
        if (payment.Id != PaymentId || invoice.Id != InvoiceId)
            throw new InvalidOperationException("Allocation identifiers do not match the supplied payment and invoice.");
        if (payment.Direction != PaymentDirection.Incoming)
            throw new InvalidOperationException("An outgoing payment cannot settle a sales invoice.");
        if (payment.BranchId != invoice.BranchId)
            throw new InvalidOperationException("Payment and invoice must belong to the same branch.");
        if (payment.Amount.CurrencyCode != Amount.CurrencyCode || invoice.CurrencyCode != Amount.CurrencyCode)
            throw new InvalidOperationException("Payment, allocation, and invoice currencies must match.");
        if (Amount.Amount > payment.Amount.Amount)
            throw new InvalidOperationException("Allocation exceeds the payment amount.");
        if (Amount.Amount > invoice.Total.Amount)
            throw new InvalidOperationException("Allocation exceeds the invoice total.");
    }
}
