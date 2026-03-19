using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

public interface IExecutionCancelService
{
    /// <summary>
    /// Requests cooperative cancel for a run (workflow or operation).
    /// Returns <c>false</c> when the run id does not exist or is not recognized.
    /// Throws <see cref="UnauthorizedAccessException"/> when unauthorized.
    /// </summary>
    ValueTask<bool> RequestCancelAsync(OperationRunId runId, CancellationToken cancellationToken = default);
}

