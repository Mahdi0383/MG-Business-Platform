using MG.BusinessPlatform.Domain.Finance;

namespace MG.BusinessPlatform.Domain.Sales;

public enum InvoiceStatus
{
    Draft = 1,
    Issued = 2,
    PartiallyPaid = 3,
    Paid = 4,
    Voided = 5
}

public sealed record SalesInvoiceLine
{
    public Guid Id { get; }
    public Guid ItemId { get; }
    public string Description { get; }
    public decimal Quantity { get; }
    public string UnitCode { get; }
    public Money UnitPrice { get; }
    public decimal DiscountRate { get; }
    public Money GrossAmount => UnitPrice.Multiply(Quantity);
    public Money DiscountAmount => GrossAmount.Multiply(DiscountRate);
    public Money NetAmount => GrossAmount - DiscountAmount;

    /// <param name="discountRate">Fraction between 0 and 1; tax and currency rounding policies are applied separately.</param>
    public SalesInvoiceLine(Guid id, Guid itemId, string description, decimal quantity,
        string unitCode, Money unitPrice, decimal discountRate = 0m)
    {
        if (id == Guid.Empty) throw new ArgumentException("Invoice line ID is required.", nameof(id));
        if (itemId == Guid.Empty) throw new ArgumentException("Item ID is required.", nameof(itemId));
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Line description is required.", nameof(description));
        if (quantity <= 0m) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        if (string.IsNullOrWhiteSpace(unitCode)) throw new ArgumentException("Unit code is required.", nameof(unitCode));
        if (unitPrice.Amount < 0m) throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");
        if (discountRate is < 0m or > 1m) throw new ArgumentOutOfRangeException(nameof(discountRate), "Discount rate must be between zero and one.");

        Id = id;
        ItemId = itemId;
        Description = description.Trim();
        Quantity = quantity;
        UnitCode = unitCode.Trim().ToUpperInvariant();
        UnitPrice = unitPrice;
        DiscountRate = discountRate;
    }
}

/// <summary>
/// Immutable invoice snapshot. Issuance, payment allocation, stock deduction and journal
/// posting must be coordinated by an application transaction and must not be inferred here.
/// </summary>
public sealed class SalesInvoice
{
    private readonly IReadOnlyList<SalesInvoiceLine> _lines;

    public Guid Id { get; }
    public string InvoiceNumber { get; }
    public Guid BranchId { get; }
    public Guid? CustomerId { get; }
    public DateOnly IssueDate { get; }
    public string CurrencyCode { get; }
    public InvoiceStatus Status { get; }
    public IReadOnlyList<SalesInvoiceLine> Lines => _lines;
    public Money Total { get; }

    public SalesInvoice(Guid id, string invoiceNumber, Guid branchId, Guid? customerId,
        DateOnly issueDate, InvoiceStatus status, IEnumerable<SalesInvoiceLine> lines)
    {
        if (id == Guid.Empty) throw new ArgumentException("Invoice ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(invoiceNumber)) throw new ArgumentException("Invoice number is required.", nameof(invoiceNumber));
        if (branchId == Guid.Empty) throw new ArgumentException("Branch ID is required.", nameof(branchId));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer ID must be null or non-empty.", nameof(customerId));
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        ArgumentNullException.ThrowIfNull(lines);

        var items = lines.ToArray();
        if (items.Length == 0) throw new ArgumentException("An invoice must contain at least one line.", nameof(lines));
        if (items.Any(line => line is null)) throw new ArgumentException("Invoice lines cannot contain null values.", nameof(lines));

        var currency = items[0].UnitPrice.CurrencyCode;
        if (items.Any(line => !string.Equals(line.UnitPrice.CurrencyCode, currency, StringComparison.Ordinal)))
            throw new InvalidOperationException("All invoice lines must use the same currency.");

        var total = items.Aggregate(0m, (sum, line) => checked(sum + line.NetAmount.Amount));
        Id = id;
        InvoiceNumber = invoiceNumber.Trim();
        BranchId = branchId;
        CustomerId = customerId;
        IssueDate = issueDate;
        Status = status;
        CurrencyCode = currency;
        Total = new Money(total, currency);
        _lines = Array.AsReadOnly(items);
    }
}
