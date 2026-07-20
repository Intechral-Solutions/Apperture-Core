using System.Text.Json;
using Apperture.Core.Application.OperationItems;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling;

public static class ApiPollingOperationItemization
{
    public const string OperationKind = "api_poll";
    public const string RetryOperationKind = "api_poll_retry";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string MapGroupStatus(SyncBatchStatus status) =>
        status switch
        {
            SyncBatchStatus.Succeeded => OperationGroupStatuses.Succeeded,
            SyncBatchStatus.CompletedWithErrors => OperationGroupStatuses.CompletedWithErrors,
            SyncBatchStatus.Failed => OperationGroupStatuses.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Running is not a terminal group status."),
        };

    public static bool IsRetryable(RecordClassification classification) =>
        classification.ChangeKind == ApiPollingChangeKinds.Failed;

    public static string BuildGroupSummaryJson(
        string snapshotId,
        string syncBatchId,
        bool resetState,
        bool strictMode,
        bool failOnRecordError,
        bool includeDeleted,
        int pageSize,
        int maxRecords,
        int newCount,
        int updatedCount,
        int unchangedCount,
        int deletedCount,
        int failedCount,
        int skippedCount) =>
        JsonSerializer.Serialize(
            new
            {
                snapshotId,
                syncBatchId,
                resetState,
                strictMode,
                failOnRecordError,
                includeDeleted,
                pageSize,
                maxRecords,
                newCount,
                updatedCount,
                unchangedCount,
                deletedCount,
                failedCount,
                skippedCount,
            },
            JsonOptions);

    public static string BuildGroupMetadataJson(string snapshotId, int recordCount, bool resetState) =>
        JsonSerializer.Serialize(new { snapshotId, recordCount, resetState }, JsonOptions);

    public static AppendOperationItemRequest ToAppendRequest(
        string operationGroupId,
        string runId,
        string snapshotId,
        bool includeDeleted,
        RecordClassification classification,
        string? retryAttemptId = null,
        string? parentOperationItemId = null)
    {
        var displayLabel = !string.IsNullOrWhiteSpace(classification.DisplayName)
            ? classification.DisplayName!.Trim()
            : classification.ExternalId ?? $"seq {classification.Sequence}";

        var changeSummaryJson = JsonSerializer.Serialize(
            new
            {
                changeKind = classification.ChangeKind,
                summary = classification.ChangeSummary,
                changedFields = classification.ChangedFields,
            },
            JsonOptions);

        object metadata = string.IsNullOrWhiteSpace(retryAttemptId)
            ? new
            {
                snapshotId,
                sequence = classification.Sequence,
                includeDeleted,
                changeKind = classification.ChangeKind,
                externalId = classification.ExternalId,
                version = classification.Version,
                status = classification.Status,
                tier = classification.Tier,
                errorCode = classification.ErrorCode,
            }
            : new
            {
                snapshotId,
                sequence = classification.Sequence,
                includeDeleted,
                changeKind = classification.ChangeKind,
                externalId = classification.ExternalId,
                version = classification.Version,
                status = classification.Status,
                tier = classification.Tier,
                errorCode = classification.ErrorCode,
                retryAttemptId,
                parentOperationItemId,
            };

        var metadataJson = JsonSerializer.Serialize(metadata, JsonOptions);

        return new AppendOperationItemRequest(
            OperationGroupId: operationGroupId,
            RunId: runId,
            Sequence: classification.Sequence,
            Status: classification.ItemStatus,
            SubjectKey: classification.ExternalId,
            DisplayLabel: displayLabel,
            ErrorCode: classification.ErrorCode,
            ErrorMessage: classification.ErrorMessage,
            IsRetryable: IsRetryable(classification),
            RetryCount: 0,
            LastAttemptedAtUtc: classification.CreatedAtUtc,
            CompletedAtUtc: classification.CreatedAtUtc,
            ChangeSummaryJson: changeSummaryJson,
            MetadataJson: metadataJson);
    }
}
