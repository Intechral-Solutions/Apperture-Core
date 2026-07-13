using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.PlatformEvents;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Platform events with no linked notification instance yet (typically worker module emits).
/// </summary>
public sealed class SqlPlatformEventNotificationEvaluationStore : IPlatformEventNotificationEvaluationStore
{
    private readonly AppertureDbContext _db;

    public SqlPlatformEventNotificationEvaluationStore(AppertureDbContext db) => _db = db;

    public async ValueTask<IReadOnlyList<PlatformEventEnvelope>> ListPendingEvaluationAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        var batch = Math.Clamp(take, 1, 200);
        var rows = await _db.PlatformEvents.AsNoTracking()
            .Where(e => e.ModuleId != null
                        && !_db.NotificationInstances.Any(i => i.PlatformEventId == e.Id))
            .OrderBy(e => e.RecordedAtUtc)
            .ThenBy(e => e.Id)
            .Take(batch)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToEnvelope).ToList();
    }

    private static PlatformEventEnvelope ToEnvelope(PlatformEventEntity e) =>
        new(
            new PlatformEventId(e.Id),
            e.Type,
            e.Source,
            e.SpecVersion,
            e.DataContentType,
            e.DataJson,
            e.OccurredAtUtc,
            e.RecordedAtUtc,
            e.Severity,
            e.Subject,
            e.DedupeKey,
            e.IntegrationId,
            e.ModuleId,
            e.ProcedureId,
            e.RunId,
            e.ScheduleId,
            e.ActorId,
            e.CorrelationId,
            e.CausationId);
}
