using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Integration;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Evaluates integration/procedure readiness for scheduled dispatch and other background paths (no per-row auth).
/// </summary>
public interface IIntegrationDispatchReadinessEvaluator
{
    /// <summary>
    /// Loads persisted integration facts and evaluates readiness. Uses procedure requirement subset when <paramref name="procedureId"/> is set.
    /// </summary>
    /// <returns>null when the integration does not exist.</returns>
    ValueTask<IntegrationReadinessProjection?> EvaluateForDispatchAsync(
        IntegrationId integrationId,
        string? procedureId,
        CancellationToken cancellationToken = default);
}
