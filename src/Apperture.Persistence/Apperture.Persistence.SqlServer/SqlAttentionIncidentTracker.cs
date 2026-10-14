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
    public async ValueTask<AttentionIncidentObserveResult> ObserveScheduleExpectationAsync(
        IntegrationId integrationId,
        ScheduleExpectationSnapshot snapshot,
        DateTimeOffset observedAtUtc,
        string? resourceContextId = null,
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
                var id = NewIncidentId();
                _db.AttentionIncidents.Add(new AttentionIncidentEntity
                {
                    Id = id,
                    IntegrationId = integrationId.Value,
                    // CTX-2: snapshot at open only; never rewritten by later observation/resolution.
                    ResourceContextId = resourceContextId,
                    ConditionKey = conditionKey,
                    Status = AttentionIncidentStatuses.Active,
                    StartedAtUtc = observedAtUtc,
                    LastObservedAtUtc = observedAtUtc,
                    FirstExpectedFireAtUtc = snapshot.Evaluation.MostRecentExpectedFireUtc,
                    LastExpectedFireAtUtc = snapshot.Evaluation.MostRecentExpectedFireUtc,
                });
                try
                {
                    await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.Opened, id, resourceContextId);
                }
                catch (DbUpdateException)
                {
                    _db.ChangeTracker.Clear();
                    return await ObserveStillActiveAfterConcurrentOpenAsync(
                        integrationId,
                        conditionKey,
                        snapshot,
                        observedAtUtc,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            active.LastObservedAtUtc = observedAtUtc;
            if (snapshot.Evaluation.MostRecentExpectedFireUtc is { } fire)
            {
                active.LastExpectedFireAtUtc = fire;
                active.FirstExpectedFireAtUtc ??= fire;
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.ObservedStillActive, active.Id, active.ResourceContextId);
        }

        var toResolve = await _db.AttentionIncidents
            .FirstOrDefaultAsync(
                e => e.IntegrationId == integrationId.Value
                     && e.ConditionKey == conditionKey
                     && e.Status == AttentionIncidentStatuses.Active,
                cancellationToken)
            .ConfigureAwait(false);
        if (toResolve is null)
            return new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.NoChange, null);

        var resolvedId = toResolve.Id;
        toResolve.Status = AttentionIncidentStatuses.Resolved;
        toResolve.ResolvedAtUtc = observedAtUtc;
        toResolve.ResolutionReason = AttentionIncidentResolutionReasons.ExpectationCleared;
        toResolve.LastObservedAtUtc = observedAtUtc;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.Resolved, resolvedId, toResolve.ResourceContextId);
    }

    private async ValueTask<AttentionIncidentObserveResult> ObserveStillActiveAfterConcurrentOpenAsync(
        IntegrationId integrationId,
        string conditionKey,
        ScheduleExpectationSnapshot snapshot,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        var active = await _db.AttentionIncidents
            .FirstOrDefaultAsync(
                e => e.IntegrationId == integrationId.Value
                     && e.ConditionKey == conditionKey
                     && e.Status == AttentionIncidentStatuses.Active,
                cancellationToken)
            .ConfigureAwait(false);
        if (active is null)
            return new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.NoChange, null);
        active.LastObservedAtUtc = observedAtUtc;
        if (snapshot.Evaluation.MostRecentExpectedFireUtc is { } fire)
        {
            active.LastExpectedFireAtUtc = fire;
            active.FirstExpectedFireAtUtc ??= fire;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.ObservedStillActive, active.Id, active.ResourceContextId);
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
    public async ValueTask<AttentionIncidentProjection?> GetIncidentAsync(
        IntegrationId integrationId,
        string incidentId,
        CancellationToken cancellationToken = default)
    {
        var e = await _db.AttentionIncidents.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == incidentId && x.IntegrationId == integrationId.Value,
                cancellationToken)
            .ConfigureAwait(false);
        return e is null ? null : ToProjection(e);
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
            e.AcknowledgementNote,
            e.ResourceContextId);

    private static string NewIncidentId() => $"attn_{Guid.NewGuid():N}";
}
