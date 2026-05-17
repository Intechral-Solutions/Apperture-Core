using Apperture.Core.Application.PlatformEvents;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlPlatformEventStore : IPlatformEventStore
{
    private readonly AppertureDbContext _db;

    public SqlPlatformEventStore(AppertureDbContext db) => _db = db;

    public async ValueTask<PlatformEventEnvelope> AppendAsync(
        PlatformEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        _db.PlatformEvents.Add(ToEntity(envelope));
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsDedupeViolation(ex))
        {
            var existing = await _db.PlatformEvents.AsNoTracking()
                .FirstOrDefaultAsync(
                    e => e.Source == envelope.Source
                         && e.Type == envelope.Type
                         && e.DedupeKey == envelope.DedupeKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
                return ToEnvelope(existing);
            throw;
        }

        return envelope;
    }

    public async ValueTask<PlatformEventQueryPage> QueryAsync(
        PlatformEventQuery query,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(query.Take, 1, 200);
        var q = _db.PlatformEvents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.IntegrationId))
            q = q.Where(e => e.IntegrationId == query.IntegrationId);
        if (!string.IsNullOrWhiteSpace(query.EventType))
            q = q.Where(e => e.Type == query.EventType);
        if (!string.IsNullOrWhiteSpace(query.Severity))
            q = q.Where(e => e.Severity == query.Severity);
        if (query.FromOccurredAtUtc is { } from)
            q = q.Where(e => e.OccurredAtUtc >= from);
        if (query.ToOccurredAtUtc is { } to)
            q = q.Where(e => e.OccurredAtUtc <= to);
        if (!string.IsNullOrWhiteSpace(query.RunId))
            q = q.Where(e => e.RunId == query.RunId);
        if (!string.IsNullOrWhiteSpace(query.ModuleId))
            q = q.Where(e => e.ModuleId == query.ModuleId);

        var rows = await q
            .OrderByDescending(e => e.OccurredAtUtc)
            .ThenByDescending(e => e.RecordedAtUtc)
            .Take(take + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = rows.Count > take;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        return new PlatformEventQueryPage(rows.Select(ToEnvelope).ToList(), hasMore);
    }

    private static PlatformEventEntity ToEntity(PlatformEventEnvelope e) =>
        new()
        {
            Id = e.Id.Value,
            Type = e.Type,
            Source = e.Source,
            SpecVersion = e.SpecVersion,
            DataContentType = e.DataContentType,
            DataJson = e.DataJson,
            OccurredAtUtc = e.OccurredAtUtc,
            RecordedAtUtc = e.RecordedAtUtc,
            Severity = e.Severity,
            Subject = e.Subject,
            DedupeKey = e.DedupeKey,
            IntegrationId = e.IntegrationId,
            ModuleId = e.ModuleId,
            ProcedureId = e.ProcedureId,
            RunId = e.RunId,
            ScheduleId = e.ScheduleId,
            ActorId = e.ActorId,
            CorrelationId = e.CorrelationId,
            CausationId = e.CausationId,
        };

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

    private static bool IsDedupeViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("IX_PlatformEvents_Source_Type_DedupeKey", StringComparison.OrdinalIgnoreCase) == true
        || ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true;
}
