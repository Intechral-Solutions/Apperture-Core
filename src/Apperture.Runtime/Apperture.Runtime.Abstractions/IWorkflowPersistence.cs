using Apperture.Shared.Kernel;

namespace Apperture.Runtime.Abstractions;

public interface IWorkflowPersistence
{
    ValueTask<WorkflowInstance?> GetInstanceAsync(WorkflowInstanceId id, CancellationToken cancellationToken = default);
    ValueTask SaveInstanceAsync(WorkflowInstance instance, CancellationToken cancellationToken = default);
    ValueTask<OperationRun?> GetRunAsync(OperationRunId id, CancellationToken cancellationToken = default);
    ValueTask SaveRunAsync(OperationRun run, CancellationToken cancellationToken = default);
}
