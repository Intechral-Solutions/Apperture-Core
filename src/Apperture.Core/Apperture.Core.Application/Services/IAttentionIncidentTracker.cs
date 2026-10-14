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

    /// <param name="resourceContextId">
    /// CTX-2: the Integration's <b>current</b> ResourceContext, supplied by the caller (who already
    /// has the Integration loaded). Used only to snapshot onto a newly opened incident; ignored
    /// otherwise. Never queried inside the tracker.
    /// </param>
    ValueTask<AttentionIncidentObserveResult> ObserveScheduleExpectationAsync(
        IntegrationId integrationId,
        ScheduleExpectationSnapshot snapshot,
        DateTimeOffset observedAtUtc,
        string? resourceContextId = null,
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

    // SEC-2.1: there is deliberately no acknowledgement here. Acknowledging an incident is an operator action, an audited unit of
    // work with its own authorization (AttentionIncidentAcknowledgementService); this tracker opens, observes and resolves incidents
    // and reads them, and never writes who acknowledged one.
}
