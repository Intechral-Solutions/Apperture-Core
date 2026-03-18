using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

public interface IWorkflowRetryService
{
    /// <summary>
    /// Requests a workflow retry as a new run. Returns null when the original run does not exist.
    /// Throws UnauthorizedAccessException when the caller is not authorized.
    /// Throws InvalidOperationException when the original run is not eligible or replay context is missing.
    /// </summary>
    ValueTask<WorkflowRetryResult?> RequestRetryAsync(OperationRunId runId, CancellationToken cancellationToken = default);
}

public sealed record WorkflowRetryResult(
    OperationRunId OriginalRunId,
    OperationRunId RunId,
    WorkflowInstanceId WorkflowInstanceId,
    WorkflowDefinitionId WorkflowDefinitionId,
    string Status);

