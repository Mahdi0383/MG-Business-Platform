using System.Net;
using System.Net.Sockets;
using MG.BusinessPlatform.Infrastructure.Services;

namespace MG.BusinessPlatform.Domain.Tests.PaymentTerminals;

public sealed class PaymentTerminalProfileServiceTests
{
    [Fact]
    public async Task SaveLoad_NormalizesProfileAndKeepsOnlyOneDefault()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mg-pcpos-{Guid.NewGuid():N}");
        var service = new PaymentTerminalProfileService(Path.Combine(directory, "profiles.json"));
        try
        {
            var first = new PaymentTerminalProfile
            {
                Name = "  Counter  ",
                CurrencyCode = "IRR",
                IsDefault = true
            };
            await service.SaveAsync(first);
            var second = new PaymentTerminalProfile
            {
                Name = "Backup terminal",
                CurrencyCode = "USD",
                IsDefault = true
            };
            await service.SaveAsync(second);

            var profiles = await service.LoadAsync();
            Assert.Equal(2, profiles.Count);
            Assert.Equal("Counter", profiles.Single(x => x.Id == first.Id).Name);
            Assert.Equal("IRR", profiles.Single(x => x.Id == first.Id).CurrencyCode);
            Assert.False(profiles.Single(x => x.Id == first.Id).IsDefault);
            Assert.True(profiles.Single(x => x.Id == second.Id).IsDefault);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task Save_RejectsInvalidTcpProfileBeforePersisting()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mg-pcpos-{Guid.NewGuid():N}");
        var service = new PaymentTerminalProfileService(Path.Combine(directory, "profiles.json"));
        try
        {
            var profile = new PaymentTerminalProfile
            {
                Name = "LAN terminal",
                ConnectionType = PaymentConnectionType.TcpIp,
                Host = string.Empty,
                Port = 5000
            };

            await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(profile));
            Assert.Empty(await service.LoadAsync());
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task TestConnection_TcpOnlyConfirmsTransportAndNeverClaimsPaymentApproval()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var service = new PaymentTerminalProfileService(Path.Combine(Path.GetTempPath(), $"mg-pcpos-{Guid.NewGuid():N}.json"));
        var profile = new PaymentTerminalProfile
        {
            Name = "Local simulator",
            ConnectionType = PaymentConnectionType.TcpIp,
            Host = "127.0.0.1",
            Port = port
        };

        var result = await service.TestConnectionAsync(profile);

        Assert.True(result.ConfigurationValid);
        Assert.True(result.TransportConnected);
        Assert.Equal("TransportConnected", result.Status);
        Assert.Contains("نه پشتیبانی پروتکل، تأیید پرداخت", result.Message);
    }

    [Fact]
    public async Task TestConnection_PropagatesCallerCancellation()
    {
        var service = new PaymentTerminalProfileService(Path.Combine(Path.GetTempPath(), $"mg-pcpos-{Guid.NewGuid():N}.json"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var profile = new PaymentTerminalProfile
        {
            Name = "Cancelled diagnostic",
            ConnectionType = PaymentConnectionType.TcpIp,
            Host = "127.0.0.1",
            Port = 5000
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.TestConnectionAsync(profile, cancellation.Token));
    }

    [Fact]
    public async Task ExportAndImport_RoundTripNonSecretProfileSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mg-pcpos-{Guid.NewGuid():N}");
        var sourceService = new PaymentTerminalProfileService(Path.Combine(directory, "source", "profiles.json"));
        var destinationService = new PaymentTerminalProfileService(Path.Combine(directory, "destination", "profiles.json"));
        var exportPath = Path.Combine(directory, "export", "profiles.json");
        try
        {
            var profile = new PaymentTerminalProfile
            {
                Name = "Counter LAN",
                Vendor = "Example vendor",
                Model = "Example model",
                ConnectionType = PaymentConnectionType.TcpIp,
                Host = "127.0.0.1",
                Port = 5000,
                MerchantId = "merchant-123",
                AuthenticationReference = "credential-ref-001",
                IsDefault = true
            };
            await sourceService.SaveAsync(profile);
            await sourceService.ExportAsync(exportPath);
            await destinationService.ImportAsync(exportPath);

            var imported = Assert.Single(await destinationService.LoadAsync());
            Assert.Equal(profile.Name, imported.Name);
            Assert.Equal(profile.Host, imported.Host);
            Assert.Equal(profile.Port, imported.Port);
            Assert.Equal(profile.MerchantId, imported.MerchantId);
            Assert.Equal("credential-ref-001", imported.AuthenticationReference);
            Assert.True(imported.IsDefault);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task Import_InvalidProfileFilePreservesCurrentProfiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mg-pcpos-{Guid.NewGuid():N}");
        var service = new PaymentTerminalProfileService(Path.Combine(directory, "active", "profiles.json"));
        var invalidPath = Path.Combine(directory, "invalid.json");
        try
        {
            var existing = new PaymentTerminalProfile { Name = "Existing" };
            await service.SaveAsync(existing);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(invalidPath,
                """[{"Id":"00000000-0000-0000-0000-000000000000","Name":"Invalid","DriverId":"manual","CurrencyCode":"IRR"}]""");

            await Assert.ThrowsAsync<ArgumentException>(() => service.ImportAsync(invalidPath));

            var profiles = await service.LoadAsync();
            Assert.Single(profiles);
            Assert.Equal("Existing", profiles[0].Name);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void Validate_RejectsCredentialsEmbeddedInVendorEndpoint()
    {
        var profile = new PaymentTerminalProfile
        {
            Name = "Vendor API",
            ConnectionType = PaymentConnectionType.VendorLocalApi,
            Endpoint = "https://user:password@127.0.0.1:9000/api"
        };

        Assert.Throws<ArgumentException>(() => PaymentTerminalProfileService.Validate(profile));
    }
}
