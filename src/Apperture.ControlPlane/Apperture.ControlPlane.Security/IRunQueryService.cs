using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

public interface IRunQueryService
{
    ValueTask<IReadOnlyList<RunSummaryDto>> ListRunsAsync(RunQueryFilter? filter = null, CancellationToken cancellationToken = default);
    ValueTask<RunDetailDto?> GetRunAsync(OperationRunId id, CancellationToken cancellationToken = default);
}

public sealed record RunQueryFilter(
    string? ExecutionKind = null,
    string? OperationDefinitionId = null,
    string? Status = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    string? ModuleId = null,
    string? CorrelationKey = null);

public sealed record RunSummaryDto(
    string RunId,
    string ExecutionInstanceId,
    string ExecutionDefinitionId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? CorrelationKey,
    string? RetryOfRunId,
    string ExecutionKind = "workflow");

/// <param name="CurrentStepName">
/// Same field as <see cref="WorkflowInstanceEntity.CurrentStepName"/>: when <paramref name="Status"/> is <c>parked</c>, this is the next step to run after resume; when <c>running</c>, reflects current step progression.
/// </param>
public sealed record RunDetailDto(
    string RunId,
    string ExecutionInstanceId,
    string ExecutionDefinitionId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? CorrelationKey,
    string? RetryOfRunId,
    string? CurrentStepName,
    RunResultDto? Result,
    RunErrorDto? Error,
    IReadOnlyList<RunLifecycleEventDto> LifecycleEvents,
    IReadOnlyList<RunStepInstanceDto> StepInstances,
    IReadOnlyList<RunControlRequestDto> ControlRequests,
    string ExecutionKind = "workflow");

public sealed record RunResultDto(
    string Outcome,
    string Summary,
    string? PayloadJson,
    string? DiagnosticsJson);

public sealed record RunErrorDto(
    string Category,
    string Message,
    string? Code,
    string? DetailsJson);

public sealed record RunLifecycleEventDto(
    int Id,
    string EventType,
    string? FromStatus,
    string? ToStatus,
    DateTimeOffset OccurredAt,
    string? RelatedRunId,
    string? StepName,
    string? DetailsJson);

public sealed record RunStepInstanceDto(
    string StepName,
    int Attempt,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    string? Error);

public sealed record RunControlRequestDto(
    long Id,
    string ControlKind,
    string Status,
    DateTimeOffset RequestedAt,
    string? RequestedBy,
    DateTimeOffset? AppliedAt,
    string? AppliedBy,
    string? LastError);

