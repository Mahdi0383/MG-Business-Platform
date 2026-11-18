using Mg.Provisioning.Agent.Core;

namespace Mg.Provisioning.Agent.Api;

/// <summary>Registered only when the isolated agent has a real provisioner and secret source.</summary>
public interface IAgentProvisioningHandler
{
    Task<string> ProvisionAsync(AgentProvisioningRequest request, CancellationToken cancellationToken);
}
