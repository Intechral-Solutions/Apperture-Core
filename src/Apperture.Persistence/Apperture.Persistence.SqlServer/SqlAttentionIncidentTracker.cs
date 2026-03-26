using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <inheritdoc />
public sealed class SqlAttentionIncidentTracker : IAttentionIncidentTracker
{
    private readonly AppertureDbContext _db;

    public SqlAttentionIncidentTracker(AppertureDbContext db) => _db = db;

    /// <inheritdoc />
    public async ValueTask<IReadOnlySet<string>> GetIntegrationIdsWithActiveConditionAsync(
        string conditionKey,
        CancellationToken cancellationToken = default)
    {
        var ids = await _db.AttentionIncidents.AsNoTracking()
            .Where(e => e.ConditionKey == conditionKey && e.Status == AttentionIncidentStatuses.Active)
            .Select(e => e.IntegrationId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new HashSet<string>(ids, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async ValueTask ObserveScheduleExpectationAsync(
        IntegrationId integrationId,
        ScheduleExpectationSnapshot snapshot,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var conditionKey = AttentionConditionKeys.ExpectedRunMissing;
        if (snapshot.Evaluation.Status == ScheduleExpectationEvaluationStatus.MissingExpectedRun)
        {
            var active = await _db.AttentionIncidents
                .FirstOrDefaultAsync(
                    e => e.IntegrationId == integrationId.Value
                         && e.ConditionKey == conditionKey
                         && e.Status == AttentionIncidentStatuses.Active,
                    cancellationToken)
                .ConfigureAwait(false);
            if (active is null)
            {
                _db.AttentionIncidents.Add(new AttentionIncidentEntity
                {
                    Id = NewIncidentId(),
                    IntegrationId = integrationId.Value,
                    ConditionKey = conditionKey,
                    Status = AttentionIncidentStatuses.Active,
                    StartedAtUtc = observedAtUtc,
                    LastObservedAtUtc = observedAtUtc,
                    FirstExpectedFireAtUtc = snapshot.Evaluation.MostRecentExpectedFireUtc,
                    LastExpectedFireAtUtc = snapshot.Evaluation.MostRecentExpectedFireUtc,
                });
            }
            else
            {
                active.LastObservedAtUtc = observedAtUtc;
                if (snapshot.Evaluation.MostRecentExpectedFireUtc is { } fire)
                {
                    active.LastExpectedFireAtUtc = fire;
                    active.FirstExpectedFireAtUtc ??= fire;
                }
            }
        }
        else
        {
            var active = await _db.AttentionIncidents
                .FirstOrDefaultAsync(
                    e => e.IntegrationId == integrationId.Value
                         && e.ConditionKey == conditionKey
                         && e.Status == AttentionIncidentStatuses.Active,
                    cancellationToken)
                .ConfigureAwait(false);
            if (active is not null)
            {
                active.Status = AttentionIncidentStatuses.Resolved;
                active.ResolvedAtUtc = observedAtUtc;
                active.ResolutionReason = AttentionIncidentResolutionReasons.ExpectationCleared;
                active.LastObservedAtUtc = observedAtUtc;
            }
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Concurrent open: unique filtered index on active row — retry as update.
            _db.ChangeTracker.Clear();
            if (snapshot.Evaluation.Status != ScheduleExpectationEvaluationStatus.MissingExpectedRun)
                return;
            var active = await _db.AttentionIncidents
                .FirstOrDefaultAsync(
                    e => e.IntegrationId == integrationId.Value
                         && e.ConditionKey == conditionKey
                         && e.Status == AttentionIncidentStatuses.Active,
                    cancellationToken)
                .ConfigureAwait(false);
            if (active is null)
                return;
            active.LastObservedAtUtc = observedAtUtc;
            if (snapshot.Evaluation.MostRecentExpectedFireUtc is { } fire)
            {
                active.LastExpectedFireAtUtc = fire;
                active.FirstExpectedFireAtUtc ??= fire;
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AttentionIncidentProjection>> ListRecentForIntegrationAsync(
        IntegrationId integrationId,
        string conditionKey,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.AttentionIncidents.AsNoTracking()
            .Where(e => e.IntegrationId == integrationId.Value && e.ConditionKey == conditionKey)
            .OrderByDescending(e => e.StartedAtUtc)
            .Take(maxCount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(ToProjection).ToList();
    }

    /// <inheritdoc />
    public async ValueTask<bool> AcknowledgeAsync(
        IntegrationId integrationId,
        string incidentId,
        string acknowledgedBy,
        string? note,
        DateTimeOffset acknowledgedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.AttentionIncidents
            .FirstOrDefaultAsync(
                e => e.Id == incidentId && e.IntegrationId == integrationId.Value,
                cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
            return false;
        entity.AcknowledgedAtUtc = acknowledgedAtUtc;
        entity.AcknowledgedBy = acknowledgedBy;
        entity.AcknowledgementNote = note;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static AttentionIncidentProjection ToProjection(AttentionIncidentEntity e) =>
        new(
            e.Id,
            e.IntegrationId,
            e.ConditionKey,
            e.Status,
            e.StartedAtUtc,
            e.LastObservedAtUtc,
            e.ResolvedAtUtc,
            e.ResolutionReason,
            e.FirstExpectedFireAtUtc,
            e.LastExpectedFireAtUtc,
            e.AcknowledgedAtUtc,
            e.AcknowledgedBy,
            e.AcknowledgementNote);

    private static string NewIncidentId() => $"attn_{Guid.NewGuid():N}";
}
