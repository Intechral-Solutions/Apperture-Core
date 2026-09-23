using Apperture.Core.Application.Connector;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.ResourceContexts;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Application workflow for connection management. Enforces authorization at each operation.
/// </summary>
public interface IConnectionManagementService
{
    /// <summary>
    /// Lists connections visible to the current actor.
    /// </summary>
    ValueTask<IReadOnlyList<Connection>> ListConnectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists connections visible to the current actor, optionally restricted to one owning ResourceContext.
    /// </summary>
    /// <param name="resourceContextId">
    /// Owning ResourceContext to filter by, or null for the default global behavior (no context filter).
    /// </param>
    /// <remarks>
    /// The default implementation exists only so in-memory test doubles keep working; the production
    /// implementation pushes the filter down to the repository query.
    /// </remarks>
    async ValueTask<IReadOnlyList<Connection>> ListConnectionsAsync(
        ResourceContextId? resourceContextId,
        CancellationToken cancellationToken = default)
    {
        var all = await ListConnectionsAsync(cancellationToken).ConfigureAwait(false);
        if (resourceContextId is null)
            return all;
        return all.Where(c => string.Equals(c.ResourceContextId.Value, resourceContextId.Value, StringComparison.Ordinal)).ToList();
    }

    /// <summary>
    /// Lists the connections made from one connector definition, in every state, that the current actor may
    /// see. Authorized exactly like <see cref="ListConnectionsAsync(CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// The default implementation exists only so in-memory test doubles keep working; the production
    /// implementation pushes the connector filter down to the repository query.
    /// </remarks>
    async ValueTask<IReadOnlyList<Connection>> ListConnectionsByConnectorAsync(
        ConnectorDefinitionId connectorDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var all = await ListConnectionsAsync(cancellationToken).ConfigureAwait(false);
        return all.Where(c => c.ConnectorDefinitionId == connectorDefinitionId).ToList();
    }

    /// <summary>
    /// Gets a connection by id. Returns null if not found or not visible.
    /// </summary>
    ValueTask<Connection?> GetConnectionAsync(ConnectionId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new connection.
    /// </summary>
    ValueTask<Connection> CreateConnectionAsync(CreateConnectionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates connection metadata/config.
    /// </summary>
    ValueTask<Connection> UpdateConnectionAsync(ConnectionId id, UpdateConnectionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retires a connection. Excluded from binding eligibility.
    /// </summary>
    ValueTask RetireConnectionAsync(ConnectionId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes connector-specific test-connect and persists the latest verification result on the connection.
    /// Requires <c>connection.read</c> on the connection (diagnostic for operators who can view the resource).
    /// </summary>
    ValueTask<Connection> VerifyConnectionAsync(ConnectionId id, CancellationToken cancellationToken = default);
}
