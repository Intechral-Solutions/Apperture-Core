using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Connector;

/// <summary>
/// Declares a connector kind: required inputs, resolution contract.
/// Platform or module-provided connector definitions implement this.
/// </summary>
public interface IConnectorDefinition
{
    ConnectorDefinitionId Id { get; }
    string Name { get; }
    string? ModuleId { get; }
    IReadOnlyDictionary<string, string> RequiredInputSchema { get; }
    string? Description { get; }
    IReadOnlyList<ConnectionFieldDescriptor> ConnectionFields { get; }
    ConnectorCapabilities Capabilities { get; }
    IGovernedResourceIdentity GetIdentity();
}
