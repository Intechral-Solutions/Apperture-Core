using Apperture.Core.Application.ControlFacade;

namespace Apperture.ControlPlane.Security;

public interface IControlCenterQueryService
{
    ValueTask<ControlCenterDto> GetControlCenterAsync(
        DateTimeOffset createdFromUtc,
        int takeRecentActivity,
        int takeAttention,
        string? resourceContextId = null,
        CancellationToken cancellationToken = default);
}

