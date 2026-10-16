namespace MG.BusinessPlatform.Domain.Finance;

public enum AgingDocumentKind
{
    Receivable = 1,
    Payable = 2
}

/// <summary>
/// Immutable snapshot of an issued invoice used for receivable/payable aging.
/// Amounts are kept in their original currency; reports must not sum across currencies.
/// </summary>
public sealed record AgingDocument
{
    public Guid Id { get; }
    public AgingDocumentKind Kind { get; }
    public string DocumentNumber { get; }
    public string PartyName { get; }
    public DateOnly IssueDate { get; }
    public DateOnly DueDate { get; }
    public string CurrencyCode { get; }
    public decimal TotalAmount { get; }
    public decimal PaidAmount { get; }
    public decimal OutstandingAmount => checked(TotalAmount - PaidAmount);

    public AgingDocument(Guid id, AgingDocumentKind kind, string documentNumber, string partyName,
        DateOnly issueDate, DateOnly dueDate, string currencyCode, decimal totalAmount, decimal paidAmount)
    {
        if (id == Guid.Empty) throw new ArgumentException("Document ID is required.", nameof(id));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (string.IsNullOrWhiteSpace(documentNumber)) throw new ArgumentException("Document number is required.", nameof(documentNumber));
        if (string.IsNullOrWhiteSpace(partyName)) throw new ArgumentException("Party name is required.", nameof(partyName));
        if (dueDate < issueDate) throw new ArgumentException("Due date cannot precede issue date.", nameof(dueDate));
        if (totalAmount < 0m) throw new ArgumentOutOfRangeException(nameof(totalAmount));
        if (paidAmount < 0m) throw new ArgumentOutOfRangeException(nameof(paidAmount));
        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Trim().Length != 3
            || currencyCode.Trim().Any(c => !char.IsAsciiLetter(c)))
            throw new ArgumentException("Currency must be a three-letter ISO-style code.", nameof(currencyCode));

        Id = id;
        Kind = kind;
        DocumentNumber = documentNumber.Trim();
        PartyName = partyName.Trim();
        IssueDate = issueDate;
        DueDate = dueDate;
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        TotalAmount = totalAmount;
        PaidAmount = paidAmount;
    }
}

public sealed record AgingReportLine(
    Guid DocumentId,
    AgingDocumentKind Kind,
    string DocumentNumber,
    string PartyName,
    DateOnly IssueDate,
    DateOnly DueDate,
    int DaysOverdue,
    string CurrencyCode,
    decimal OutstandingAmount,
    decimal NotDue,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal Over90Days);

public static class ReceivablesPayablesAgingCalculator
{
    public static IReadOnlyList<AgingReportLine> Calculate(IEnumerable<AgingDocument> documents, DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var result = new List<AgingReportLine>();

        foreach (var document in documents)
        {
            ArgumentNullException.ThrowIfNull(document);
            var outstanding = document.OutstandingAmount;
            if (outstanding <= 0m)
                continue;

            var daysOverdue = Math.Max(0, asOfDate.DayNumber - document.DueDate.DayNumber);
            var notDue = daysOverdue == 0 ? outstanding : 0m;
            var days1To30 = daysOverdue is >= 1 and <= 30 ? outstanding : 0m;
            var days31To60 = daysOverdue is >= 31 and <= 60 ? outstanding : 0m;
            var days61To90 = daysOverdue is >= 61 and <= 90 ? outstanding : 0m;
            var over90Days = daysOverdue > 90 ? outstanding : 0m;

            result.Add(new AgingReportLine(
                document.Id, document.Kind, document.DocumentNumber, document.PartyName,
                document.IssueDate, document.DueDate, daysOverdue, document.CurrencyCode,
                outstanding, notDue, days1To30, days31To60, days61To90, over90Days));
        }

        return Array.AsReadOnly(result
            .OrderBy(x => x.CurrencyCode, StringComparer.Ordinal)
            .ThenBy(x => x.Kind)
            .ThenByDescending(x => x.DaysOverdue)
            .ThenBy(x => x.DueDate)
            .ThenBy(x => x.DocumentNumber, StringComparer.Ordinal)
            .ToArray());
    }
}
