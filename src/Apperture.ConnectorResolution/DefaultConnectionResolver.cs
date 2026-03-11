using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Security.Secrets.Abstractions;

namespace Apperture.ConnectorResolution;

/// <summary>
/// Resolves a connection to a live client by building resolved material and delegating to the registered connector factory.
/// </summary>
public sealed class DefaultConnectionResolver : IConnectionResolver
{
    private readonly ISecretResolver _secretResolver;
    private readonly IConnectorDefinitionRegistry _connectorRegistry;
    private readonly IConnectorClientFactoryRegistry _factoryRegistry;

    public DefaultConnectionResolver(
        ISecretResolver secretResolver,
        IConnectorDefinitionRegistry connectorRegistry,
        IConnectorClientFactoryRegistry factoryRegistry)
    {
        _secretResolver = secretResolver ?? throw new ArgumentNullException(nameof(secretResolver));
        _connectorRegistry = connectorRegistry ?? throw new ArgumentNullException(nameof(connectorRegistry));
        _factoryRegistry = factoryRegistry ?? throw new ArgumentNullException(nameof(factoryRegistry));
    }

    /// <inheritdoc />
    public async ValueTask<T> ResolveAsync<T>(Connection connection, CancellationToken cancellationToken = default) where T : class
    {
        if (connection is null)
            throw new ArgumentNullException(nameof(connection));

        var def = _connectorRegistry.Get(connection.ConnectorDefinitionId);
        if (def is null)
            throw new InvalidOperationException($"Connector definition {connection.ConnectorDefinitionId.Value} not found.");

        var factory = _factoryRegistry.GetFactory<T>(connection.ConnectorDefinitionId);
        if (factory is null)
            throw new InvalidOperationException($"No connector client factory registered for {connection.ConnectorDefinitionId.Value} and type {typeof(T).Name}.");

        var material = await ResolvedConnectionMaterialBuilder.BuildAsync(connection, _secretResolver, cancellationToken);
        return factory.Create(connection, material);
    }
}
