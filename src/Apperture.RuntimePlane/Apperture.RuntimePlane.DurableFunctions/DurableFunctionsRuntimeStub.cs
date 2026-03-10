using Apperture.RuntimePlane.Abstractions;

namespace Apperture.RuntimePlane.DurableFunctions;

/// <summary>
/// Stub for Azure Durable Functions adapter. Full implementation deferred until cloud phase.
/// </summary>
/// <remarks>
/// When implemented: orchestrator state and history may contain only SecretRef (e.g. kv:name or Key Vault URI), never ResolvedSecret or secret material.
/// Activities must resolve secrets just-in-time via ISecretResolver and return only non-secret outputs.
/// </remarks>
public sealed class DurableFunctionsRuntimeStub : IWorkflowRuntime
{
    public ValueTask<WorkflowInstanceId> StartAsync(StartWorkflowRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Durable Functions adapter not implemented.");

    public ValueTask SignalAsync(WorkflowInstanceId instanceId, string signalName, object? payload, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Durable Functions adapter not implemented.");

    public ValueTask CancelAsync(WorkflowInstanceId instanceId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Durable Functions adapter not implemented.");

    public ValueTask<WorkflowInstance?> GetInstanceAsync(WorkflowInstanceId id, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Durable Functions adapter not implemented.");
}
