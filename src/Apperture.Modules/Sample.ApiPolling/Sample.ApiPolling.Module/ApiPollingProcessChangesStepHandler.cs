using Apperture.Core.Application.Execution;
using Apperture.Core.Application.OperationItems;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling;

/// <summary>
/// Begins a durable platform operation group, appends one item per classification,
/// and stages last-seen mutations for successful classifications.
/// Retry mode writes <c>api_poll_retry</c> with parent/attempt linkage metadata.
/// </summary>
public sealed class ApiPollingProcessChangesStepHandler : IStepHandler
{
    private readonly IApiPollingStateStore _store;

    public ApiPollingProcessChangesStepHandler(IApiPollingStateStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public async ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var runId = context.Signals.Correlation.RunId ?? context.RunId
            ?? throw OperatorExecutionResult.FailSummaryOnly(
                "ProcessChanges requires a run id to locate the open sync batch.");

        var batch = _store.GetRunningSyncByRunId(runId)
            ?? throw OperatorExecutionResult.FailSummaryOnly(
                $"No running sync batch found for run '{runId}'. Poll must create the batch first.");

        var classifications = _store.GetClassifications(batch.SyncBatchId);
        if (classifications.Count == 0 && _store.GetPolledRecords(batch.SyncBatchId).Count > 0)
        {
            throw OperatorExecutionResult.FailSummaryOnly(
                "ProcessChanges found polled records but no classifications. Compare must run first.");
        }

        var strictMode = ApiPollingParameterReader.ResolveStrictMode(parameters);
        var failOnRecordError = ApiPollingParameterReader.ResolveFailOnRecordError(parameters);
        var includeDeleted = ApiPollingParameterReader.ResolveIncludeDeleted(parameters);
        var pageSize = ApiPollingParameterReader.ResolvePageSize(parameters);
        var maxRecords = ApiPollingParameterReader.ResolveMaxRecords(parameters);
        var retryMode = ApiPollingParameterReader.ResolveRetryMode(parameters);
        var parentOperationItemId = ApiPollingParameterReader.GetString(
            parameters,
            ApiPollingOperationItemRetryHandler.ParentOperationItemIdParameter);
        var retryAttemptId = ApiPollingParameterReader.GetString(
            parameters,
            ApiPollingOperationItemRetryHandler.RetryAttemptIdParameter);

        var newCount = classifications.Count(c => c.ChangeKind == ApiPollingChangeKinds.New);
        var updatedCount = classifications.Count(c => c.ChangeKind == ApiPollingChangeKinds.Updated);
        var unchangedCount = classifications.Count(c => c.ChangeKind == ApiPollingChangeKinds.Unchanged);
        var deletedCount = classifications.Count(c => c.ChangeKind == ApiPollingChangeKinds.Deleted);
        var failedCount = classifications.Count(c => c.ChangeKind == ApiPollingChangeKinds.Failed);
        var skippedCount = classifications.Count(c => c.ChangeKind == ApiPollingChangeKinds.Skipped);

        var operationKind = retryMode
            ? ApiPollingOperationItemization.RetryOperationKind
            : ApiPollingOperationItemization.OperationKind;

        OperationGroup group;
        try
        {
            group = await context.Signals.OperationItems.BeginGroupAsync(
                operationKind: operationKind,
                sourceLabel: batch.SnapshotId,
                summaryJson: ApiPollingOperationItemization.BuildGroupSummaryJson(
                    batch.SnapshotId,
                    batch.SyncBatchId,
                    batch.ResetState,
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
                    skippedCount),
                metadataJson: ApiPollingOperationItemization.BuildGroupMetadataJson(
                    batch.SnapshotId,
                    classifications.Count,
                    batch.ResetState),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationItemizationException ex)
        {
            throw OperatorExecutionResult.FailSummaryOnly(
                "Sample.ApiPolling could not begin platform operation itemization: " + ex.Message);
        }

        _store.AttachOperationGroup(batch.SyncBatchId, group.OperationGroupId);

        foreach (var classification in classifications)
        {
            try
            {
                await context.Signals.OperationItems.AppendItemAsync(
                    ApiPollingOperationItemization.ToAppendRequest(
                        group.OperationGroupId,
                        runId,
                        batch.SnapshotId,
                        includeDeleted,
                        classification,
                        retryAttemptId: retryMode ? retryAttemptId : null,
                        parentOperationItemId: retryMode ? parentOperationItemId : null),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationItemizationException ex)
            {
                throw OperatorExecutionResult.FailSummaryOnly(
                    "Sample.ApiPolling could not append platform operation item: " + ex.Message);
            }
        }

        ApplyStateUpdates(batch, classifications);

        var succeeded = newCount + updatedCount + unchangedCount + deletedCount;
        return OperatorExecutionResult.SuccessSummaryOnly(new
        {
            step = "ProcessChanges",
            syncBatchId = batch.SyncBatchId,
            operationGroupId = group.OperationGroupId,
            snapshotId = batch.SnapshotId,
            retryMode,
            operationKind,
            totalRecords = classifications.Count,
            newCount,
            updatedCount,
            unchangedCount,
            deletedCount,
            succeededCount = succeeded,
            failedCount,
            skippedCount,
        });
    }

    /// <summary>
    /// Stages last-seen mutations for successful classifications only (one record in retry mode).
    /// Commit happens in Complete only for Succeeded / CompletedWithErrors.
    /// </summary>
    private void ApplyStateUpdates(SyncBatch batch, IReadOnlyList<RecordClassification> classifications)
    {
        var polledById = _store.GetPolledRecords(batch.SyncBatchId)
            .Where(r => !string.IsNullOrWhiteSpace(r.ExternalId))
            .GroupBy(r => r.ExternalId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        var upserts = new List<SeenAccountFingerprint>();
        var removals = new List<string>();

        foreach (var c in classifications)
        {
            switch (c.ChangeKind)
            {
                case ApiPollingChangeKinds.New:
                case ApiPollingChangeKinds.Updated:
                case ApiPollingChangeKinds.Unchanged:
                    if (!string.IsNullOrWhiteSpace(c.ExternalId)
                        && polledById.TryGetValue(c.ExternalId, out var record))
                    {
                        upserts.Add(ApiPollingClassifier.ToFingerprint(record, now));
                    }
                    break;

                case ApiPollingChangeKinds.Deleted:
                    if (!string.IsNullOrWhiteSpace(c.ExternalId))
                        removals.Add(c.ExternalId);
                    break;
            }
        }

        _store.StageLastSeenMutations(batch.SyncBatchId, upserts, removals);
    }
}
