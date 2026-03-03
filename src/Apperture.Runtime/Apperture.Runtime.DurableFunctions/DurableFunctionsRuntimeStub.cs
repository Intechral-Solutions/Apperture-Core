using Apperture.Runtime.Abstractions;
using Apperture.Shared.Kernel;

namespace Apperture.Runtime.DurableFunctions;

/// <summary>
/// Stub for Azure Durable Functions adapter. Full implementation deferred until cloud phase.
/// </summary>
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
