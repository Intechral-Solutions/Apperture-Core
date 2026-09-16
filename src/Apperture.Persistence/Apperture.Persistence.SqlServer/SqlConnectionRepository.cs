using System.Text.Json;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.ResourceContexts;
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
    public ValueTask<IReadOnlyList<Connection>> GetVisibleAsync(string scopeKey, CancellationToken cancellationToken = default) =>
        GetVisibleAsync(scopeKey, resourceContextId: null, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Connection>> GetVisibleAsync(
        string scopeKey,
        ResourceContextId? resourceContextId,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Connections.AsNoTracking().AsQueryable();

        // Context filtering happens in SQL. The authorization scope filter below is the pre-existing
        // in-memory containment evaluation and is deliberately left as-is.
        if (resourceContextId is not null)
            query = query.Where(e => e.ResourceContextId == resourceContextId.Value);

        var entities = await query.ToListAsync(cancellationToken);

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
        // Ownership only ever changes through the narrow SetResourceContextIdAsync method below (added
        // in CTX-3B Plan 2 for ResourceOwnershipMoveService), never through this general UpdateAsync.
        entity.OwnerId = connection.OwnerId;
        entity.State = (int)connection.State;
        entity.LastVerificationStatus = (int)connection.LastVerificationStatus;
        entity.LastVerificationAtUtc = connection.LastVerificationAtUtc;
        entity.LastVerificationSummary = connection.LastVerificationSummary;

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<Connection?> GetByIdForUpdateAsync(ConnectionId id, CancellationToken cancellationToken = default)
    {
        List<ConnectionEntity> rows;
        if (_db.Database.IsRelational())
        {
            // AsNoTracking is load-bearing: see the interface remarks on execution-strategy retries.
            // The UPDLOCK hint lives in the SQL text, so the lock is taken and held to end of transaction
            // regardless of tracking behavior.
            rows = await _db.Connections
                .FromSqlInterpolated($"SELECT * FROM [Connections] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {id.Value}")
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }
        else
        {
            rows = await _db.Connections.AsNoTracking()
                .Where(e => e.Id == id.Value)
                .ToListAsync(cancellationToken);
        }

        var entity = rows.SingleOrDefault();
        return entity is null ? null : ToDomain(entity);
    }

    /// <inheritdoc />
    public async ValueTask SetResourceContextIdAsync(ConnectionId id, ResourceContextId resourceContextId, CancellationToken cancellationToken = default)
    {
        if (_db.Database.IsRelational())
        {
            var affected = await _db.Connections
                .Where(e => e.Id == id.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ResourceContextId, resourceContextId.Value), cancellationToken);
            if (affected == 0)
                throw new InvalidOperationException($"Connection {id.Value} not found.");
            return;
        }

        // InMemory fallback: ExecuteUpdateAsync is unsupported there. Same precedent as
        // SqlScheduleRepository's existing ExecuteUpdateAsync/tracked-fallback pair.
        var entity = await _db.Connections.FirstOrDefaultAsync(e => e.Id == id.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Connection {id.Value} not found.");
        entity.ResourceContextId = resourceContextId.Value;
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
        (ConnectionState)e.State,
        (ConnectionVerificationStatus)e.LastVerificationStatus,
        e.LastVerificationAtUtc,
        e.LastVerificationSummary)
    {
        ResourceContextId = new ResourceContextId(
            string.IsNullOrWhiteSpace(e.ResourceContextId) ? ResourceContextIds.SharedValue : e.ResourceContextId)
    };

    private static ConnectionEntity ToEntity(Connection c) => new()
    {
        Id = c.Id.Value,
        ConnectorDefinitionId = c.ConnectorDefinitionId.Value,
        Name = c.Name,
        ConfigJson = JsonSerializer.Serialize(c.Config),
        SecretRefsJson = JsonSerializer.Serialize(c.SecretRefs),
        ScopeKey = c.ScopeKey,
        ResourceContextId = c.ResourceContextId.Value,
        OwnerId = c.OwnerId,
        State = (int)c.State,
        LastVerificationStatus = (int)c.LastVerificationStatus,
        LastVerificationAtUtc = c.LastVerificationAtUtc,
        LastVerificationSummary = c.LastVerificationSummary
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
