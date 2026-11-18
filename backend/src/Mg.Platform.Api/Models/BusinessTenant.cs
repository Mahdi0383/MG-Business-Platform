namespace Mg.Platform.Api.Models;

public sealed class BusinessTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string Status { get; set; } = "Provisioning";
    public required string DatabaseSecretReference { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ActivatedAtUtc { get; set; }
}
