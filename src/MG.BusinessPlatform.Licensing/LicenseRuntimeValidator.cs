using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace MG.BusinessPlatform.Licensing;

/// <summary>
/// Validates the installed license from the shared local data directory without
/// creating or rewriting any licensing files. Used by both the desktop client and
/// the independently running customer storefront process.
/// </summary>
public static class LicenseRuntimeValidator
{
    public static LicenseValidationResult ValidateDirectory(string? dataDirectory, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
            return new(LicenseValidationStatus.MissingLicense,
                "The local license data directory is not configured.");

        try
        {
            var root = Path.GetFullPath(dataDirectory);
            var serialPath = Path.Combine(root, "software-serial.txt");
            var licensePath = Path.Combine(root, "license.json");
            if (!File.Exists(serialPath))
                return new(LicenseValidationStatus.MissingLicense,
                    "The installation software serial is missing.");
            if (!File.Exists(licensePath))
                return new(LicenseValidationStatus.MissingLicense,
                    "The installation does not have an activated license.");

            var softwareSerial = File.ReadAllText(serialPath).Trim().ToUpperInvariant();
            if (!softwareSerial.StartsWith("MGS-", StringComparison.Ordinal)
                || softwareSerial.Length != 36
                || !Guid.TryParseExact(softwareSerial[4..], "N", out _))
                return new(LicenseValidationStatus.InvalidFormat,
                    "The saved software serial is invalid.");

            var hardwareSerial = GetHardwareSerial();
            var publicKey = LicenseCryptography.GetTrustedPublicKeyPem();
            var envelope = LicenseCryptography.DeserializeEnvelope(File.ReadAllText(licensePath));
            return LicenseCryptography.Verify(envelope, publicKey, softwareSerial, hardwareSerial, nowUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.Text.Json.JsonException or ArgumentException or CryptographicException
            or InvalidOperationException or System.Security.SecurityException)
        {
            return new(LicenseValidationStatus.InvalidFormat,
                $"Installed license validation failed: {ex.Message}");
        }
    }

    public static string GetHardwareSerial()
    {
        string machineId;
        if (!OperatingSystem.IsWindows())
        {
            machineId = Environment.MachineName;
        }
        else
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", writable: false);
                machineId = key?.GetValue("MachineGuid")?.ToString() ?? Environment.MachineName;
            }
            catch (UnauthorizedAccessException)
            {
                machineId = Environment.MachineName;
            }
            catch (System.Security.SecurityException)
            {
                machineId = Environment.MachineName;
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(machineId.Trim().ToUpperInvariant()));
        return "MGH-" + Convert.ToHexString(hash.AsSpan(0, 16));
    }
}
