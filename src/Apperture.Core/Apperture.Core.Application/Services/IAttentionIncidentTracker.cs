using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Domain.Integration;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Epic 68: persists schedule-expectation attention transitions (<c>expected_run_missing</c>) from shared evaluation snapshots.
/// </summary>
public interface IAttentionIncidentTracker
{
    /// <summary>
    /// Integration ids that currently have an <see cref="AttentionIncidentStatuses.Active"/> row for <paramref name="conditionKey"/>.
    /// </summary>
    ValueTask<IReadOnlySet<string>> GetIntegrationIdsWithActiveConditionAsync(
        string conditionKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens, updates, or resolves the <c>expected_run_missing</c> incident from <paramref name="snapshot"/>.
    /// </summary>
    ValueTask ObserveScheduleExpectationAsync(
        IntegrationId integrationId,
        ScheduleExpectationSnapshot snapshot,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bounded history for diagnostics (newest first by start time).
    /// </summary>
    ValueTask<IReadOnlyList<AttentionIncidentProjection>> ListRecentForIntegrationAsync(
        IntegrationId integrationId,
        string conditionKey,
        int maxCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records operator acknowledgement on an incident belonging to the integration.
    /// </summary>
    /// <returns>false if the incident was not found or does not belong to the integration.</returns>
    ValueTask<bool> AcknowledgeAsync(
        IntegrationId integrationId,
        string incidentId,
        string acknowledgedBy,
        string? note,
        DateTimeOffset acknowledgedAtUtc,
        CancellationToken cancellationToken = default);
}
