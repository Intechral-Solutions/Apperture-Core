using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Integration;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Epic 67: schedule diagnostics and expected-fire evidence timeline.
/// </summary>
public interface IIntegrationScheduleDiagnosticsService
{
    ValueTask<IntegrationScheduleDiagnosticsProjection?> GetDiagnosticsAsync(
        IntegrationId integrationId,
        CancellationToken cancellationToken = default);
}

