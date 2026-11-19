using Mg.Platform.Api.Models;

namespace Mg.Platform.Api.Provisioning;

public interface ITenantProvisioningHandler
{
    Task ProvisionAsync(TenantProvisioningJob job, CancellationToken cancellationToken);
}
