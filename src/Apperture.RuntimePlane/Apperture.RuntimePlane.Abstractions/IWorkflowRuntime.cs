using Apperture.Shared.Kernel;

namespace Apperture.RuntimePlane.Abstractions;

public interface IWorkflowRuntime
{
    ValueTask<WorkflowInstanceId> StartAsync(StartWorkflowRequest request, CancellationToken cancellationToken = default);
    ValueTask SignalAsync(WorkflowInstanceId instanceId, string signalName, object? payload, CancellationToken cancellationToken = default);
    ValueTask CancelAsync(WorkflowInstanceId instanceId, CancellationToken cancellationToken = default);
    ValueTask<WorkflowInstance?> GetInstanceAsync(WorkflowInstanceId id, CancellationToken cancellationToken = default);
}
