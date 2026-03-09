using Apperture.Core.Domain.Operations;
using Apperture.Shared.Kernel;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Boundary to start an operation. Delegates to IWorkflowStarter or module executor. Bridges control plane to runtime; no step execution.
/// </summary>
public interface IExecutionDispatchService
{
    /// <summary>
    /// Starts an operation and returns the execution id.
    /// </summary>
    ValueTask<ExecutionId> StartAsync(
        OperationId operationId,
        IReadOnlyDictionary<string, object?>? parameters,
        ActorId actorId,
        CorrelationId correlationId,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);
}
