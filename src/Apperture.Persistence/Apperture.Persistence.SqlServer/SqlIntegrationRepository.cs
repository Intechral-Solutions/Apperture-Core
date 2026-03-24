using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// SQL persistence for Integration resources. Epic 49.
/// </summary>
public sealed class SqlIntegrationRepository : IIntegrationRepository
{
    private readonly AppertureDbContext _db;

    public SqlIntegrationRepository(AppertureDbContext db) => _db = db;

    /// <inheritdoc />
    public async ValueTask<DomainIntegration?> GetByIdAsync(IntegrationId id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Integrations.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id.Value, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DomainIntegration>> GetVisibleAsync(string scopeKey, CancellationToken cancellationToken = default)
    {
        var entities = await _db.Integrations.AsNoTracking().ToListAsync(cancellationToken);

        var filtered = entities
            .Select(ToDomain)
            .Where(i => IsScopeContained(i.ScopeKey ?? ScopeKeyConstants.Instance, scopeKey))
            .ToList();
        return filtered;
    }

    /// <inheritdoc />
    public async ValueTask AddAsync(DomainIntegration integration, CancellationToken cancellationToken = default)
    {
        var entity = ToEntity(integration);
        _db.Integrations.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask UpdateAsync(DomainIntegration integration, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Integrations.FirstOrDefaultAsync(e => e.Id == integration.Id.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Integration {integration.Id.Value} not found.");

        entity.DisplayName = integration.DisplayName;
        entity.ModuleId = integration.ModuleId;
        entity.ScopeKey = integration.ScopeKey;
        entity.State = (int)integration.State;
        entity.UpdatedAt = integration.UpdatedAt;

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static DomainIntegration ToDomain(IntegrationEntity e) => new(
        new IntegrationId(e.Id),
        e.DisplayName,
        e.ModuleId,
        e.ScopeKey,
        (IntegrationState)e.State,
        e.CreatedAt,
        e.UpdatedAt);

    private static IntegrationEntity ToEntity(DomainIntegration i) => new()
    {
        Id = i.Id.Value,
        DisplayName = i.DisplayName,
        ModuleId = i.ModuleId,
        ScopeKey = i.ScopeKey,
        State = (int)i.State,
        CreatedAt = i.CreatedAt,
        UpdatedAt = i.UpdatedAt
    };

    private static bool IsScopeContained(string inner, string outer)
    {
        if (string.Equals(outer, ScopeKeyConstants.Instance, StringComparison.Ordinal)) return true;
        if (string.Equals(inner, outer, StringComparison.Ordinal)) return true;
        return inner.StartsWith(outer + ":", StringComparison.Ordinal);
    }
}
