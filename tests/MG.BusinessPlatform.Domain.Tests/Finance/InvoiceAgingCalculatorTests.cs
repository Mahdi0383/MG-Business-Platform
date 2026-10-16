using MG.BusinessPlatform.Domain.Finance;

namespace MG.BusinessPlatform.Domain.Tests.Finance;

public sealed class InvoiceAgingCalculatorTests
{
    private static readonly DateOnly AsOf = new(2026, 10, 16);

    [Fact]
    public void Calculate_UsesInclusiveAgeBucketBoundaries()
    {
        var documents = new[]
        {
            Doc("INV-0", AsOf),
            Doc("INV-30", AsOf.AddDays(-30)),
            Doc("INV-31", AsOf.AddDays(-31)),
            Doc("INV-60", AsOf.AddDays(-60)),
            Doc("INV-61", AsOf.AddDays(-61)),
            Doc("INV-90", AsOf.AddDays(-90)),
            Doc("INV-91", AsOf.AddDays(-91)),
            Doc("INV-FUTURE", AsOf.AddDays(2))
        };

        var report = InvoiceAgingCalculator.Calculate(documents, AsOf, "IRR");

        Assert.Equal(8, report.Lines.Count);
        Assert.Equal(2m, report.Current0To30Days.Amount);
        Assert.Equal(2m, report.Days31To60.Amount);
        Assert.Equal(2m, report.Days61To90.Amount);
        Assert.Equal(1m, report.Over90Days.Amount);
        Assert.Equal(1m, report.FutureDated.Amount);
        Assert.Equal(8m, report.TotalOutstanding.Amount);
        Assert.Equal(InvoiceAgeBucket.Current0To30Days, Assert.Single(report.Lines, line => line.DocumentNumber == "INV-30").Bucket);
        Assert.Equal(InvoiceAgeBucket.Days31To60, Assert.Single(report.Lines, line => line.DocumentNumber == "INV-31").Bucket);
        Assert.Equal(InvoiceAgeBucket.Days61To90, Assert.Single(report.Lines, line => line.DocumentNumber == "INV-61").Bucket);
        Assert.Equal(InvoiceAgeBucket.Over90Days, Assert.Single(report.Lines, line => line.DocumentNumber == "INV-91").Bucket);
        Assert.Equal(InvoiceAgeBucket.FutureDated, Assert.Single(report.Lines, line => line.DocumentNumber == "INV-FUTURE").Bucket);
    }

    [Fact]
    public void Calculate_UsesRecordedDueDateAndFlagsIssueDateFallback()
    {
        var documents = new[]
        {
            Doc("WITH-DUE", AsOf.AddDays(-90), dueDate: AsOf.AddDays(-10)),
            Doc("LEGACY", AsOf.AddDays(-45))
        };

        var report = InvoiceAgingCalculator.Calculate(documents, AsOf, "IRR");

        var dueDateLine = Assert.Single(report.Lines, line => line.DocumentNumber == "WITH-DUE");
        Assert.Equal(AsOf.AddDays(-10), dueDateLine.AgingDate);
        Assert.Equal(10, dueDateLine.AgeDays);
        Assert.Equal(InvoiceAgeBucket.Current0To30Days, dueDateLine.Bucket);
        Assert.False(dueDateLine.UsesIssueDateFallback);

        var legacyLine = Assert.Single(report.Lines, line => line.DocumentNumber == "LEGACY");
        Assert.Equal(legacyLine.IssueDate, legacyLine.AgingDate);
        Assert.True(legacyLine.UsesIssueDateFallback);
        Assert.Equal(InvoiceAgeBucket.Days31To60, legacyLine.Bucket);
    }

    [Fact]
    public void Calculate_ExcludesPaidAndVoidedDocumentsAndUsesOutstandingBalance()
    {
        var documents = new[]
        {
            Doc("PARTIAL", AsOf.AddDays(-20), total: 100m, paid: 35m),
            Doc("PAID", AsOf.AddDays(-50), total: 100m, paid: 100m),
            Doc("VOID", AsOf.AddDays(-100), total: 500m, paid: 0m, status: "Voided")
        };

        var report = InvoiceAgingCalculator.Calculate(documents, AsOf, "IRR");

        var line = Assert.Single(report.Lines);
        Assert.Equal("PARTIAL", line.DocumentNumber);
        Assert.Equal(65m, line.OutstandingAmount.Amount);
        Assert.Equal(65m, report.TotalOutstanding.Amount);
    }

    [Fact]
    public void Calculate_RejectsMixedCurrenciesAndInvalidPaymentAmounts()
    {
        Assert.Throws<InvalidOperationException>(() =>
            InvoiceAgingCalculator.Calculate([Doc("USD", AsOf, currency: "USD")], AsOf, "IRR"));
        Assert.Throws<InvalidOperationException>(() =>
            InvoiceAgingCalculator.Calculate([Doc("OVERPAID", AsOf, total: 10m, paid: 11m)], AsOf, "IRR"));
        Assert.Throws<InvalidOperationException>(() =>
            InvoiceAgingCalculator.Calculate([Doc("NEGATIVE", AsOf, total: -1m)], AsOf, "IRR"));
    }

    [Fact]
    public void Calculate_RejectsInvalidDocumentIdentity()
    {
        var invalid = Doc(" ", AsOf);
        Assert.Throws<ArgumentException>(() => InvoiceAgingCalculator.Calculate([invalid], AsOf, "IRR"));
    }

    private static AgingDocument Doc(
        string number,
        DateOnly issueDate,
        decimal total = 1m,
        decimal paid = 0m,
        string status = "Issued",
        string currency = "IRR",
        DateOnly? dueDate = null) =>
        new("Receivable", number, "Test customer", issueDate, total, paid, currency, status, dueDate);
}
