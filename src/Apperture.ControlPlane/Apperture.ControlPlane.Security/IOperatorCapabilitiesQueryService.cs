using Apperture.ControlPlane.Security.Models;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Query service for operator UI capabilities based on current principal permissions. Epic 34.
/// </summary>
public interface IOperatorCapabilitiesQueryService
{
    ValueTask<OperatorCapabilitiesDto> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
}
