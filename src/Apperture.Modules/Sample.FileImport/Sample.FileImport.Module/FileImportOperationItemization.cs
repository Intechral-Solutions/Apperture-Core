using System.Text.Json;
using Apperture.Core.Application.OperationItems;
using Sample.FileImport.Contracts;

namespace Sample.FileImport;

/// <summary>
/// Maps SDK-2 process-local import outcomes to SDK-3A durable operation groups/items.
/// </summary>
internal static class FileImportOperationItemization
{
    public const string OperationKind = "file_import";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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

    /// <summary>
    /// Row validation/process failures are retry-ready; control-flow skips are not.
    /// </summary>
    public static bool IsRetryable(ImportRowOutcome outcome) =>
        outcome.Status == ImportRowStatus.Failed;

    public static string BuildGroupSummaryJson(
        string fixtureId,
        string batchId,
        bool strictMode,
        bool failOnFirstError,
        int maxRows) =>
        JsonSerializer.Serialize(
            new
            {
                fixtureId,
                batchId,
                strictMode,
                failOnFirstError,
                maxRows,
            },
            JsonOptions);

    public static string BuildGroupMetadataJson(string fixtureId, int parsedRowCount) =>
        JsonSerializer.Serialize(new { fixtureId, parsedRowCount }, JsonOptions);

    public static AppendOperationItemRequest ToAppendRequest(
        string operationGroupId,
        string runId,
        string fixtureId,
        ImportRowOutcome outcome,
        string? retryAttemptId = null,
        string? parentOperationItemId = null)
    {
        // Prefer business key; fall back to compact row label (no silent truncation of large payloads).
        var displayLabel = !string.IsNullOrWhiteSpace(outcome.SourceKey)
            ? outcome.SourceKey!.Trim()
            : $"row {outcome.RowNumber}";

        var changeSummaryJson = JsonSerializer.Serialize(
            new { summary = outcome.SafeRowSummary },
            JsonOptions);

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

        var metadataJson = JsonSerializer.Serialize(metadata, JsonOptions);

        return new AppendOperationItemRequest(
            OperationGroupId: operationGroupId,
            RunId: runId,
            Sequence: outcome.RowNumber,
            Status: MapItemStatus(outcome.Status),
            SubjectKey: outcome.SourceKey,
            DisplayLabel: displayLabel,
            ErrorCode: outcome.ErrorCode,
            ErrorMessage: outcome.ErrorMessage,
            IsRetryable: IsRetryable(outcome),
            RetryCount: 0,
            LastAttemptedAtUtc: outcome.CreatedAtUtc,
            CompletedAtUtc: outcome.CreatedAtUtc,
            ChangeSummaryJson: changeSummaryJson,
            MetadataJson: metadataJson);
    }
}
