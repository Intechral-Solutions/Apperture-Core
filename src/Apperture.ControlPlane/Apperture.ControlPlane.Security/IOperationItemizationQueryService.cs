using Apperture.Core.Application.OperationItems;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>SDK-3B read model for durable operation groups/items attached to a run.</summary>
public interface IOperationItemizationQueryService
{
    /// <summary>Lists operation groups for a run. Returns null when the run does not exist.</summary>
    Task<IReadOnlyList<OperationGroupSummaryDto>?> ListGroupsByRunAsync(
        OperationRunId runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Paged items for a group under a run. Returns null when the run or group is missing / mismatched.
    /// </summary>
    Task<OperationItemPageDto?> ListItemsByGroupAsync(
        OperationRunId runId,
        string operationGroupId,
        OperationItemQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Lightweight aggregates for run detail. Null when the run does not exist.</summary>
    Task<RunOperationItemizationSummaryDto?> GetRunSummaryAsync(
        OperationRunId runId,
        CancellationToken cancellationToken = default);
}

public sealed record OperationGroupSummaryDto(
    string OperationGroupId,
    string RunId,
    string? IntegrationId,
    string ModuleId,
    string? ProcedureId,
    string OperationKind,
    string? SourceLabel,
    string Status,
    int TotalCount,
    int SucceededCount,
    int FailedCount,
    int SkippedCount,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? SummaryJson,
    string? MetadataJson);

public sealed record OperationItemSummaryDto(
    string OperationItemId,
    string OperationGroupId,
    string RunId,
    int Sequence,
    string? SubjectKey,
    string? DisplayLabel,
    string Status,
    string? ErrorCode,
    string? ErrorMessage,
    bool IsRetryable,
    int RetryCount,
    DateTimeOffset? LastAttemptedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ChangeSummaryJson,
    string? MetadataJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record OperationItemPageDto(
    IReadOnlyList<OperationItemSummaryDto> Items,
    int TotalCount,
    int Skip,
    int Take);

public sealed record RunOperationItemizationSummaryDto(
    int GroupCount,
    int FailedItemCount,
    int SkippedItemCount,
    bool HasOperationItems);
