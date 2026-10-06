using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Persistence.SqlServer.Audit;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq.Expressions;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Appends audit records to AuditLogs table. Epic 37: Outcome and reference columns are first-class.
/// Query supports DB-backed filtering by actor, time, outcome, moduleId, scheduleId, runId.
/// </summary>
/// <remarks>
/// <para><see cref="AppendAsync"/> is the AUD-1.1 transitional bridge (audit hardening design gate §14) for writers not yet
/// converted to the recorder. Each writer keeps its current write mode and guarantee: the row is saved on the caller's
/// scoped context, so it is atomic with the caller's change only where the caller already wrapped both in one
/// transaction. Converting a writer to <see cref="IAuditedUnitOfWork"/> is what makes it atomic.</para>
/// <para>A bridge row carries <c>Producer = append</c>, the caller's <see cref="AuditRecord.Id"/> as its event id and
/// operation event id, and the details text exactly as given (no <c>Resource=</c> suffix; the resource descriptor was
/// never queryable). It is inserted with its whole classification set when insert-time classification succeeds, or with
/// none of it when any lookup cannot run; the next Management Host start completes it. Never partially.</para>
/// </remarks>
public sealed class SqlAuditService : IAuditService
{
    private readonly AppertureDbContext _db;
    private readonly ILogger<SqlAuditService> _logger;

    public SqlAuditService(AppertureDbContext db, ILogger<SqlAuditService>? logger = null)
    {
        _db = db;
        _logger = logger ?? NullLogger<SqlAuditService>.Instance;
    }

    /// <inheritdoc />
    public async ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        var eventId = record.Id?.Value;
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 64)
            eventId = Guid.NewGuid().ToString("N");

        var outcome = string.IsNullOrEmpty(record.Outcome) ? null : record.Outcome;
        if (AuditActionCatalog.Default.Find(record.Action) is null)
        {
            _logger.LogWarning(
                "Audit action {AuditAction} is not declared in the audit catalog; it is classified as 'other'. Declare it.",
                record.Action);
        }

        var classification = await TryClassifyAsync(
            new AuditLegacyRow(record.ActorId.Value, record.Action, outcome, record.Details, AuditProducers.Append),
            cancellationToken).ConfigureAwait(false);

        var entity = new AuditLogEntity
        {
            At = record.Timestamp,
            Actor = record.ActorId.Value,
            Action = record.Action,
            Details = record.Details,
            Outcome = outcome,
            ModuleId = record.ModuleId,
            ScheduleId = record.ScheduleId,
            RunId = record.RunId,
            ResourceContextId = record.ResourceContextId,
            // Whole-branch review Finding 1 (CRITICAL): CorrelationId is a non-nullable-typed reference
            // that a class type still lets a caller pass null through (e.g. RecoveryController.cs:405's
            // own convention is `new CorrelationId(null)`, never a bare `null` -- but nothing enforced
            // that at the call site until now). `record.CorrelationId.Value` used to be safe only because
            // nothing dereferenced it; once this line existed (Task 1), a bare-null CorrelationId argument
            // became an NRE that silently dropped the whole audit append under any best-effort catch {}
            // wrapper -- see SchedulerOperationsController.cs's TryAuditAsync, which was hit by exactly
            // this. Null-conditional matches every other nullable field in this same initializer.
            CorrelationId = record.CorrelationId?.Value,
            EventId = eventId,
            OperationEventId = eventId,
            Producer = AuditProducers.Append,
        };
        if (classification is not null)
            AuditClassificationColumns.Apply(entity, classification);

        _db.AuditLogs.Add(entity);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (AuditRecorderRows.IsEventIdViolation(ex))
        {
            // The save was retried after a lost acknowledgement: the row with this event id already exists (§13.5).
            _db.Entry(entity).State = EntityState.Detached;
            _logger.LogInformation("Audit row {EventId} ({AuditAction}) was already written.", eventId, record.Action);
        }
    }

    /// <summary>
    /// Insert-time classification (§14) by the same rules as the startup step (<see cref="AuditRowClassifier"/>), against
    /// only the identity rows that can bear on this actor value. Any failure leaves the whole set null.
    /// </summary>
    private async Task<AuditClassificationSet?> TryClassifyAsync(AuditLegacyRow row, CancellationToken cancellationToken)
    {
        try
        {
            var classifier = await AuditActorEvidence.LoadForValueAsync(_db, row.Actor, cancellationToken).ConfigureAwait(false);
            var set = AuditRowClassifier.Classify(row, classifier, AuditActionCatalog.Default);
            if (set.ActorKind == AuditActorKinds.User)
            {
                // Event-time username (§10.3): read inside this write.
                var username = await _db.Users.AsNoTracking()
                    .Where(u => u.Id == set.ActorUserId)
                    .Select(u => u.Username)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                set = set with { ActorUsername = username ?? set.ActorUsername };
            }

            return set;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Audit row for {AuditAction} could not be classified at insert; it is stored unclassified and completed at the next start.",
                row.Action);
            return null;
        }
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
            new CorrelationId(e.CorrelationId),
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
            new CorrelationId(entity.CorrelationId),
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
