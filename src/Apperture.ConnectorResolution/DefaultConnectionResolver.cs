using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;

namespace Apperture.ConnectorResolution;

/// <summary>
/// Which <see cref="ConnectionResolutionPath"/> slot resolution reports in this host. The Worker Host resolves slots to run
/// steps; the Management Host resolves them only for the integration settings options. Registered by
/// <c>AddConnectorResolution</c>, so the label follows the host and not the caller.
/// </summary>
public sealed record ConnectionSlotResolution(ConnectionResolutionPath Path);

/// <summary>
/// Resolves a connection to a live client by building resolved material and delegating to the registered connector factory.
/// The material comes only from <see cref="IConnectionMaterialResolver"/>, which proves each secret reference is the
/// connection's own before the secret is read.
/// </summary>
public sealed class DefaultConnectionResolver : IConnectionResolver
{
    private readonly IConnectionMaterialResolver _materialResolver;
    private readonly IConnectorDefinitionRegistry _connectorRegistry;
    private readonly IConnectorClientFactoryRegistry _factoryRegistry;
    private readonly ConnectionResolutionPath _path;

    public DefaultConnectionResolver(
        IConnectionMaterialResolver materialResolver,
        IConnectorDefinitionRegistry connectorRegistry,
        IConnectorClientFactoryRegistry factoryRegistry,
        ConnectionSlotResolution? slotResolution = null)
    {
        _materialResolver = materialResolver ?? throw new ArgumentNullException(nameof(materialResolver));
        _connectorRegistry = connectorRegistry ?? throw new ArgumentNullException(nameof(connectorRegistry));
        _factoryRegistry = factoryRegistry ?? throw new ArgumentNullException(nameof(factoryRegistry));
        _path = slotResolution?.Path ?? ConnectionResolutionPath.Run;
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

        var material = await _materialResolver.ResolveAsync(connection, _path, cancellationToken);
        return factory.Create(connection, material);
    }
}
