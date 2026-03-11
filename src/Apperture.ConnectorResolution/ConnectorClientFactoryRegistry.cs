using System.Collections.Concurrent;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;

namespace Apperture.ConnectorResolution;

/// <summary>
/// In-memory registry of connector client factories by connector definition id and client type.
/// </summary>
public sealed class ConnectorClientFactoryRegistry : IConnectorClientFactoryRegistry
{
    private readonly ConcurrentDictionary<(string Id, Type Type), object> _factories = new();

    /// <inheritdoc />
    public void Register<T>(ConnectorDefinitionId connectorDefinitionId, IConnectorClientFactory<T> factory) where T : class
    {
        if (factory is null)
            throw new ArgumentNullException(nameof(factory));
        _factories[(connectorDefinitionId.Value, typeof(T))] = factory;
    }

    /// <inheritdoc />
    public IConnectorClientFactory<T>? GetFactory<T>(ConnectorDefinitionId connectorDefinitionId) where T : class
    {
        return _factories.TryGetValue((connectorDefinitionId.Value, typeof(T)), out var boxed) && boxed is IConnectorClientFactory<T> f
            ? f
            : null;
    }
}
