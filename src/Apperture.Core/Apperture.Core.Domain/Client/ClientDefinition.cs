using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Domain.Client;

/// <summary>
/// Higher-level abstraction over a Connection. Epic 51.
/// Contributed by modules/connectors. Usage and execution deferred.
/// </summary>
public sealed record ClientDefinition(
    ClientDefinitionId Id,
    string DisplayName,
    string? Description,
    ConnectorDefinitionId RequiredConnectorDefinitionId,
    string? ModuleId,
    bool UsesGateway,
    IReadOnlyList<ClientOperationMetadata> Operations);
