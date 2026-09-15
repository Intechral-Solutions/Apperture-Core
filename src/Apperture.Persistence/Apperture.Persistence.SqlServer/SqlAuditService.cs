using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Appends audit records to AuditLogs table. Epic 37: Outcome and reference columns are first-class.
/// Query supports DB-backed filtering by actor, time, outcome, moduleId, scheduleId, runId.
/// </summary>
public sealed class SqlAuditService : IAuditService
{
    private readonly AppertureDbContext _db;

    public SqlAuditService(AppertureDbContext db) => _db = db;

    /// <inheritdoc />
    public async ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        var details = record.Details;
        if (record.Resource is not null)
            details = (details ?? "") + (string.IsNullOrEmpty(details) ? "" : "; ") + $"Resource={record.Resource}";

        _db.AuditLogs.Add(new AuditLogEntity
        {
            At = record.Timestamp,
            Actor = record.ActorId.Value,
            Action = record.Action,
            Details = details,
            Outcome = string.IsNullOrEmpty(record.Outcome) ? null : record.Outcome,
            ModuleId = record.ModuleId,
            ScheduleId = record.ScheduleId,
            RunId = record.RunId,
            ResourceContextId = record.ResourceContextId
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        var q = ApplyFilters(query, forPaging: true)
            .OrderByDescending(x => x.At)
            .ThenByDescending(x => x.Id);

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
            Outcome: e.Outcome ?? "",
            e.At,
            new CorrelationId(null),
            e.Details,
            ModuleId: e.ModuleId,
            ScheduleId: e.ScheduleId,
            RunId: e.RunId,
            ResourceContextId: e.ResourceContextId)).ToList();

        var nextToken = hasMore ? items[^1].Id.ToString() : null;
        return new AuditPage(records, nextToken);
    }

    /// <inheritdoc />
    public async ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
        await ApplyFilters(query, forPaging: false).CountAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(id, out var pk))
            return null;

        var entity = await _db.AuditLogs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == pk, cancellationToken);
        if (entity is null)
            return null;

        return new AuditRecord(
            new AuditRecordId(entity.Id.ToString()),
            new ActorId(entity.Actor),
            entity.Action,
            Resource: null,
            Outcome: entity.Outcome ?? "",
            entity.At,
            new CorrelationId(null),
            entity.Details,
            ModuleId: entity.ModuleId,
            ScheduleId: entity.ScheduleId,
            RunId: entity.RunId,
            ResourceContextId: entity.ResourceContextId);
    }

    private IQueryable<AuditLogEntity> ApplyFilters(AuditQuery query, bool forPaging)
    {
        var q = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (query.FilterByActorId is { } actorId)
            q = q.Where(x => x.Actor == actorId.Value);
        if (!string.IsNullOrEmpty(query.FilterByActionPrefix))
            q = q.Where(x => x.Action.StartsWith(query.FilterByActionPrefix));
        if (query.FromUtc.HasValue)
            q = q.Where(x => x.At >= query.FromUtc.Value);
        if (query.ToUtc.HasValue)
            q = q.Where(x => x.At <= query.ToUtc.Value);
        if (!string.IsNullOrEmpty(query.FilterByOutcome))
            q = q.Where(x => x.Outcome != null && x.Outcome == query.FilterByOutcome);
        if (!string.IsNullOrEmpty(query.FilterByModuleId))
            q = q.Where(x => x.ModuleId != null && x.ModuleId == query.FilterByModuleId);
        if (!string.IsNullOrEmpty(query.FilterByScheduleId))
            q = q.Where(x => x.ScheduleId != null && x.ScheduleId == query.FilterByScheduleId);
        if (!string.IsNullOrEmpty(query.FilterByRunId))
            q = q.Where(x => x.RunId != null && x.RunId == query.FilterByRunId);
        if (!string.IsNullOrEmpty(query.FilterByResourceContextId))
            q = q.Where(x => x.ResourceContextId != null && x.ResourceContextId == query.FilterByResourceContextId);

        if (query.FilterByContextClassification != AuditContextClassificationFilter.None)
        {
            q = q.Where(x => x.ResourceContextId == null);
            if (query.FilterByContextClassification == AuditContextClassificationFilter.System)
            {
                q = q.Where(AnyActionPrefix(AuditSystemActionPrefixes.All));
            }
            else
            {
                foreach (var prefix in AuditSystemActionPrefixes.All)
                    q = q.Where(x => !x.Action.StartsWith(prefix));
            }
        }

        if (forPaging
            && !string.IsNullOrEmpty(query.ContinuationToken)
            && long.TryParse(query.ContinuationToken, out var lastId))
            q = q.Where(x => x.Id < lastId);

        return q;
    }

    /// <summary>
    /// Builds an <c>OrElse</c> chain of <see cref="string.StartsWith(string)"/> calls that EF Core can
    /// translate to SQL. A plain <c>IEnumerable.Any(p =&gt; x.Action.StartsWith(p))</c> closure does not
    /// translate.
    /// </summary>
    private static Expression<Func<AuditLogEntity, bool>> AnyActionPrefix(IEnumerable<string> prefixes)
    {
        var x = Expression.Parameter(typeof(AuditLogEntity), "x");
        var action = Expression.Property(x, nameof(AuditLogEntity.Action));
        var startsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
        Expression body = Expression.Constant(false);
        foreach (var prefix in prefixes)
            body = Expression.OrElse(body, Expression.Call(action, startsWith, Expression.Constant(prefix)));
        return Expression.Lambda<Func<AuditLogEntity, bool>>(body, x);
    }
}
