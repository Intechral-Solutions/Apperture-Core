using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Registry of connector client factories by connector definition and client type.
/// Used by IConnectionResolver implementation to create live clients.
/// </summary>
public interface IConnectorClientFactoryRegistry
{
    /// <summary>
    /// Registers a factory for the given connector definition and client type.
    /// </summary>
    void Register<T>(ConnectorDefinitionId connectorDefinitionId, IConnectorClientFactory<T> factory) where T : class;

    /// <summary>
    /// Gets the factory for the given connector definition and client type, or null.
    /// </summary>
    IConnectorClientFactory<T>? GetFactory<T>(ConnectorDefinitionId connectorDefinitionId) where T : class;
}
