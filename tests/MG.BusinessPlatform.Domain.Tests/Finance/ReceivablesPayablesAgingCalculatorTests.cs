using MG.BusinessPlatform.Domain.Finance;

namespace MG.BusinessPlatform.Domain.Tests.Finance;

public sealed class ReceivablesPayablesAgingCalculatorTests
{
    private static readonly DateOnly AsOf = new(2026, 10, 16);

    [Fact]
    public void Calculate_AssignsOutstandingAmountsToExclusiveAgingBuckets()
    {
        var documents = new[]
        {
            Document("CURRENT", new DateOnly(2026, 10, 16), 100m, 20m),
            Document("DUE-30", new DateOnly(2026, 9, 16), 200m, 0m),
            Document("DUE-31", new DateOnly(2026, 9, 15), 300m, 0m),
            Document("DUE-60", new DateOnly(2026, 8, 17), 400m, 0m),
            Document("DUE-61", new DateOnly(2026, 8, 16), 500m, 0m),
            Document("DUE-90", new DateOnly(2026, 7, 18), 600m, 0m),
            Document("DUE-91", new DateOnly(2026, 7, 17), 700m, 0m)
        };

        var report = ReceivablesPayablesAgingCalculator.Calculate(documents, AsOf);

        Assert.Equal(7, report.Count);
        Assert.Equal(80m, report.Single(x => x.DocumentNumber == "CURRENT").NotDue);
        Assert.Equal(200m, report.Single(x => x.DocumentNumber == "DUE-30").Days1To30);
        Assert.Equal(300m, report.Single(x => x.DocumentNumber == "DUE-31").Days31To60);
        Assert.Equal(400m, report.Single(x => x.DocumentNumber == "DUE-60").Days31To60);
        Assert.Equal(500m, report.Single(x => x.DocumentNumber == "DUE-61").Days61To90);
        Assert.Equal(600m, report.Single(x => x.DocumentNumber == "DUE-90").Days61To90);
        Assert.Equal(700m, report.Single(x => x.DocumentNumber == "DUE-91").Over90Days);

        foreach (var line in report)
        {
            Assert.Equal(line.OutstandingAmount, line.NotDue + line.Days1To30 + line.Days31To60
                + line.Days61To90 + line.Over90Days);
        }
    }

    [Fact]
    public void Calculate_ExcludesPaidAndOverpaidDocumentsAndPreservesCurrency()
    {
        var documents = new[]
        {
            Document("PAID", new DateOnly(2026, 1, 1), 100m, 100m),
            Document("OVERPAID", new DateOnly(2026, 1, 1), 100m, 120m),
            Document("USD", new DateOnly(2026, 10, 1), 200m, 50m, "USD")
        };

        var report = ReceivablesPayablesAgingCalculator.Calculate(documents, AsOf);

        var line = Assert.Single(report);
        Assert.Equal("USD", line.CurrencyCode);
        Assert.Equal(150m, line.OutstandingAmount);
        Assert.Equal(150m, line.NotDue);
    }

    [Fact]
    public void Calculate_RejectsNullInput()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ReceivablesPayablesAgingCalculator.Calculate(null!, AsOf));
    }

    private static AgingDocument Document(string number, DateOnly dueDate, decimal total, decimal paid,
        string currency = "IRR") =>
        new(Guid.NewGuid(), AgingDocumentKind.Receivable, number, "Test customer",
            dueDate.AddDays(-30), dueDate, currency, total, paid);
}
