using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Connector;

/// <summary>
/// Declares a connector kind: required inputs, resolution contract, and connection-field metadata for operators.
/// Platform or module-provided. Governed resource type.
/// </summary>
public sealed record ConnectorDefinition(
    ConnectorDefinitionId Id,
    string Name,
    string? ModuleId,
    IReadOnlyDictionary<string, string> RequiredInputSchema) : IConnectorDefinition
{
    /// <summary>Human-readable detail for catalog and docs.</summary>
    public string? Description { get; init; }

    /// <summary>Config vs secret field hints for connection authoring (metadata only).</summary>
    public IReadOnlyList<ConnectionFieldDescriptor> ConnectionFields { get; init; } =
        Array.Empty<ConnectionFieldDescriptor>();

    /// <summary>Optional flags (e.g. future gateway/broker labeling).</summary>
    public ConnectorCapabilities Capabilities { get; init; } = ConnectorCapabilities.None;

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
