using System.Text.Json;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// SQL persistence for connection resources.
/// </summary>
public sealed class SqlConnectionRepository : IConnectionRepository
{
    private readonly AppertureDbContext _db;

    public SqlConnectionRepository(AppertureDbContext db) => _db = db;

    /// <inheritdoc />
    public async ValueTask<Connection?> GetByIdAsync(ConnectionId id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Connections.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id.Value, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Connection>> GetVisibleAsync(string scopeKey, CancellationToken cancellationToken = default)
    {
        var entities = await _db.Connections.AsNoTracking().ToListAsync(cancellationToken);

        // Filter by scope containment: actor's scopeKey contains connection's scope
        var filtered = entities
            .Select(ToDomain)
            .Where(c => IsScopeContained(c.ScopeKey ?? "instance", scopeKey))
            .ToList();
        return filtered;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Connection>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _db.Connections.AsNoTracking()
            .Where(e => e.State == (int)ConnectionState.Active)
            .ToListAsync(cancellationToken);
        return entities.Select(ToDomain).ToList();
    }

    /// <inheritdoc />
    public async ValueTask AddAsync(Connection connection, CancellationToken cancellationToken = default)
    {
        var entity = ToEntity(connection);
        _db.Connections.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask UpdateAsync(Connection connection, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Connections.FirstOrDefaultAsync(e => e.Id == connection.Id.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Connection {connection.Id.Value} not found.");

        entity.Name = connection.Name;
        entity.ConfigJson = JsonSerializer.Serialize(connection.Config);
        entity.SecretRefsJson = JsonSerializer.Serialize(connection.SecretRefs);
        entity.ScopeKey = connection.ScopeKey;
        entity.OwnerId = connection.OwnerId;
        entity.State = (int)connection.State;

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static Connection ToDomain(ConnectionEntity e) => new(
        new ConnectionId(e.Id),
        new ConnectorDefinitionId(e.ConnectorDefinitionId),
        e.Name,
        DeserializeDict(e.ConfigJson),
        DeserializeDict(e.SecretRefsJson),
        e.ScopeKey,
        e.OwnerId,
        (ConnectionState)e.State);

    private static ConnectionEntity ToEntity(Connection c) => new()
    {
        Id = c.Id.Value,
        ConnectorDefinitionId = c.ConnectorDefinitionId.Value,
        Name = c.Name,
        ConfigJson = JsonSerializer.Serialize(c.Config),
        SecretRefsJson = JsonSerializer.Serialize(c.SecretRefs),
        ScopeKey = c.ScopeKey,
        OwnerId = c.OwnerId,
        State = (int)c.State
    };

    private static IReadOnlyDictionary<string, string> DeserializeDict(string json)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        return dict;
    }

    private static bool IsScopeContained(string inner, string outer)
    {
        if (string.Equals(outer, "instance", StringComparison.Ordinal)) return true;
        if (string.Equals(inner, outer, StringComparison.Ordinal)) return true;
        return inner.StartsWith(outer + ":", StringComparison.Ordinal);
    }
}
