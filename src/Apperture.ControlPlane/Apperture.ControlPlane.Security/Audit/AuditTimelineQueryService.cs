using Apperture.Core.Application.Services;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security.Audit;

/// <summary>
/// Query service for audit timeline discoverability. Epic 32.
/// </summary>
public sealed class AuditTimelineQueryService : IAuditTimelineQueryService
{
    private readonly IAuditService _audit;

    public AuditTimelineQueryService(IAuditService audit) => _audit = audit;

    /// <inheritdoc />
    public async ValueTask<AuditTimelinePage> QueryAsync(AuditFilterCriteria criteria, CancellationToken cancellationToken = default)
    {
        var actorId = string.IsNullOrEmpty(criteria.Actor) ? null : new ActorId(criteria.Actor);
        var query = new AuditQuery(
            FilterByActorId: actorId,
            FilterByActionPrefix: criteria.ActionPrefix,
            FromUtc: criteria.FromUtc,
            ToUtc: criteria.ToUtc,
            PageSize: criteria.PageSize,
            ContinuationToken: criteria.ContinuationToken);

        var pageSize = Math.Clamp(criteria.PageSize, 1, 100);
        var fetchSize = pageSize;
        if (criteria.ModuleId is not null || criteria.ScheduleId is not null || criteria.RunId is not null || criteria.Outcome is not null)
        {
            fetchSize = Math.Min(pageSize * 3, 300);
        }

        query = query with { PageSize = fetchSize };

        var page = await _audit.QueryAsync(query, cancellationToken);
        var entries = page.Items
            .Select(r => MapToEntry(r))
            .Where(e => MatchesInMemoryFilters(e, criteria))
            .Take(pageSize)
            .ToList();

        var nextToken = page.Items.Count >= fetchSize ? page.NextContinuationToken : null;

        return new AuditTimelinePage(entries, nextToken);
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
        var outcome = AuditDetailParser.ParseOutcome(details);
        var refs = AuditDetailParser.ParseRelatedReferences(details);
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

    private static bool MatchesInMemoryFilters(AuditTimelineEntry e, AuditFilterCriteria c)
    {
        if (!string.IsNullOrEmpty(c.ModuleId))
        {
            var hasModule = e.RelatedReferences.Any(r =>
                string.Equals(r.Kind, "module", StringComparison.Ordinal)
                && string.Equals(r.Id, c.ModuleId, StringComparison.Ordinal));
            if (!hasModule) return false;
        }

        if (!string.IsNullOrEmpty(c.ScheduleId))
        {
            var hasSchedule = e.RelatedReferences.Any(r =>
                string.Equals(r.Kind, "schedule", StringComparison.Ordinal)
                && string.Equals(r.Id, c.ScheduleId, StringComparison.Ordinal));
            if (!hasSchedule) return false;
        }

        if (!string.IsNullOrEmpty(c.RunId))
        {
            var hasRun = e.RelatedReferences.Any(r =>
                string.Equals(r.Kind, "run", StringComparison.Ordinal)
                && string.Equals(r.Id, c.RunId, StringComparison.Ordinal));
            if (!hasRun) return false;
        }

        if (!string.IsNullOrEmpty(c.Outcome))
        {
            if (!string.Equals(e.Outcome, c.Outcome, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
