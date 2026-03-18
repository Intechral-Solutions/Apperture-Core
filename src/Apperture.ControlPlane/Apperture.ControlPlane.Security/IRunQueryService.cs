using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

public interface IRunQueryService
{
    ValueTask<IReadOnlyList<RunSummaryDto>> ListRunsAsync(CancellationToken cancellationToken = default);
    ValueTask<RunDetailDto?> GetRunAsync(OperationRunId id, CancellationToken cancellationToken = default);
}

public sealed record RunSummaryDto(
    string RunId,
    string WorkflowInstanceId,
    string WorkflowDefinitionId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? CorrelationKey,
    string? RetryOfRunId);

public sealed record RunDetailDto(
    string RunId,
    string WorkflowInstanceId,
    string WorkflowDefinitionId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? CorrelationKey,
    string? RetryOfRunId,
    IReadOnlyList<RunLifecycleEventDto> LifecycleEvents,
    IReadOnlyList<RunStepInstanceDto> StepInstances);

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

