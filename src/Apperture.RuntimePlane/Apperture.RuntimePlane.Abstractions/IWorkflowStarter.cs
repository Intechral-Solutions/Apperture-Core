namespace Apperture.RuntimePlane.Abstractions;

/// <summary>
/// Portable contract to start a workflow as an operation run.
/// </summary>
/// <remarks>
/// This interface is not on the current production enqueue path. API and schedule-driven starts persist work through
/// <c>IWorkflowStartDispatchService</c> / <c>IOperationStartDispatchService</c> (see <c>Apperture.Core.Application</c> and Epic 73 ADR).
/// </remarks>
public interface IWorkflowStarter
{
    ValueTask<OperationRunId> StartAsync(StartWorkflowRequest request, CancellationToken cancellationToken = default);
}
