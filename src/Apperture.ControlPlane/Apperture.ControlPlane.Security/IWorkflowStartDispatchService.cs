using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

public interface IWorkflowStartDispatchService
{
    ValueTask<WorkflowStartDispatchResult?> StartAsync(StartWorkflowRequest request, CancellationToken cancellationToken = default);
}

public sealed record WorkflowStartDispatchResult(
    OperationRunId RunId,
    WorkflowInstanceId WorkflowInstanceId,
    WorkflowDefinitionId WorkflowDefinitionId,
    string Status);

