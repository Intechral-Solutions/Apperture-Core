using Apperture.RuntimePlane.Abstractions;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Persists a durable workflow start. Inside an audited unit of work (a transaction already open on the request's context)
/// it joins that transaction and the unit owns the commit, so the start request and its audit row commit together;
/// otherwise it opens and commits its own.
/// </summary>
public interface IWorkflowStartDispatchService
{
    ValueTask<WorkflowStartDispatchResult?> StartAsync(StartWorkflowRequest request, CancellationToken cancellationToken = default);
}

/// <param name="IsReplay">
/// True when the request's idempotency key already named a start and nothing new was persisted. The run is the earlier one.
/// </param>
/// <param name="ResourceContextId">The ResourceContext snapshot the run was created with, when it has one.</param>
public sealed record WorkflowStartDispatchResult(
    OperationRunId RunId,
    WorkflowInstanceId WorkflowInstanceId,
    WorkflowDefinitionId WorkflowDefinitionId,
    string Status,
    bool IsReplay = false,
    string? ResourceContextId = null);
