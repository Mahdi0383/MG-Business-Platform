namespace MG.BusinessPlatform.Infrastructure.Services;

/// <summary>
/// Escapes audit values for UTF-8 CSV exports and neutralizes spreadsheet formula injection.
/// </summary>
public static class AuditCsvFormatter
{
    public static string EscapeField(string? value)
    {
        var text = value ?? string.Empty;
        var firstVisibleCharacter = text.TrimStart(' ', '\t', '\r', '\n');
        if (firstVisibleCharacter.Length > 0
            && firstVisibleCharacter[0] is '=' or '+' or '-' or '@')
        {
            text = "'" + text;
        }

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
