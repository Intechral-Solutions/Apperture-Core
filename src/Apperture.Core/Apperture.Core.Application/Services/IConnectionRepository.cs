using Apperture.Core.Domain.Connector;

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
