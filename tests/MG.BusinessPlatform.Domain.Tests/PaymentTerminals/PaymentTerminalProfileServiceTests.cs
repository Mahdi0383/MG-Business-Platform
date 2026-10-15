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
