namespace Mg.Provisioning.Agent.Core;

/// <summary>
/// A durable tenant/idempotency key is already bound to a different request.
/// This is a permanent conflict, not a transient provisioning failure.
/// </summary>
public sealed class AgentProvisioningConflictException(string message)
    : InvalidOperationException(message);
