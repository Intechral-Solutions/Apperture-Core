using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// SQL persistence for ResourceContexts.
/// </summary>
/// <remarks>
/// Intentionally exposes no delete. ResourceContexts are archiveable only, and the foreign keys from
/// Integrations, Connections and ScheduleDefinitions use restrictive delete behavior as structural
/// backup for that rule.
/// </remarks>
public sealed class SqlResourceContextRepository : IResourceContextRepository
{
    private readonly AppertureDbContext _db;

    public SqlResourceContextRepository(AppertureDbContext db) => _db = db;

    /// <inheritdoc />
    public async ValueTask<ResourceContext?> GetByIdAsync(ResourceContextId id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ResourceContexts.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id.Value, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    /// <inheritdoc />
    public async ValueTask<ResourceContext?> GetBySlugAsync(string normalizedSlug, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ResourceContexts.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Slug == normalizedSlug, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ResourceContext>> ListAsync(bool includeArchived = true, CancellationToken cancellationToken = default)
    {
        var query = _db.ResourceContexts.AsNoTracking().AsQueryable();
        if (!includeArchived)
            query = query.Where(e => e.State == ResourceContextStates.Active);

        var entities = await query.OrderBy(e => e.Name).ToListAsync(cancellationToken);
        return entities.Select(ToDomain).ToList();
    }

    /// <inheritdoc />
    public async ValueTask AddAsync(ResourceContext resourceContext, CancellationToken cancellationToken = default)
    {
        _db.ResourceContexts.Add(ToEntity(resourceContext));
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask UpdateAsync(ResourceContext resourceContext, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ResourceContexts.FirstOrDefaultAsync(e => e.Id == resourceContext.Id.Value, cancellationToken)
            ?? throw new InvalidOperationException($"ResourceContext {resourceContext.Id.Value} not found.");

        entity.Name = resourceContext.Name;
        entity.Slug = resourceContext.Slug;
        entity.Kind = resourceContext.Kind;
        entity.State = resourceContext.State;
        entity.Description = resourceContext.Description;
        entity.UpdatedAtUtc = resourceContext.UpdatedAtUtc;

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<ResourceContextOwnershipSummary> GetOwnershipSummaryAsync(ResourceContextId id, CancellationToken cancellationToken = default)
    {
        var all = await GetOwnershipSummariesAsync(cancellationToken).ConfigureAwait(false);
        return all.TryGetValue(id.Value, out var summary)
            ? summary
            : new ResourceContextOwnershipSummary(0, 0, 0, 0, 0, 0);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<string, ResourceContextOwnershipSummary>> GetOwnershipSummariesAsync(CancellationToken cancellationToken = default)
    {
        // One GROUP BY per resource family -- a constant number of queries regardless of how many
        // ResourceContexts exist, so this scales for callers like the Applications list page.
        var integrationCounts = await _db.Integrations
            .GroupBy(e => new { e.ResourceContextId, e.State })
            .Select(g => new { g.Key.ResourceContextId, g.Key.State, Count = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var connectionCounts = await _db.Connections
            .GroupBy(e => new { e.ResourceContextId, e.State })
            .Select(g => new { g.Key.ResourceContextId, g.Key.State, Count = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var scheduleCounts = await _db.ScheduleDefinitions
            .GroupBy(e => new { e.ResourceContextId, e.Enabled })
            .Select(g => new { g.Key.ResourceContextId, g.Key.Enabled, Count = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var liveIntegrationsByContext = integrationCounts.Where(x => x.State == (int)IntegrationState.Active)
            .ToDictionary(x => x.ResourceContextId, x => x.Count, StringComparer.Ordinal);
        var retiredIntegrationsByContext = integrationCounts.Where(x => x.State == (int)IntegrationState.Retired)
            .ToDictionary(x => x.ResourceContextId, x => x.Count, StringComparer.Ordinal);
        var activeConnectionsByContext = connectionCounts.Where(x => x.State == (int)ConnectionState.Active)
            .ToDictionary(x => x.ResourceContextId, x => x.Count, StringComparer.Ordinal);
        var retiredConnectionsByContext = connectionCounts.Where(x => x.State == (int)ConnectionState.Retired)
            .ToDictionary(x => x.ResourceContextId, x => x.Count, StringComparer.Ordinal);
        var enabledSchedulesByContext = scheduleCounts.Where(x => x.Enabled)
            .ToDictionary(x => x.ResourceContextId, x => x.Count, StringComparer.Ordinal);
        var disabledSchedulesByContext = scheduleCounts.Where(x => !x.Enabled)
            .ToDictionary(x => x.ResourceContextId, x => x.Count, StringComparer.Ordinal);

        var contextIds = integrationCounts.Select(x => x.ResourceContextId)
            .Concat(connectionCounts.Select(x => x.ResourceContextId))
            .Concat(scheduleCounts.Select(x => x.ResourceContextId))
            .Distinct(StringComparer.Ordinal);

        var result = new Dictionary<string, ResourceContextOwnershipSummary>(StringComparer.Ordinal);
        foreach (var contextId in contextIds)
        {
            result[contextId] = new ResourceContextOwnershipSummary(
                LiveIntegrations: liveIntegrationsByContext.GetValueOrDefault(contextId),
                RetiredIntegrations: retiredIntegrationsByContext.GetValueOrDefault(contextId),
                ActiveConnections: activeConnectionsByContext.GetValueOrDefault(contextId),
                RetiredConnections: retiredConnectionsByContext.GetValueOrDefault(contextId),
                EnabledSchedules: enabledSchedulesByContext.GetValueOrDefault(contextId),
                DisabledSchedules: disabledSchedulesByContext.GetValueOrDefault(contextId));
        }

        return result;
    }

    private static ResourceContext ToDomain(ResourceContextEntity e) => new(
        new ResourceContextId(e.Id),
        e.Name,
        e.Slug,
        e.Kind,
        e.State,
        e.IsReserved,
        e.Description,
        e.CreatedAtUtc,
        e.UpdatedAtUtc);

    private static ResourceContextEntity ToEntity(ResourceContext c) => new()
    {
        Id = c.Id.Value,
        Name = c.Name,
        Slug = c.Slug,
        Kind = c.Kind,
        State = c.State,
        IsReserved = c.IsReserved,
        Description = c.Description,
        CreatedAtUtc = c.CreatedAtUtc,
        UpdatedAtUtc = c.UpdatedAtUtc
    };
}
