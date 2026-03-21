using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

public interface IWorkflowRetryService
{
    /// <summary>
    /// Requests a workflow retry as a new run. Returns null when the original run does not exist.
    /// Throws UnauthorizedAccessException when the caller is not authorized.
    /// Returns a result with <see cref="WorkflowRetryResult.IsBlocked"/> when the definition is not executable (e.g. module disabled).
    /// Throws InvalidOperationException when the original run is not eligible or replay context is missing.
    /// </summary>
    ValueTask<WorkflowRetryResult?> RequestRetryAsync(OperationRunId runId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a workflow retry request. When IsBlocked is true, no new run was created; deferred work was blocked due to lifecycle.
/// </summary>
public sealed record WorkflowRetryResult(
    OperationRunId OriginalRunId,
    OperationRunId? RunId,
    WorkflowInstanceId? WorkflowInstanceId,
    WorkflowDefinitionId WorkflowDefinitionId,
    string Status,
    bool IsBlocked = false,
    string? BlockedReason = null);

