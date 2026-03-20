using Apperture.RuntimePlane.Abstractions;

namespace Apperture.Management.Client;

public interface IManagementClient
{
    Task<IReadOnlyList<ModuleDto>> GetModulesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowDto>> GetWorkflowsAsync(string moduleId, CancellationToken cancellationToken = default);
    Task<StartWorkflowResult> StartWorkflowAsync(
        string workflowId,
        IReadOnlyDictionary<string, object?>? parameters,
        string? idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RunSummaryDto>> GetRunsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RunSummaryDto>> GetRunsAsync(RunQueryFilter? filter, CancellationToken cancellationToken = default);
    Task<RunDetailDto?> GetRunAsync(string runId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ControlActionAvailabilityDto>> GetRunActionsAsync(string runId, CancellationToken cancellationToken = default);
    Task<RunTimelineDto?> GetRunTimelineAsync(string runId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationDefinitionDto>> GetOperationsAsync(CancellationToken cancellationToken = default);
    Task<OperationDefinitionDto?> GetOperationAsync(string operationDefinitionId, CancellationToken cancellationToken = default);
    Task<StartOperationResult> StartOperationAsync(
        string operationDefinitionId,
        IReadOnlyDictionary<string, object?>? parameters,
        string? idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default);

    Task<CancelRunResult> CancelRunAsync(string runId, CancellationToken cancellationToken = default);
    Task<RetryRunResult> RetryRunAsync(string runId, CancellationToken cancellationToken = default);
    Task<ExternalControlResult> ParkRunAsync(string runId, CancellationToken cancellationToken = default);
    Task<ExternalControlResult> ResumeRunAsync(string runId, CancellationToken cancellationToken = default);

    Task<ControlCenterDto> GetControlCenterAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RuntimeControlRequestDto>> GetRuntimeShutdownRequestsAsync(
        string runtimeId,
        CancellationToken cancellationToken = default);
}

public sealed record StartWorkflowResult(
    string RunId,
    string WorkflowInstanceId,
    string WorkflowDefinitionId,
    string Status);

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

public sealed record RunQueryFilter(
    string? ExecutionKind = null,
    string? OperationDefinitionId = null,
    string? Status = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    string? ModuleId = null,
    string? CorrelationKey = null);

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

public sealed record RunTimelineDto(
    string RunId,
    string ExecutionKind,
    IReadOnlyList<RunTimelineEventDto> Events);

public sealed record RunTimelineEventDto(
    string EventId,
    string RunId,
    DateTimeOffset OccurredAt,
    string EventKind,
    string Summary,
    string? Actor,
    string SourceType,
    string? SourceId,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record CancelRunResult(string RunId, string Status);

public sealed record StartOperationResult(
    string RunId,
    string OperationInstanceId,
    string OperationDefinitionId,
    string Status);

public sealed record OperationDefinitionDto(
    string OperationDefinitionId,
    string? ModuleId,
    string InvariantName,
    string DisplayName,
    string? Description,
    string Category,
    bool CanStart,
    OperationInputSchema? InputSchema);

public sealed record OperationInputSchema(IReadOnlyList<OperationInputFieldDto> Fields);

public sealed record OperationInputFieldDto(
    string Key,
    string Type,
    string Label,
    string? HelpText,
    bool Required,
    string? DefaultValue,
    int Order,
    IReadOnlyList<string>? AllowedValues,
    decimal? Min,
    decimal? Max,
    int? MaxLength);

public sealed record RetryRunResult(
    string OriginalRunId,
    string RunId,
    string WorkflowInstanceId,
    string WorkflowDefinitionId,
    string Status,
    string RetryOfRunId);

public sealed record ExternalControlResult(
    string RunId,
    string WorkflowInstanceId,
    string WorkflowDefinitionId,
    string ControlKind,
    string Status,
    string RunStatus,
    string? CurrentStepName);

public sealed record ControlActionAvailabilityDto(
    string ActionKind,
    string AvailabilityState,
    string? ReasonCode);

public sealed record ControlTargetSummaryDto(
    string TargetKind,
    string TargetId,
    string DisplayName,
    string? GroupKey,
    string? DetailLink,
    IReadOnlyList<ControlActionAvailabilityDto> AvailableActions);

public sealed record ControlActivitySummaryDto(
    string ActivityKind,
    string ActivityId,
    string TargetKind,
    string TargetId,
    string RequestedAtIso,
    string? CompletedAtIso,
    string CurrentNormalizedStatus,
    string Outcome,
    string OutcomeSummary,
    string ActivitySource,
    string DetailLink,
    IReadOnlyList<ControlActionAvailabilityDto> AvailableActions);

public sealed record ControlAttentionItemDto(
    string AttentionKind,
    string Severity,
    string TargetKind,
    string TargetId,
    string Summary,
    string CreatedAtIso,
    string? RelatedActivityId);

public sealed record ControlCenterDto(
    IReadOnlyList<ControlTargetSummaryDto> Targets,
    IReadOnlyList<ControlActivitySummaryDto> RecentActivity,
    IReadOnlyList<ControlAttentionItemDto> AttentionItems);

public sealed record RuntimeControlRequestDto(
    long Id,
    string RuntimeId,
    string ControlKind,
    string Status,
    DateTimeOffset RequestedAt,
    string? RequestedBy,
    DateTimeOffset? AppliedAt,
    string? AppliedBy,
    string? LastError);
