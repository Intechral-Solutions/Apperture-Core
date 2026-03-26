using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Domain.Integration;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Projects integration readiness and operator detail from persisted facts (slots, bindings, connections, schedules).
/// </summary>
public interface IIntegrationReadinessService
{
    /// <summary>
    /// Returns readiness after enforcing the same integration read authorization as <see cref="IIntegrationManagementService.GetIntegrationAsync"/>.
    /// </summary>
    /// <returns>null if the integration does not exist.</returns>
    ValueTask<IntegrationReadinessProjection?> GetReadinessAsync(IntegrationId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Full operator view: integration metadata, readiness, required slots, bindings, and schedules.
    /// </summary>
    /// <returns>null if the integration does not exist.</returns>
    ValueTask<IntegrationOperatorDetailProjection?> GetOperatorDetailAsync(IntegrationId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fleet overview: visible integrations with readiness and attention flags. Epic 61.
    /// </summary>
    ValueTask<IReadOnlyList<IntegrationFleetRowProjection>> ListFleetAsync(
        IntegrationFleetQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Epic 68: same schedule expectation evaluation as fleet (integration read auth enforced).
    /// </summary>
    ValueTask<ScheduleExpectationSnapshot?> EvaluateScheduleExpectationAsync(
        IntegrationId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Epic 68: background incident observer — loads integration from persistence without per-row read auth.
    /// </summary>
    ValueTask<ScheduleExpectationSnapshot?> EvaluateScheduleExpectationForBackgroundAsync(
        IntegrationId id,
        CancellationToken cancellationToken = default);
}
