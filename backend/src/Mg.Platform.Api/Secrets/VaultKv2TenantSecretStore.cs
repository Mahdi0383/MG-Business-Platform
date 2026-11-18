using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mg.Platform.Api.Secrets;

/// <summary>
/// HashiCorp Vault KV v2 adapter. Vault is optional until a provisioner consumes
/// this service; when used, the Vault endpoint must be HTTPS.
/// </summary>
public sealed class VaultKv2TenantSecretStore(
    HttpClient httpClient,
    IConfiguration configuration) : ITenantSecretStore
{
    private readonly VaultSecretStoreOptions options = VaultSecretStoreOptions.From(configuration);

    public async Task StoreAsync(
        string secretReference, string secretValue, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretValue);
        var path = BuildDataPath(secretReference);
        using var request = CreateRequest(HttpMethod.Post, path);
        request.Content = JsonContent.Create(new
        {
            data = new Dictionary<string, string> { ["value"] = secretValue }
        });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string?> ReadAsync(
        string secretReference, CancellationToken cancellationToken)
    {
        var path = BuildDataPath(secretReference);
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var outer)
            || !outer.TryGetProperty("data", out var inner)
            || !inner.TryGetProperty("value", out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Vault response did not contain the expected KV v2 secret value.");
        }

        return value.GetString();
    }

    public async Task DeleteAsync(string secretReference, CancellationToken cancellationToken)
    {
        var path = BuildMetadataPath(secretReference);
        using var request = CreateRequest(HttpMethod.Delete, path);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return;
        response.EnsureSuccessStatusCode();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(method, new Uri(options.Address, relativePath));
        request.Headers.TryAddWithoutValidation("X-Vault-Token", options.Token);
        return request;
    }

    private string BuildDataPath(string reference) =>
        $"v1/{options.Mount}/data/{NormalizeReference(reference)}";

    private string BuildMetadataPath(string reference) =>
        $"v1/{options.Mount}/metadata/{NormalizeReference(reference)}";

    private static string NormalizeReference(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        if (reference.Length > 300 || reference.StartsWith('/') || reference.EndsWith('/')
            || reference.Contains('\\') || reference.Contains('?') || reference.Contains('#'))
        {
            throw new ArgumentException("Secret reference is invalid.", nameof(reference));
        }

        var segments = reference.Split('/');
        if (segments.Any(segment => segment.Length is < 1 or > 64
            || segment is "." or ".."
            || segment.Any(character => !(char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character) || character is '-' or '_'))))
        {
            throw new ArgumentException("Secret reference contains an invalid path segment.", nameof(reference));
        }

        return string.Join('/', segments.Select(Uri.EscapeDataString));
    }
}
