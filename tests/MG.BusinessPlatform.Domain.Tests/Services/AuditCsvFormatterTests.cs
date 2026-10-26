using MG.BusinessPlatform.Infrastructure.Services;

namespace MG.BusinessPlatform.Domain.Tests.Services;

public sealed class AuditCsvFormatterTests
{
    [Theory]
    [InlineData("ordinary text", "\"ordinary text\"")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hello\"", "\"say \"\"hello\"\"\"")]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData(" +SUM(A1:A2)", "\"' +SUM(A1:A2)\"")]
    [InlineData("\n@SUM(A1:A2)", "\"'\n@SUM(A1:A2)\"")]
    [InlineData("-1+2", "\"'-1+2\"")]
    [InlineData(null, "\"\"")]
    public void EscapeField_QuotesValuesAndNeutralizesSpreadsheetFormulas(string? input, string expected)
    {
        Assert.Equal(expected, AuditCsvFormatter.EscapeField(input));
    }
}
