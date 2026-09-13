namespace Apperture.RuntimePlane.Abstractions;

/// <summary>
/// Workflow runtime contract. Implementations must enforce orchestration-safe parameters and payloads (no ResolvedSecret or SecretMaterial in state/history).
/// </summary>
/// <remarks>
/// Future-facing adapter for DTFx / Azure Durable Functions-style orchestration. Today’s worker execution path uses SQL-backed start requests and
/// <see cref="IWorkflowExecutor"/> inline step execution instead of this contract. See Epic 73 ADR for host roles and hot path.
/// </remarks>
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
