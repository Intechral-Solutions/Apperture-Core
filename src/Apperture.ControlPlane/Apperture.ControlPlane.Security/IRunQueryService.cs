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
    string? RetryOfRunId);

