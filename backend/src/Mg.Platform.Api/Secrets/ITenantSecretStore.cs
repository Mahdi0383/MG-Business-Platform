namespace Mg.Platform.Api.Secrets;

/// <summary>
/// Stores tenant connection secrets outside the MG central database.
/// Implementations must never log secret values or authentication tokens.
/// </summary>
public interface ITenantSecretStore
{
    Task StoreAsync(string secretReference, string secretValue, CancellationToken cancellationToken);
    Task<string?> ReadAsync(string secretReference, CancellationToken cancellationToken);
    Task DeleteAsync(string secretReference, CancellationToken cancellationToken);
}
