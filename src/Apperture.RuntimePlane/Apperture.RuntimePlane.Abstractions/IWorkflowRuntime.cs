namespace Apperture.RuntimePlane.Abstractions;

/// <summary>
/// Workflow runtime contract. Implementations must enforce orchestration-safe parameters and payloads (no ResolvedSecret or SecretMaterial in state/history).
/// </summary>
public interface IWorkflowRuntime
{
    ValueTask<WorkflowInstanceId> StartAsync(StartWorkflowRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a signal to a workflow instance. Implementations must call OrchestrationSafeValidator.ValidateSignalPayload(payload)
    /// before persisting or forwarding the payload to orchestration state/history.
    /// </summary>
    ValueTask SignalAsync(WorkflowInstanceId instanceId, string signalName, object? payload, CancellationToken cancellationToken = default);

    ValueTask CancelAsync(WorkflowInstanceId instanceId, CancellationToken cancellationToken = default);
    ValueTask<WorkflowInstance?> GetInstanceAsync(WorkflowInstanceId id, CancellationToken cancellationToken = default);
}
