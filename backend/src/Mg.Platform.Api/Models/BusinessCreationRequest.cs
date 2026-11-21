namespace Mg.Platform.Api.Models;

/// <summary>
/// Durable idempotency binding for one authenticated creator's business registration request.
/// Stores only one-way hashes of creator identity and normalized request data.
/// </summary>
public sealed class BusinessCreationRequest
{
    public Guid IdempotencyKey { get; set; }
    public required string CreatorIdentityHash { get; set; }
    public required string RequestFingerprint { get; set; }
    public Guid BusinessId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
