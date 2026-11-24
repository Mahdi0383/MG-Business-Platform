namespace Mg.Platform.Api.Health;

/// <summary>
/// Checksums for reviewed, immutable platform SQL migration artifacts.
/// Updating a migration requires an explicit reviewed checksum change.
/// </summary>
public static class PlatformMigrationChecksums
{
    public const string Migration006Id = "006_business_creation_idempotency";
    public const string Migration006Sha256 =
        "bc5ea7800741e9de958590c99fd73bd6d882980928f71567ff08dc72a38b5d1d";
}
