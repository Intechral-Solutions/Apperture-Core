using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Appends audit records to AuditLogs table. Query supports basic filtering by actor and time.
/// </summary>
public sealed class SqlAuditService : IAuditService
{
    private readonly AppertureDbContext _db;

    public SqlAuditService(AppertureDbContext db) => _db = db;

    /// <inheritdoc />
    public async ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        var details = record.Details ?? record.Outcome;
        if (!string.IsNullOrEmpty(record.Outcome) && record.Details != null)
            details = $"{record.Outcome}; {record.Details}";
        else if (record.Resource is not null)
            details = (details ?? "") + (string.IsNullOrEmpty(details) ? "" : "; ") + $"Resource={record.Resource}";

        _db.AuditLogs.Add(new AuditLogEntity
        {
            At = record.Timestamp,
            Actor = record.ActorId.Value,
            Action = record.Action,
            Details = details
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        var q = _db.AuditLogs.AsNoTracking().OrderByDescending(x => x.At).AsQueryable();

        if (query.FilterByActorId is { } actorId)
            q = q.Where(x => x.Actor == actorId.Value);
        if (query.FromUtc.HasValue)
            q = q.Where(x => x.At >= query.FromUtc.Value);
        if (query.ToUtc.HasValue)
            q = q.Where(x => x.At <= query.ToUtc.Value);

        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var items = await q.Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = items.Count > pageSize;
        if (hasMore)
            items.RemoveAt(items.Count - 1);

        var records = items.Select(e => new AuditRecord(
            new AuditRecordId(e.Id.ToString()),
            new ActorId(e.Actor),
            e.Action,
            Resource: null,
            Outcome: "",
            e.At,
            new CorrelationId(null),
            e.Details)).ToList();

        var nextToken = hasMore ? items[^1].Id.ToString() : null;
        return new AuditPage(records, nextToken);
    }
}
