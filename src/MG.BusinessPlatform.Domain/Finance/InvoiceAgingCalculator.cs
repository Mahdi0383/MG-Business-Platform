namespace MG.BusinessPlatform.Domain.Finance;

public enum InvoiceAgeBucket
{
    FutureDated = 0,
    Current0To30Days = 1,
    Days31To60 = 2,
    Days61To90 = 3,
    Over90Days = 4
}

public sealed record AgingDocument(
    string DocumentType,
    string DocumentNumber,
    string CounterpartyName,
    DateOnly IssueDate,
    decimal TotalAmount,
    decimal PaidAmount,
    string CurrencyCode,
    string Status,
    DateOnly? DueDate = null);

public sealed record InvoiceAgingLine(
    string DocumentType,
    string DocumentNumber,
    string CounterpartyName,
    DateOnly IssueDate,
    DateOnly? DueDate,
    DateOnly AgingDate,
    bool UsesIssueDateFallback,
    int AgeDays,
    InvoiceAgeBucket Bucket,
    Money TotalAmount,
    Money PaidAmount,
    Money OutstandingAmount);

/// <summary>
/// Open receivable/payable invoice aging using recorded due dates when available.
/// Legacy invoices without a due date fall back to issue-date aging and are flagged in the report.
/// </summary>
public sealed class InvoiceAgingReport
{
    public DateOnly AsOfDate { get; }
    public string CurrencyCode { get; }
    public IReadOnlyList<InvoiceAgingLine> Lines { get; }
    public Money FutureDated { get; }
    public Money Current0To30Days { get; }
    public Money Days31To60 { get; }
    public Money Days61To90 { get; }
    public Money Over90Days { get; }
    public Money TotalOutstanding { get; }

    internal InvoiceAgingReport(DateOnly asOfDate, string currencyCode, IReadOnlyList<InvoiceAgingLine> lines)
    {
        AsOfDate = asOfDate;
        CurrencyCode = Money.Zero(currencyCode).CurrencyCode;
        Lines = lines;
        FutureDated = Sum(lines, InvoiceAgeBucket.FutureDated);
        Current0To30Days = Sum(lines, InvoiceAgeBucket.Current0To30Days);
        Days31To60 = Sum(lines, InvoiceAgeBucket.Days31To60);
        Days61To90 = Sum(lines, InvoiceAgeBucket.Days61To90);
        Over90Days = Sum(lines, InvoiceAgeBucket.Over90Days);
        TotalOutstanding = FutureDated + Current0To30Days + Days31To60 + Days61To90 + Over90Days;
    }

    private Money Sum(IEnumerable<InvoiceAgingLine> lines, InvoiceAgeBucket bucket) =>
        lines.Where(line => line.Bucket == bucket)
            .Aggregate(Money.Zero(CurrencyCode), (sum, line) => sum + line.OutstandingAmount);
}

public static class InvoiceAgingCalculator
{
    public static InvoiceAgingReport Calculate(
        IEnumerable<AgingDocument> documents,
        DateOnly asOfDate,
        string currencyCode)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var currency = Money.Zero(currencyCode).CurrencyCode;
        var lines = new List<InvoiceAgingLine>();

        foreach (var document in documents)
        {
            if (string.IsNullOrWhiteSpace(document.DocumentType) || document.DocumentType.Trim().Length > 40)
                throw new ArgumentException("Document type is required and must not exceed 40 characters.", nameof(documents));
            if (string.IsNullOrWhiteSpace(document.DocumentNumber) || document.DocumentNumber.Trim().Length > 100)
                throw new ArgumentException("Document number is required and must not exceed 100 characters.", nameof(documents));
            if (string.IsNullOrWhiteSpace(document.CounterpartyName) || document.CounterpartyName.Trim().Length > 200)
                throw new ArgumentException("Counterparty name is required and must not exceed 200 characters.", nameof(documents));
            var documentCurrency = Money.Zero(document.CurrencyCode).CurrencyCode;
            if (!string.Equals(documentCurrency, currency, StringComparison.Ordinal))
                throw new InvalidOperationException($"Document {document.DocumentNumber} uses {documentCurrency}, not report currency {currency}.");
            if (string.Equals(document.Status, "Voided", StringComparison.OrdinalIgnoreCase))
                continue;
            if (document.TotalAmount < 0m || document.PaidAmount < 0m || document.PaidAmount > document.TotalAmount)
                throw new InvalidOperationException($"Document {document.DocumentNumber} has invalid total or paid amounts.");

            var outstanding = checked(document.TotalAmount - document.PaidAmount);
            if (outstanding == 0m)
                continue;

            var agingDate = document.DueDate ?? document.IssueDate;
            var difference = asOfDate.DayNumber - agingDate.DayNumber;
            var ageDays = Math.Max(0, difference);
            var bucket = difference < 0
                ? InvoiceAgeBucket.FutureDated
                : ageDays <= 30 ? InvoiceAgeBucket.Current0To30Days
                : ageDays <= 60 ? InvoiceAgeBucket.Days31To60
                : ageDays <= 90 ? InvoiceAgeBucket.Days61To90
                : InvoiceAgeBucket.Over90Days;

            lines.Add(new InvoiceAgingLine(document.DocumentType.Trim(), document.DocumentNumber.Trim(),
                document.CounterpartyName.Trim(), document.IssueDate, document.DueDate, agingDate,
                document.DueDate is null, ageDays, bucket,
                new Money(document.TotalAmount, currency), new Money(document.PaidAmount, currency),
                new Money(outstanding, currency)));
        }

        var ordered = lines.OrderByDescending(line => line.Bucket)
            .ThenByDescending(line => line.AgeDays)
            .ThenBy(line => line.IssueDate)
            .ThenBy(line => line.DocumentNumber, StringComparer.Ordinal)
            .ToArray();
        return new InvoiceAgingReport(asOfDate, currency, Array.AsReadOnly(ordered));
    }
}
