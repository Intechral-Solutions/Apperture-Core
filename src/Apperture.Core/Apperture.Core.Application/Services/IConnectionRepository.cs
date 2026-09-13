using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.ResourceContexts;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Persistence for connection resources. Used by connection management and binding services.
/// </summary>
public interface IConnectionRepository
{
    /// <summary>
    /// Gets a connection by id.
    /// </summary>
    ValueTask<Connection?> GetByIdAsync(ConnectionId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets connections visible within the given scope (instance or contained scopes).
    /// </summary>
    ValueTask<IReadOnlyList<Connection>> GetVisibleAsync(string scopeKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets connections visible within the given scope, optionally restricted to one owning ResourceContext.
    /// </summary>
    /// <param name="resourceContextId">
    /// Owning ResourceContext to filter by, or null for the default global behavior (no context filter).
    /// </param>
    /// <remarks>
    /// A store-backed implementation must push the context filter into the query. The default
    /// implementation here exists only so in-memory test doubles keep working; it is not a query plan.
    /// </remarks>
    async ValueTask<IReadOnlyList<Connection>> GetVisibleAsync(
        string scopeKey,
        ResourceContextId? resourceContextId,
        CancellationToken cancellationToken = default)
    {
        var all = await GetVisibleAsync(scopeKey, cancellationToken).ConfigureAwait(false);
        if (resourceContextId is null)
            return all;
        return all.Where(c => string.Equals(c.ResourceContextId.Value, resourceContextId.Value, StringComparison.Ordinal)).ToList();
    }

    /// <summary>
    /// Gets all active connections (for eligibility filtering).
    /// </summary>
    ValueTask<IReadOnlyList<Connection>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new connection.
    /// </summary>
    ValueTask AddAsync(Connection connection, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing connection.
    /// </summary>
    ValueTask UpdateAsync(Connection connection, CancellationToken cancellationToken = default);
}
