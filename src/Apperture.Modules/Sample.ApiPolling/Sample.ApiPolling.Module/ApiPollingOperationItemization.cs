using Apperture.Core.Application.OperationItems;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling;

public static class ApiPollingOperationItemization
{
    public const string OperationKind = "api_poll";
    public const string RetryOperationKind = "api_poll_retry";

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
        OperationItemReporting.SerializeMetadata(new
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
        });

    public static string BuildGroupMetadataJson(string snapshotId, int recordCount, bool resetState) =>
        OperationItemReporting.SerializeMetadata(new { snapshotId, recordCount, resetState });

    public static OperationGroupAggregator AggregateClassifications(IReadOnlyList<RecordClassification> classifications)
    {
        var aggregator = new OperationGroupAggregator();
        foreach (var classification in classifications)
            aggregator.Record(classification.ItemStatus);
        return aggregator;
    }

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

        return OperationItemReporting.BuildAppendRequest(
            operationGroupId,
            runId,
            classification.Sequence,
            classification.ItemStatus,
            classification.ExternalId,
            displayLabel,
            classification.ErrorCode,
            classification.ErrorMessage,
            IsRetryable(classification),
            new
            {
                changeKind = classification.ChangeKind,
                summary = classification.ChangeSummary,
                changedFields = classification.ChangedFields,
            },
            metadata,
            classification.CreatedAtUtc);
    }
}
