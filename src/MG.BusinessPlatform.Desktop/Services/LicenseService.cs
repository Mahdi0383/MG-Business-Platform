using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using MG.BusinessPlatform.Licensing;

namespace MG.BusinessPlatform.Desktop.Services;

public sealed record MachineLicenseInfo(
    string SoftwareSerial,
    string HardwareSerial,
    LicenseValidationResult Validation);

public sealed class LicenseService
{
    public MachineLicenseInfo LoadOrCreate(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var softwareSerialPath = Path.Combine(dataDirectory, "software-serial.txt");
        var softwareSerial = LoadOrCreateSoftwareSerial(softwareSerialPath);
        var hardwareSerial = GetHardwareSerial();
        var publicKeyPath = Path.Combine(dataDirectory, "license-public-key.pem");
        var licensePath = Path.Combine(dataDirectory, "license.json");

        if (!File.Exists(publicKeyPath))
            return new(softwareSerial, hardwareSerial, new(
                LicenseValidationStatus.MissingPublicKey,
                "کلید عمومی لایسنس پیکربندی نشده است."));
        if (!File.Exists(licensePath))
            return new(softwareSerial, hardwareSerial, new(
                LicenseValidationStatus.MissingLicense,
                "فایل لایسنس فعال‌سازی نشده است. داده‌های کسب‌وکار حفظ می‌شوند."));

        try
        {
            var publicKey = File.ReadAllText(publicKeyPath);
            var envelope = LicenseCryptography.DeserializeEnvelope(File.ReadAllText(licensePath));
            var result = LicenseCryptography.Verify(envelope, publicKey, softwareSerial, hardwareSerial, DateTimeOffset.UtcNow);
            return new(softwareSerial, hardwareSerial, result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
            or ArgumentException or CryptographicException)
        {
            return new(softwareSerial, hardwareSerial, new(
                LicenseValidationStatus.InvalidFormat,
                $"خواندن یا اعتبارسنجی لایسنس ناموفق بود: {ex.Message}"));
        }
    }

    public static string GetHardwareSerial()
    {
        string machineId;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWAREMicrosoftCryptography", writable: false);
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

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(machineId.Trim().ToUpperInvariant()));
        return "MGH-" + Convert.ToHexString(hash.AsSpan(0, 16));
    }

    private static string LoadOrCreateSoftwareSerial(string path)
    {
        if (File.Exists(path))
            return ValidateSoftwareSerial(File.ReadAllText(path));

        var serial = "MGS-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryPath, serial);
            try
            {
                File.Move(temporaryPath, path, overwrite: false);
                return serial;
            }
            catch (IOException) when (File.Exists(path))
            {
                return ValidateSoftwareSerial(File.ReadAllText(path));
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string ValidateSoftwareSerial(string value)
    {
        var serial = value.Trim().ToUpperInvariant();
        if (!serial.StartsWith("MGS-", StringComparison.Ordinal) || serial.Length != 36
            || !Guid.TryParseExact(serial[4..], "N", out _))
            throw new InvalidDataException("The saved software serial is invalid; it was not regenerated to avoid invalidating an existing license.");
        return serial;
    }
}
