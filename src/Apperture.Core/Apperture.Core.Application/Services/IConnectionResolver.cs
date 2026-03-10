using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Resolves a connection resource into a live client. Internal to the resolution path.
/// Uses ISecretResolver and connector-specific logic. Auth does not depend on this.
/// </summary>
public interface IConnectionResolver
{
    /// <summary>
    /// Resolves a connection into a live client of the specified type.
    /// The connection must be bound to the requesting context (module/slot).
    /// </summary>
    /// <typeparam name="T">Expected client type (e.g. IDbConnection, HttpClient).</typeparam>
    /// <param name="connection">The governed connection resource.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved live client. Caller is responsible for disposal where applicable.</returns>
    ValueTask<T> ResolveAsync<T>(Connection connection, CancellationToken cancellationToken = default)
        where T : class;
}
