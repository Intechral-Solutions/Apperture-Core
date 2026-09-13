using System.Text.Json;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
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
    public ValueTask<IReadOnlyList<DomainIntegration>> GetVisibleAsync(string scopeKey, CancellationToken cancellationToken = default) =>
        GetVisibleAsync(scopeKey, resourceContextId: null, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DomainIntegration>> GetVisibleAsync(
        string scopeKey,
        ResourceContextId? resourceContextId,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Integrations.AsNoTracking().AsQueryable();

        // Context filtering happens in SQL. The authorization scope filter below is the pre-existing
        // in-memory containment evaluation and is deliberately left as-is.
        if (resourceContextId is not null)
            query = query.Where(e => e.ResourceContextId == resourceContextId.Value);

        var entities = await query.ToListAsync(cancellationToken);

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
        // Ownership is established at create time. CTX-1 has no move operation, so UpdateAsync
        // deliberately does not rewrite ResourceContextId.
        entity.State = (int)integration.State;
        entity.SettingsJson = integration.Settings is { Count: > 0 }
            ? JsonSerializer.Serialize(integration.Settings)
            : null;
        entity.UpdatedAt = integration.UpdatedAt;

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static DomainIntegration ToDomain(IntegrationEntity e) => new(
        new IntegrationId(e.Id),
        e.DisplayName,
        e.ModuleId,
        e.ScopeKey,
        (IntegrationState)e.State,
        DeserializeSettings(e.SettingsJson),
        e.CreatedAt,
        e.UpdatedAt)
    {
        ResourceContextId = new ResourceContextId(
            string.IsNullOrWhiteSpace(e.ResourceContextId) ? ResourceContextIds.SharedValue : e.ResourceContextId)
    };

    private static IntegrationEntity ToEntity(DomainIntegration i) => new()
    {
        Id = i.Id.Value,
        DisplayName = i.DisplayName,
        ModuleId = i.ModuleId,
        ScopeKey = i.ScopeKey,
        ResourceContextId = i.ResourceContextId.Value,
        State = (int)i.State,
        SettingsJson = i.Settings is { Count: > 0 }
            ? JsonSerializer.Serialize(i.Settings)
            : null,
        CreatedAt = i.CreatedAt,
        UpdatedAt = i.UpdatedAt
    };

    private static IReadOnlyDictionary<string, string>? DeserializeSettings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        return dict is { Count: > 0 } ? dict : null;
    }

    private static bool IsScopeContained(string inner, string outer)
    {
        if (string.Equals(outer, ScopeKeyConstants.Instance, StringComparison.Ordinal)) return true;
        if (string.Equals(inner, outer, StringComparison.Ordinal)) return true;
        return inner.StartsWith(outer + ":", StringComparison.Ordinal);
    }
}
