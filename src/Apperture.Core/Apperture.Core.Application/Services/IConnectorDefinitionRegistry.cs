using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Registers and resolves connector definitions. Platform and module-provided connectors use this for discovery.
/// No connector-family types in auth; registry is application-layer only.
/// </summary>
public interface IConnectorDefinitionRegistry
{
    /// <summary>
    /// Registers a connector definition.
    /// </summary>
    void Register(ConnectorDefinition definition);

    /// <summary>
    /// Gets a connector definition by id.
    /// </summary>
    ConnectorDefinition? Get(ConnectorDefinitionId id);

    /// <summary>
    /// Gets all registered connector definitions, optionally filtered by module.
    /// </summary>
    IReadOnlyList<ConnectorDefinition> GetAll(string? moduleId = null);
}
