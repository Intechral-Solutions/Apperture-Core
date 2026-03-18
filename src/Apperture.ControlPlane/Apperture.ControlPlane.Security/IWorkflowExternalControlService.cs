using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

public interface IWorkflowExternalControlService
{
    ValueTask<WorkflowExternalControlResult?> RequestParkAsync(OperationRunId runId, CancellationToken cancellationToken = default);
    ValueTask<WorkflowExternalControlResult?> RequestResumeAsync(OperationRunId runId, CancellationToken cancellationToken = default);
}

public sealed record WorkflowExternalControlResult(
    OperationRunId RunId,
    WorkflowInstanceId WorkflowInstanceId,
    WorkflowDefinitionId WorkflowDefinitionId,
    string ControlKind,
    string Status,
    string RunStatus,
    string? CurrentStepName);
