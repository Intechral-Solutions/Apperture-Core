using Apperture.Core.Application.Connector;
using Apperture.Core.Domain.Connector;

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
}
