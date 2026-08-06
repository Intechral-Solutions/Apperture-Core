using Apperture.Core.Application.OperationItems;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <summary>JOB-1 — SQL persistence for operation-item attention acknowledgements.</summary>
public sealed class SqlOperationItemAttentionAcknowledgementStore : IOperationItemAttentionAcknowledgementStore
{
    private readonly AppertureDbContext _db;

    public SqlOperationItemAttentionAcknowledgementStore(AppertureDbContext db)
    {
        _db = db;
    }

    public async Task<OperationItemAttentionAcknowledgement> CreateAsync(
        CreateOperationItemAttentionAcknowledgementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var itemId = request.OperationItemId.Trim();
        var existing = await GetActiveByItemIdAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        var disposition = OperationItemAttentionDispositions.Normalize(request.Disposition)
            ?? throw new OperationItemizationException($"Unknown acknowledgement disposition '{request.Disposition}'.");

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason is { Length: > OperationItemAttentionBounds.ReasonMaxLength })
        {
            throw new OperationItemizationException(
                $"Acknowledgement reason exceeds {OperationItemAttentionBounds.ReasonMaxLength} characters.");
        }

        var now = request.AcknowledgedAtUtc ?? DateTimeOffset.UtcNow;
        var entity = new OperationItemAttentionAcknowledgementEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            OperationItemId = itemId,
            RunId = request.RunId.Trim(),
            OperationGroupId = request.OperationGroupId.Trim(),
            Disposition = disposition,
            Reason = reason,
            AcknowledgedAtUtc = now,
            AcknowledgedByUserId = request.AcknowledgedByUserId.Trim(),
            ReopenedAtUtc = null,
            ReopenedByUserId = null,
            CreatedAtUtc = now,
        };

        _db.OperationItemAttentionAcknowledgements.Add(entity);
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Concurrent create — return the winning active row.
            _db.Entry(entity).State = EntityState.Detached;
            var winner = await GetActiveByItemIdAsync(itemId, cancellationToken).ConfigureAwait(false);
            if (winner is not null)
                return winner;
            throw;
        }

        return Map(entity);
    }

    public async Task<OperationItemAttentionAcknowledgement?> GetActiveByItemIdAsync(
        string operationItemId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationItemId))
            return null;

        var row = await _db.OperationItemAttentionAcknowledgements.AsNoTracking()
            .Where(a => a.OperationItemId == operationItemId.Trim() && a.ReopenedAtUtc == null)
            .OrderByDescending(a => a.AcknowledgedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyDictionary<string, OperationItemAttentionAcknowledgement>> GetActiveByItemIdsAsync(
        IReadOnlyCollection<string> operationItemIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationItemIds);
        var ids = operationItemIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
            return new Dictionary<string, OperationItemAttentionAcknowledgement>(StringComparer.Ordinal);

        var rows = await _db.OperationItemAttentionAcknowledgements.AsNoTracking()
            .Where(a => ids.Contains(a.OperationItemId) && a.ReopenedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(a => a.OperationItemId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => Map(g.OrderByDescending(x => x.AcknowledgedAtUtc).First()),
                StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<OperationItemAttentionAcknowledgement>> ListByItemIdAsync(
        string operationItemId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationItemId))
            return [];

        var rows = await _db.OperationItemAttentionAcknowledgements.AsNoTracking()
            .Where(a => a.OperationItemId == operationItemId.Trim())
            .OrderByDescending(a => a.AcknowledgedAtUtc)
            .ThenByDescending(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(Map).ToArray();
    }

    public async Task<OperationItemAttentionAcknowledgement?> ReopenActiveAsync(
        string operationItemId,
        string reopenedByUserId,
        DateTimeOffset? reopenedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationItemId) || string.IsNullOrWhiteSpace(reopenedByUserId))
            return null;

        var row = await _db.OperationItemAttentionAcknowledgements
            .Where(a => a.OperationItemId == operationItemId.Trim() && a.ReopenedAtUtc == null)
            .OrderByDescending(a => a.AcknowledgedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return null;

        row.ReopenedAtUtc = reopenedAtUtc ?? DateTimeOffset.UtcNow;
        row.ReopenedByUserId = reopenedByUserId.Trim();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Map(row);
    }

    public async Task<int> CountActiveAcknowledgedFailuresByRunAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return 0;

        return await _db.OperationItemAttentionAcknowledgements.AsNoTracking()
            .CountAsync(a => a.RunId == runId.Trim() && a.ReopenedAtUtc == null, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountActiveAcknowledgedFailuresByGroupAsync(
        string operationGroupId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationGroupId))
            return 0;

        return await _db.OperationItemAttentionAcknowledgements.AsNoTracking()
            .CountAsync(a => a.OperationGroupId == operationGroupId.Trim() && a.ReopenedAtUtc == null, cancellationToken)
            .ConfigureAwait(false);
    }

    private static OperationItemAttentionAcknowledgement Map(OperationItemAttentionAcknowledgementEntity e) =>
        new(
            e.Id,
            e.OperationItemId,
            e.RunId,
            e.OperationGroupId,
            e.Disposition,
            e.Reason,
            e.AcknowledgedAtUtc,
            e.AcknowledgedByUserId,
            e.ReopenedAtUtc,
            e.ReopenedByUserId,
            e.CreatedAtUtc);
}
