using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Domain.Integration;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Epic 68: persists schedule-expectation attention transitions (<c>expected_run_missing</c>) from shared evaluation snapshots.
/// Epic 69: returns transition outcome for incident-driven notifications.
/// </summary>
public interface IAttentionIncidentTracker
{
    ValueTask<IReadOnlySet<string>> GetIntegrationIdsWithActiveConditionAsync(
        string conditionKey,
        CancellationToken cancellationToken = default);

    ValueTask<AttentionIncidentObserveResult> ObserveScheduleExpectationAsync(
        IntegrationId integrationId,
        ScheduleExpectationSnapshot snapshot,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AttentionIncidentProjection>> ListRecentForIntegrationAsync(
        IntegrationId integrationId,
        string conditionKey,
        int maxCount,
        CancellationToken cancellationToken = default);

    ValueTask<AttentionIncidentProjection?> GetIncidentAsync(
        IntegrationId integrationId,
        string incidentId,
        CancellationToken cancellationToken = default);

    ValueTask<bool> AcknowledgeAsync(
        IntegrationId integrationId,
        string incidentId,
        string acknowledgedBy,
        string? note,
        DateTimeOffset acknowledgedAtUtc,
        CancellationToken cancellationToken = default);
}
