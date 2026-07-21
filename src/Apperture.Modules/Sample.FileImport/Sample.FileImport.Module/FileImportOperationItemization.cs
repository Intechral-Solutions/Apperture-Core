using Apperture.Core.Application.OperationItems;
using Sample.FileImport.Contracts;

namespace Sample.FileImport;

/// <summary>
/// Maps SDK-2 process-local import outcomes to SDK-3A durable operation groups/items (SDK-6A shared reporting).
/// BDS-2: emits aligned business metadata for future demo record projection.
/// </summary>
public static class FileImportOperationItemization
{
    public const string OperationKind = "file_import";

    public static string MapGroupStatus(ImportBatchStatus status) =>
        status switch
        {
            ImportBatchStatus.Succeeded => OperationGroupStatuses.Succeeded,
            ImportBatchStatus.CompletedWithErrors => OperationGroupStatuses.CompletedWithErrors,
            ImportBatchStatus.Failed => OperationGroupStatuses.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Running is not a terminal group status."),
        };

    public static string MapItemStatus(ImportRowStatus status) =>
        status switch
        {
            ImportRowStatus.Succeeded => OperationItemStatuses.Succeeded,
            ImportRowStatus.Failed => OperationItemStatuses.Failed,
            ImportRowStatus.Skipped => OperationItemStatuses.Skipped,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown row status."),
        };

    public static string MapChangeKind(ImportRowStatus status) =>
        status switch
        {
            ImportRowStatus.Succeeded => FileImportChangeKinds.Imported,
            ImportRowStatus.Failed => FileImportChangeKinds.Failed,
            ImportRowStatus.Skipped => FileImportChangeKinds.Skipped,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown row status."),
        };

    public static bool IsRetryable(ImportRowOutcome outcome) =>
        outcome.Status == ImportRowStatus.Failed;

    public static string BuildGroupSummaryJson(
        string fixtureId,
        string batchId,
        bool strictMode,
        bool failOnFirstError,
        int maxRows) =>
        OperationItemReporting.SerializeMetadata(new
        {
            fixtureId,
            batchId,
            strictMode,
            failOnFirstError,
            maxRows,
        });

    public static string BuildGroupMetadataJson(string fixtureId, int parsedRowCount) =>
        OperationItemReporting.SerializeMetadata(new { fixtureId, parsedRowCount });

    public static OperationGroupAggregator AggregateOutcomes(IReadOnlyList<ImportRowOutcome> outcomes)
    {
        var aggregator = new OperationGroupAggregator();
        foreach (var outcome in outcomes)
            aggregator.Record(MapItemStatus(outcome.Status));
        return aggregator;
    }

    public static AppendOperationItemRequest ToAppendRequest(
        string operationGroupId,
        string runId,
        string fixtureId,
        ImportRowOutcome outcome,
        string? retryAttemptId = null,
        string? parentOperationItemId = null)
    {
        var businessKey = NormalizeBusinessKey(outcome.SourceKey);
        var changeKind = MapChangeKind(outcome.Status);
        var displayName = ResolveDisplayName(outcome, businessKey);
        var displayLabel = displayName;

        object metadata = string.IsNullOrWhiteSpace(retryAttemptId)
            ? new
            {
                recordType = OperationItemBusinessMetadataKeys.CustomerRecordType,
                businessKey,
                displayName,
                changeKind,
                observedAt = outcome.CreatedAtUtc,
                fixtureId,
                rowNumber = outcome.RowNumber,
                errorCode = outcome.ErrorCode,
            }
            : new
            {
                recordType = OperationItemBusinessMetadataKeys.CustomerRecordType,
                businessKey,
                displayName,
                changeKind,
                observedAt = outcome.CreatedAtUtc,
                fixtureId,
                rowNumber = outcome.RowNumber,
                errorCode = outcome.ErrorCode,
                retryAttemptId,
                parentOperationItemId,
            };

        return OperationItemReporting.BuildAppendRequest(
            operationGroupId,
            runId,
            outcome.RowNumber,
            MapItemStatus(outcome.Status),
            businessKey,
            displayLabel,
            outcome.ErrorCode,
            outcome.ErrorMessage,
            IsRetryable(outcome),
            new { changeKind, summary = outcome.SafeRowSummary },
            metadata,
            outcome.CreatedAtUtc);
    }

    public static string? NormalizeBusinessKey(string? sourceKey) =>
        string.IsNullOrWhiteSpace(sourceKey) ? null : sourceKey.Trim();

    static string ResolveDisplayName(ImportRowOutcome outcome, string? businessKey)
    {
        if (!string.IsNullOrWhiteSpace(outcome.DisplayName))
            return outcome.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(businessKey))
            return businessKey;
        return $"row {outcome.RowNumber}";
    }
}
