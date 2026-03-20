using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

public static class RunTimeline
{
    public const string EventKindRunRequested = "runRequested";
    public const string EventKindRunClaimed = "runClaimed";
    public const string EventKindLifecycleTransition = "lifecycleTransition";
    public const string EventKindExternalControlRequested = "externalControlRequested";
    public const string EventKindExternalControlApplied = "externalControlApplied";
    public const string EventKindExternalControlRejected = "externalControlRejected";
    public const string EventKindRunCompletedBoundary = "runCompletedBoundary";
    public const string EventKindRetryLinked = "retryLinked";

    public const string SourceTypeRun = "run";
    public const string SourceTypeStartRequest = "startRequest";
    public const string SourceTypeLifecycleEvent = "lifecycleEvent";
    public const string SourceTypeExternalControlRequest = "externalControlRequest";
}

public interface IRunTimelineQueryService
{
    Task<RunTimelineDto?> GetRunTimelineAsync(OperationRunId runId, CancellationToken cancellationToken = default);
}

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
