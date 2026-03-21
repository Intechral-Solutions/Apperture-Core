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
    string? StartIdempotencyKey,
    string? StartCorrelationId,
    string? StartParametersJson,
    DateTimeOffset? StartRequestedAt,
    string? StartClaimedBy,
    DateTimeOffset? StartClaimedAt,
    string ExecutionKind = "workflow");

/// <summary>
/// Terminal run result projection for <c>GET /runs/{id}</c>. <see cref="Outcome"/> is derived from lifecycle status.
/// <see cref="Summary"/> uses persisted captured-output summary when the run succeeded and that summary is non-empty; otherwise a standard phrase for the outcome.
/// <see cref="OutputDisplayPolicy"/> is set when the handler used <c>OperatorRunOutput</c> (ADR-0020); null means legacy Epic-20 plain return (implicit inline).
/// Failure diagnostics are exposed separately via <see cref="DiagnosticsAvailability"/>/<see cref="DiagnosticsDisplayPolicy"/> and optional inline <see cref="DiagnosticsJson"/>.
/// </summary>
public sealed record RunResultDto(
    string Outcome,
    string Summary,
    string? PayloadJson,
    string? DiagnosticsJson,
    string? OutputAvailability = null,
    string? OutputDisplayPolicy = null,
    string? DiagnosticsAvailability = null,
    string? DiagnosticsDisplayPolicy = null);

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

