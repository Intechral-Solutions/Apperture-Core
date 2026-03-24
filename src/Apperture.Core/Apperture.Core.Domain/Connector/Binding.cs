using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Connector;

/// <summary>
/// Explicit delegation: a declared capability slot is bound to a connection or capability.
/// Authorizes the consumer module to use the bound resource for that purpose.
/// Epic 50: IntegrationId when binding is owned by an Integration.
/// </summary>
public sealed record Binding(
    BindingId Id,
    IntegrationId? IntegrationId,
    string ModuleId,
    string SlotName,
    string ContractType,
    ConnectionId? ConnectionId,
    string? CapabilityId,
    BindingState State) : IGovernedResource
{
    /// <summary>
    /// Slot scope key for authorization (e.g. module:ModuleId:slot:SlotName).
    /// </summary>
    public string SlotScopeKey => $"module:{ModuleId}:slot:{SlotName}";

    /// <inheritdoc />
    public IGovernedResourceIdentity GetIdentity() =>
        new Governance.GovernedResourceIdentity(
            ResourceTypeConstants.Binding,
            Id.Value,
            SlotScopeKey,
            null);
}
