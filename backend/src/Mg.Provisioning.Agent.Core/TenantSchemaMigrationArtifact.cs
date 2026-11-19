using System.Security.Cryptography;
using System.Text;

namespace Mg.Provisioning.Agent.Core;

/// <summary>
/// Immutable, reviewed tenant schema migration bytes with a checksum calculated from
/// the exact packaged artifact. The caller cannot supply or override the checksum.
/// </summary>
public sealed class TenantSchemaMigrationArtifact
{
    private const int MaximumArtifactBytes = 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly byte[] sqlBytes;

    private TenantSchemaMigrationArtifact(byte[] sqlBytes)
    {
        this.sqlBytes = sqlBytes;
        Sha256 = Convert.ToHexString(SHA256.HashData(sqlBytes)).ToLowerInvariant();
    }

    public int Version => 1;
    public string Name => "tenant_core_baseline";
    public string Sha256 { get; }

    /// <summary>Returns a defensive copy so provisioner code cannot mutate the reviewed bytes.</summary>
    public ReadOnlyMemory<byte> GetSqlBytes() => new((byte[])sqlBytes.Clone());

    public static TenantSchemaMigrationArtifact LoadTenantCoreBaseline(string applicationBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);
        var path = Path.GetFullPath(Path.Combine(
            applicationBaseDirectory, "tenant-database", "migrations", "001_tenant_core_baseline.sql"));

        if (!File.Exists(path))
            throw new FileNotFoundException("Reviewed tenant schema baseline migration is missing from the agent package.", path);

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length is < 1 or > MaximumArtifactBytes)
            throw new InvalidOperationException("Reviewed tenant schema baseline migration has an invalid size.");

        string sql;
        try
        {
            sql = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new InvalidOperationException("Reviewed tenant schema baseline migration is not valid UTF-8.");
        }

        if (!sql.Contains("CREATE TABLE mg.tenant_identity", StringComparison.Ordinal)
            || !sql.Contains("INSERT INTO mg.tenant_identity", StringComparison.Ordinal)
            || !sql.Contains("INSERT INTO mg.schema_migrations", StringComparison.Ordinal))
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new InvalidOperationException("Packaged tenant schema baseline does not contain the required identity and migration ledger operations.");
        }

        return new TenantSchemaMigrationArtifact(bytes);
    }
}
