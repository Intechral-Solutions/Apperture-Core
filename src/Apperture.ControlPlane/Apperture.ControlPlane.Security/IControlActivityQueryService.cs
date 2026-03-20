using Apperture.Core.Application.ControlFacade;

namespace Apperture.ControlPlane.Security;

public interface IControlActivityQueryService
{
    /// <summary>
    /// Unified recent operator activity across executions and durable runtime control requests.
    /// </summary>
    ValueTask<IReadOnlyList<ControlActivitySummaryDto>> ListRecentActivityAsync(
        DateTimeOffset createdFromUtc,
        int take,
        CancellationToken cancellationToken = default);
}

