using Apperture.Shared.Kernel;

namespace Apperture.Runtime.Abstractions;

public interface IWorkflowStarter
{
    ValueTask<OperationRunId> StartAsync(StartWorkflowRequest request, CancellationToken cancellationToken = default);
}
