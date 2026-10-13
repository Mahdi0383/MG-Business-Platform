using System.Globalization;
using System.Security.Cryptography;
using MG.BusinessPlatform.Licensing;

return await LicenseTool.RunAsync(args);

internal static class LicenseTool
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            PrintHelp();
            return args.Length == 0 ? 2 : 0;
        }

        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "keygen":
                    return GenerateKeys(args);
                case "issue":
                    return await IssueLicenseAsync(args);
                case "verify":
                    return await VerifyLicenseAsync(args);
                case "fingerprint":
                    return ShowFingerprint(args);
                default:
                    Console.Error.WriteLine($"Unknown command: {args[0]}");
                    PrintHelp();
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"License operation failed: {ex.Message}");
            return 2;
        }
    }

    private static int GenerateKeys(string[] args)
    {
        if (args.Length != 3)
            throw new ArgumentException("Usage: keygen <private-key.pem> <public-key.pem>");

        var privatePath = Path.GetFullPath(args[1]);
        var publicPath = Path.GetFullPath(args[2]);
        if (string.Equals(privatePath, publicPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Private and public key paths must differ.");
        if (File.Exists(privatePath) || File.Exists(publicPath))
            throw new IOException("Refusing to overwrite an existing key file.");

        Directory.CreateDirectory(Path.GetDirectoryName(privatePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(publicPath)!);
        var keys = LicenseCryptography.GenerateKeyPair();
        File.WriteAllText(privatePath, keys.PrivateKeyPem);
        try
        {
            File.WriteAllText(publicPath, keys.PublicKeyPem);
        }
        catch
        {
            try { File.Delete(privatePath); } catch (IOException) { }
            try { if (File.Exists(publicPath)) File.Delete(publicPath); } catch (IOException) { }
            throw;
        }

        Console.WriteLine($"Private signing key: {privatePath}");
        Console.WriteLine($"Public verification key: {publicPath}");
        Console.WriteLine("IMPORTANT: keep the private key offline and never place it in the application folder or source repository.");
        Console.WriteLine("The public key must be provisioned to each licensed installation as license-public-key.pem.");
        return 0;
    }

    private static async Task<int> IssueLicenseAsync(string[] args)
    {
        if (args.Length is < 8 or > 10)
            throw new ArgumentException("Usage: issue <private-key.pem> <output.json> <software-serial> <hardware-serial> <customer-name> <expiry-yyyy-MM-dd> <modules-csv> [max-branches] [max-users]");

        var privateKey = File.ReadAllText(args[1]);
        var expiryDate = DateOnly.ParseExact(args[6], "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var expiry = new DateTimeOffset(expiryDate.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc));
        var modules = args[7].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var maxBranches = args.Length >= 9 ? int.Parse(args[8], CultureInfo.InvariantCulture) : 1;
        var maxUsers = args.Length >= 10 ? int.Parse(args[9], CultureInfo.InvariantCulture) : 5;

        var payload = new LicensePayload
        {
            LicenseId = Guid.NewGuid(),
            CustomerName = args[5],
            SoftwareSerial = args[3],
            HardwareSerial = args[4],
            IssuedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = expiry,
            LicenseType = "Full",
            Modules = modules,
            MaxBranches = maxBranches,
            MaxUsers = maxUsers
        }.Validate();

        var envelope = LicenseCryptography.Sign(payload, privateKey);
        var output = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (File.Exists(output))
            throw new IOException("Refusing to overwrite an existing license file.");
        await File.WriteAllTextAsync(output, LicenseCryptography.SerializeEnvelope(envelope));
        Console.WriteLine($"License created: {output}");
        Console.WriteLine($"License ID: {payload.LicenseId}");
        Console.WriteLine($"Customer: {payload.CustomerName}");
        Console.WriteLine($"Expires UTC: {payload.ExpiresAtUtc:O}");
        Console.WriteLine($"Modules: {string.Join(", ", payload.Modules)}");
        return 0;
    }

    private static async Task<int> VerifyLicenseAsync(string[] args)
    {
        if (args.Length != 5)
            throw new ArgumentException("Usage: verify <public-key.pem> <license.json> <software-serial> <hardware-serial>");

        var publicKey = await File.ReadAllTextAsync(args[1]);
        var json = await File.ReadAllTextAsync(args[2]);
        var envelope = LicenseCryptography.DeserializeEnvelope(json);
        var result = LicenseCryptography.Verify(envelope, publicKey, args[3], args[4], DateTimeOffset.UtcNow);
        Console.WriteLine($"{result.Status}: {result.Message}");
        if (result.Payload is not null)
        {
            Console.WriteLine($"License ID: {result.Payload.LicenseId}");
            Console.WriteLine($"Customer: {result.Payload.CustomerName}");
            Console.WriteLine($"Expiry UTC: {result.Payload.ExpiresAtUtc:O}");
            Console.WriteLine($"Modules: {string.Join(", ", result.Payload.Modules)}");
        }
        return result.IsValid ? 0 : 3;
    }

    private static int ShowFingerprint(string[] args)
    {
        if (args.Length != 2)
            throw new ArgumentException("Usage: fingerprint <public-key.pem>");
        var publicKey = File.ReadAllText(args[1]);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKey);
        var hash = SHA256.HashData(rsa.ExportSubjectPublicKeyInfo());
        Console.WriteLine(Convert.ToHexString(hash));
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("MG Business Platform License Manager");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  keygen <private-key.pem> <public-key.pem>");
        Console.WriteLine("  issue <private-key.pem> <output.json> <software-serial> <hardware-serial> <customer-name> <expiry-yyyy-MM-dd> <modules-csv> [max-branches] [max-users]");
        Console.WriteLine("  verify <public-key.pem> <license.json> <software-serial> <hardware-serial>");
        Console.WriteLine("  fingerprint <public-key.pem>");
        Console.WriteLine();
        Console.WriteLine("Supported modules: " + string.Join(", ", LicensePayload.SupportedModules));
    }
}
