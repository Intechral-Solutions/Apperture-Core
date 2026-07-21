using Apperture.Core.Application.OperationItems;
using Sample.FileImport.Contracts;

namespace Sample.FileImport;

/// <summary>
/// Maps SDK-2 process-local import outcomes to SDK-3A durable operation groups/items (SDK-6A shared reporting).
/// </summary>
internal static class FileImportOperationItemization
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
        var displayLabel = !string.IsNullOrWhiteSpace(outcome.SourceKey)
            ? outcome.SourceKey!.Trim()
            : $"row {outcome.RowNumber}";

        object metadata = string.IsNullOrWhiteSpace(retryAttemptId)
            ? new
            {
                fixtureId,
                rowNumber = outcome.RowNumber,
                errorCode = outcome.ErrorCode,
            }
            : new
            {
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
            outcome.SourceKey,
            displayLabel,
            outcome.ErrorCode,
            outcome.ErrorMessage,
            IsRetryable(outcome),
            new { summary = outcome.SafeRowSummary },
            metadata,
            outcome.CreatedAtUtc);
    }
}
