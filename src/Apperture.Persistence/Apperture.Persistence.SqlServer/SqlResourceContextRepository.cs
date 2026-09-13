using Apperture.Core.Application.Services;
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
    public async ValueTask<int> CountOwnedResourcesAsync(ResourceContextId id, CancellationToken cancellationToken = default)
    {
        var integrations = await _db.Integrations.CountAsync(e => e.ResourceContextId == id.Value, cancellationToken);
        var connections = await _db.Connections.CountAsync(e => e.ResourceContextId == id.Value, cancellationToken);
        var schedules = await _db.ScheduleDefinitions.CountAsync(e => e.ResourceContextId == id.Value, cancellationToken);
        return integrations + connections + schedules;
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
