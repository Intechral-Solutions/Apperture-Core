namespace Apperture.RuntimePlane.Abstractions;

public interface IWorkflowStarter
{
    ValueTask<OperationRunId> StartAsync(StartWorkflowRequest request, CancellationToken cancellationToken = default);
}
