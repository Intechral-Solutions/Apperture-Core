using Apperture.Core.Application.ControlFacade;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

public interface IRunActionAvailabilityQueryService
{
    Task<IReadOnlyList<ControlActionAvailabilityDto>?> GetRunActionsAsync(
        OperationRunId runId,
        CancellationToken cancellationToken = default);
}
