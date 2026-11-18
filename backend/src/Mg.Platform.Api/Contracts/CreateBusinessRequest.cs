namespace Mg.Platform.Api.Contracts;

public sealed record CreateBusinessRequest(string Name, string Slug);
public sealed record BusinessSummary(
    Guid Id,
    string Name,
    string Slug,
    string Status,
    DateTimeOffset CreatedAtUtc);
