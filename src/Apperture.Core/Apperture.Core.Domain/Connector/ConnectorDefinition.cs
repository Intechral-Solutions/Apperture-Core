using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Connector;

/// <summary>
/// Declares a connector kind: required inputs, resolution contract.
/// Platform or module-provided. Governed resource type.
/// </summary>
public sealed record ConnectorDefinition(
    ConnectorDefinitionId Id,
    string Name,
    string? ModuleId,
    IReadOnlyDictionary<string, string> RequiredInputSchema) : IConnectorDefinition
{
    /// <summary>
    /// Returns the resource identity for authorization.
    /// </summary>
    public IGovernedResourceIdentity GetIdentity() =>
        new Governance.GovernedResourceIdentity(
            ResourceTypeConstants.ConnectorDefinition,
            Id.Value,
            ModuleId is not null ? $"module:{ModuleId}" : null,
            null);
}
