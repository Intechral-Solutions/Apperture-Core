using System.Collections.Immutable;
using Apperture.Core.Application.Services;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security.Audit;

/// <summary>
/// Query service for audit timeline discoverability. Epic 32, Epic 37.
/// All filters (time range, actor, action prefix, outcome, moduleId, scheduleId, runId) are DB-backed.
/// </summary>
public sealed class AuditTimelineQueryService : IAuditTimelineQueryService
{
    private readonly IAuditService _audit;

    public AuditTimelineQueryService(IAuditService audit) => _audit = audit;

    /// <inheritdoc />
    public async ValueTask<AuditTimelinePage> QueryAsync(AuditFilterCriteria criteria, CancellationToken cancellationToken = default)
    {
        var fromUtc = criteria.FromUtc;
        if (!fromUtc.HasValue)
            fromUtc = DateTimeOffset.UtcNow.AddHours(-24);

        var actorId = string.IsNullOrEmpty(criteria.Actor) ? null : new ActorId(criteria.Actor);
        var query = new AuditQuery(
            FilterByActorId: actorId,
            FilterByActionPrefix: criteria.ActionPrefix,
            FromUtc: fromUtc,
            ToUtc: criteria.ToUtc,
            PageSize: criteria.PageSize,
            ContinuationToken: criteria.ContinuationToken,
            FilterByOutcome: criteria.Outcome,
            FilterByModuleId: criteria.ModuleId,
            FilterByScheduleId: criteria.ScheduleId,
            FilterByRunId: criteria.RunId);

        var page = await _audit.QueryAsync(query, cancellationToken);
        var entries = page.Items.Select(r => MapToEntry(r)).ToList();
        return new AuditTimelinePage(entries, page.NextContinuationToken);
    }

    /// <inheritdoc />
    public async ValueTask<AuditTimelineEntry?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var record = await _audit.GetByIdAsync(id, cancellationToken);
        return record is null ? null : MapToEntry(record);
    }

    private static AuditTimelineEntry MapToEntry(Core.Domain.Audit.AuditRecord r)
    {
        var details = r.Details ?? "";
        var outcome = !string.IsNullOrEmpty(r.Outcome) ? r.Outcome : AuditDetailParser.ParseOutcome(details);
        var refs = MergeRelatedReferences(r, AuditDetailParser.ParseRelatedReferences(details));
        var category = AuditCategoryResolver.Resolve(r.Action);
        var summary = AuditDetailParser.BuildSummary(r.Action, outcome);
        return new AuditTimelineEntry(
            r.Id.Value,
            r.Timestamp,
            r.ActorId.Value,
            r.Action,
            category,
            outcome,
            summary,
            details.Length > 0 ? details : null,
            refs);
    }

    private static IReadOnlyList<AuditRelatedReference> MergeRelatedReferences(
        Core.Domain.Audit.AuditRecord r,
        IReadOnlyList<AuditRelatedReference> parsed)
    {
        var seen = new HashSet<(string Kind, string Id)>();
        var result = new List<AuditRelatedReference>();
        foreach (var p in parsed)
        {
            var key = (p.Kind, p.Id);
            if (seen.Add(key))
                result.Add(p);
        }
        if (!string.IsNullOrEmpty(r.ModuleId) && seen.Add(("module", r.ModuleId)))
            result.Add(new AuditRelatedReference("module", r.ModuleId));
        if (!string.IsNullOrEmpty(r.ScheduleId) && seen.Add(("schedule", r.ScheduleId)))
            result.Add(new AuditRelatedReference("schedule", r.ScheduleId));
        if (!string.IsNullOrEmpty(r.RunId) && seen.Add(("run", r.RunId)))
            result.Add(new AuditRelatedReference("run", r.RunId));
        return result.ToImmutableList();
    }
}
