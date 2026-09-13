namespace Apperture.Core.Application.PlatformEvents;

/// <summary>Terminal run facts for platform-owned lifecycle platform events (Epic 103D).</summary>
public sealed record ExecutionLifecycleTerminalContext(
    string RunId,
    string TerminalStatus,
    string ExecutionKind,
    string DefinitionId,
    string? IntegrationId,
    string? ModuleId,
    string? ProcedureId,
    string? ScheduleId,
    string? CorrelationId,
    string? CausationId,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int? StepCount = null,
    string? FailedStepName = null,
    string? FailureSummary = null,
    string? FailureCode = null,
    string? ExceptionType = null,
    /// <summary>CTX-2: immutable ResourceContext snapshot read off the OperationRun row.</summary>
    string? ResourceContextId = null);
