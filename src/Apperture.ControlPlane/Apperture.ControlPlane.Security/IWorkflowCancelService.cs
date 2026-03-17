using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

public interface IWorkflowCancelService
{
    /// <summary>
    /// Persists cancel intent for the given run id (idempotent). Returns false when the run does not exist.
    /// Throws UnauthorizedAccessException when the caller is not authorized to cancel.
    /// </summary>
    ValueTask<bool> RequestCancelAsync(OperationRunId runId, CancellationToken cancellationToken = default);
}

