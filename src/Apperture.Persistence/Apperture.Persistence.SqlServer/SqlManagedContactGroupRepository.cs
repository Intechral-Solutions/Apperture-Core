using Apperture.Core.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlManagedContactGroupRepository : IManagedContactGroupRepository
{
    private readonly AppertureDbContext _db;

    public SqlManagedContactGroupRepository(AppertureDbContext db) => _db = db;

    public async Task<IReadOnlyList<ManagedContactGroupRepositoryRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.ManagedContactGroups.AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(x => new ManagedContactGroupRepositoryRow(
            x.Id, x.DisplayName, x.Source, x.EntraGroupObjectId, x.CreatedAtUtc, x.UpdatedAtUtc)).ToList();
    }

    public async Task<ManagedContactGroupRepositoryRow?> FindByEntraGroupObjectIdAsync(
        string entraGroupObjectId,
        CancellationToken cancellationToken = default)
    {
        var oid = entraGroupObjectId.Trim();
        var x = await _db.ManagedContactGroups.AsNoTracking()
            .FirstOrDefaultAsync(g => g.EntraGroupObjectId == oid, cancellationToken)
            .ConfigureAwait(false);
        return x is null
            ? null
            : new ManagedContactGroupRepositoryRow(x.Id, x.DisplayName, x.Source, x.EntraGroupObjectId, x.CreatedAtUtc, x.UpdatedAtUtc);
    }

    public async Task<ManagedContactGroupRepositoryRow> UpsertEntraGroupAsync(
        string entraGroupObjectId,
        string displayName,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var oid = entraGroupObjectId.Trim();
        var existing = await _db.ManagedContactGroups
            .FirstOrDefaultAsync(x => x.EntraGroupObjectId == oid, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            existing.DisplayName = string.IsNullOrWhiteSpace(displayName) ? oid : displayName.Trim();
            existing.UpdatedAtUtc = nowUtc;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new ManagedContactGroupRepositoryRow(
                existing.Id, existing.DisplayName, existing.Source, existing.EntraGroupObjectId, existing.CreatedAtUtc, existing.UpdatedAtUtc);
        }

        var entity = new ManagedContactGroupEntity
        {
            Id = "mctgrp_" + Guid.NewGuid().ToString("N"),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? oid : displayName.Trim(),
            Source = ManagedContactSources.Entra,
            EntraGroupObjectId = oid,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        _db.ManagedContactGroups.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ManagedContactGroupRepositoryRow(
            entity.Id, entity.DisplayName, entity.Source, entity.EntraGroupObjectId, entity.CreatedAtUtc, entity.UpdatedAtUtc);
    }
}
