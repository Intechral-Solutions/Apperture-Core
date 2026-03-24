using Apperture.Core.Application.Connector;
using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Connector-specific factory that creates a live client from a connection and resolved material.
/// One per (ConnectorDefinitionId, T). Do not use ISecretResolver in implementations; receive only ResolvedConnectionMaterial.
/// Typed clients and factories live in connector/module code; there is no platform-managed client invocation layer.
/// </summary>
/// <typeparam name="T">The client type (e.g. IDbConnection).</typeparam>
public interface IConnectorClientFactory<out T> where T : class
{
    /// <summary>
    /// Creates a live client from the governed connection and resolved config/secret material.
    /// Caller is responsible for disposal where applicable.
    /// </summary>
    T Create(Connection connection, ResolvedConnectionMaterial material);
}
