using MG.BusinessPlatform.Infrastructure.Services;

namespace MG.BusinessPlatform.Domain.Tests.Services;

public sealed class RouterPortMappingServiceTests
{
    [Fact]
    public async Task RemoveWebPortsAsync_WhenNoMappingsWereRecorded_DoesNotProbeNetwork()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mg-upnp-test-{Guid.NewGuid():N}");
        try
        {
            var service = new RouterPortMappingService(directory);

            Assert.False(service.HasSavedMappings);
            var result = await service.RemoveWebPortsAsync();

            Assert.True(result.Success);
            Assert.False(service.HasSavedMappings);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
