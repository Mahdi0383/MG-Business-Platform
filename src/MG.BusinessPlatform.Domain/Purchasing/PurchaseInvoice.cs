using MG.BusinessPlatform.Domain.Finance;

namespace MG.BusinessPlatform.Domain.Purchasing;

public enum PurchaseInvoiceStatus
{
    Draft = 1,
    Received = 2,
    PartiallyPaid = 3,
    Paid = 4,
    Voided = 5
}

public sealed record PurchaseInvoiceLine
{
    public Guid Id { get; }
    public Guid ItemId { get; }
    public string Description { get; }
    public decimal Quantity { get; }
    public string UnitCode { get; }
    public Money UnitCost { get; }
    public decimal DiscountRate { get; }
    public Money GrossAmount => UnitCost.Multiply(Quantity);
    public Money DiscountAmount => GrossAmount.Multiply(DiscountRate);
    public Money NetAmount => GrossAmount - DiscountAmount;

    public PurchaseInvoiceLine(Guid id, Guid itemId, string description, decimal quantity,
        string unitCode, Money unitCost, decimal discountRate = 0m)
    {
        if (id == Guid.Empty) throw new ArgumentException("Purchase line ID is required.", nameof(id));
        if (itemId == Guid.Empty) throw new ArgumentException("Item ID is required.", nameof(itemId));
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Line description is required.", nameof(description));
        if (quantity <= 0m) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (string.IsNullOrWhiteSpace(unitCode)) throw new ArgumentException("Unit code is required.", nameof(unitCode));
        if (unitCost.Amount < 0m) throw new ArgumentOutOfRangeException(nameof(unitCost));
        if (discountRate is < 0m or > 1m) throw new ArgumentOutOfRangeException(nameof(discountRate));

        Id = id;
        ItemId = itemId;
        Description = description.Trim();
        Quantity = quantity;
        UnitCode = unitCode.Trim().ToUpperInvariant();
        UnitCost = unitCost;
        DiscountRate = discountRate;
    }
}

public sealed class PurchaseInvoice
{
    private readonly IReadOnlyList<PurchaseInvoiceLine> _lines;

    public Guid Id { get; }
    public string InvoiceNumber { get; }
    public Guid BranchId { get; }
    public Guid SupplierId { get; }
    public DateOnly InvoiceDate { get; }
    public PurchaseInvoiceStatus Status { get; }
    public string CurrencyCode { get; }
    public IReadOnlyList<PurchaseInvoiceLine> Lines => _lines;
    public Money Total { get; }

    public PurchaseInvoice(Guid id, string invoiceNumber, Guid branchId, Guid supplierId,
        DateOnly invoiceDate, PurchaseInvoiceStatus status, IEnumerable<PurchaseInvoiceLine> lines)
    {
        if (id == Guid.Empty) throw new ArgumentException("Purchase invoice ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(invoiceNumber)) throw new ArgumentException("Invoice number is required.", nameof(invoiceNumber));
        if (branchId == Guid.Empty) throw new ArgumentException("Branch ID is required.", nameof(branchId));
        if (supplierId == Guid.Empty) throw new ArgumentException("Supplier ID is required.", nameof(supplierId));
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        ArgumentNullException.ThrowIfNull(lines);

        var items = lines.ToArray();
        if (items.Length == 0) throw new ArgumentException("A purchase invoice must contain at least one line.", nameof(lines));
        if (items.Any(line => line is null)) throw new ArgumentException("Purchase lines cannot contain null values.", nameof(lines));
        var currency = items[0].UnitCost.CurrencyCode;
        if (items.Any(line => line.UnitCost.CurrencyCode != currency))
            throw new InvalidOperationException("All purchase lines must use the same currency.");

        var total = items.Aggregate(0m, (sum, line) => checked(sum + line.NetAmount.Amount));
        Id = id;
        InvoiceNumber = invoiceNumber.Trim();
        BranchId = branchId;
        SupplierId = supplierId;
        InvoiceDate = invoiceDate;
        Status = status;
        CurrencyCode = currency;
        Total = new Money(total, currency);
        _lines = Array.AsReadOnly(items);
    }
}
