using Apperture.Core.Application.Execution;
using Apperture.Core.Application.OperationItems;
using Apperture.ModuleSdk.Abstractions.Signals;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling;

/// <summary>
/// Completes the platform operation group and emits exactly one fail-loud sync signal.
/// </summary>
public sealed class ApiPollingCompleteStepHandler : IStepHandler
{
    private readonly IApiPollingStateStore _store;

    public ApiPollingCompleteStepHandler(IApiPollingStateStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public async ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var runId = context.Signals.Correlation.RunId ?? context.RunId
            ?? throw OperatorExecutionResult.FailSummaryOnly(
                "Complete requires a run id to locate the sync batch.");

        var open = _store.GetRunningSyncByRunId(runId);
        if (open is null)
        {
            throw OperatorExecutionResult.FailSummaryOnly(
                $"No running sync batch found for run '{runId}'.");
        }

        if (string.IsNullOrWhiteSpace(open.OperationGroupId))
        {
            throw OperatorExecutionResult.FailSummaryOnly(
                $"Sync batch '{open.SyncBatchId}' has no platform operation group id.");
        }

        var classifications = _store.GetClassifications(open.SyncBatchId);
        var aggregator = ApiPollingOperationItemization.AggregateClassifications(classifications);
        var totals = aggregator.ToTotals();
        var newCount = classifications.Count(c => c.ChangeKind == ApiPollingChangeKinds.New);
        var updatedCount = classifications.Count(c => c.ChangeKind == ApiPollingChangeKinds.Updated);
        var unchangedCount = classifications.Count(c => c.ChangeKind == ApiPollingChangeKinds.Unchanged);
        var deletedCount = classifications.Count(c => c.ChangeKind == ApiPollingChangeKinds.Deleted);
        var failed = totals.FailedCount;
        var skipped = totals.SkippedCount;
        var succeeded = totals.SucceededCount;

        var strictMode = ApiPollingParameterReader.ResolveStrictMode(parameters);
        var failOnRecordError = ApiPollingParameterReader.ResolveFailOnRecordError(parameters);
        var includeDeleted = ApiPollingParameterReader.ResolveIncludeDeleted(parameters);
        var pageSize = ApiPollingParameterReader.ResolvePageSize(parameters);
        var maxRecords = ApiPollingParameterReader.ResolveMaxRecords(parameters);
        var status = ApiPollingClassifier.ResolveBatchStatus(succeeded, failed, skipped, strictMode);
        var groupStatus = ApiPollingOperationItemization.MapGroupStatus(status);

        // Watermark commit rule (SDK-5B): advance last-seen only for Succeeded / CompletedWithErrors.
        // Failed (strict / all-failed) discards staged mutations so partial successes do not poison state.
        if (status is SyncBatchStatus.Succeeded or SyncBatchStatus.CompletedWithErrors)
            _store.CommitStagedLastSeen(open.SyncBatchId);
        else
            _store.DiscardStagedLastSeen(open.SyncBatchId);

        try
        {
            await context.Signals.OperationItems.CompleteGroupAsync(
                new CompleteOperationGroupRequest(
                    open.OperationGroupId!,
                    groupStatus,
                    totals.TotalCount,
                    totals.SucceededCount,
                    totals.FailedCount,
                    totals.SkippedCount,
                    SummaryJson: ApiPollingOperationItemization.BuildGroupSummaryJson(
                        open.SnapshotId,
                        open.SyncBatchId,
                        open.ResetState,
                        strictMode,
                        failOnRecordError,
                        includeDeleted,
                        pageSize,
                        maxRecords,
                        newCount,
                        updatedCount,
                        unchangedCount,
                        deletedCount,
                        failed,
                        skipped)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationItemizationException ex)
        {
            throw OperatorExecutionResult.FailSummaryOnly(
                "Sample.ApiPolling could not complete platform operation group: " + ex.Message);
        }

        var batch = _store.CompleteSync(
            open.SyncBatchId,
            status,
            totals.TotalCount,
            newCount,
            updatedCount,
            unchangedCount,
            deletedCount,
            succeeded,
            failed,
            skipped);

        if (status is SyncBatchStatus.Succeeded or SyncBatchStatus.CompletedWithErrors)
        {
            _store.SetLastSummary(
                batch.ScopeKey,
                new SyncSummary(
                    batch.SnapshotId,
                    batch.SyncBatchId,
                    status.ToString(),
                    newCount,
                    updatedCount,
                    unchangedCount,
                    deletedCount,
                    failed,
                    skipped,
                    batch.CompletedAtUtc ?? DateTimeOffset.UtcNow));
        }

        var signal = ApiPollingClassifier.SignalForBatch(status, newCount, updatedCount, deletedCount);
        var payload = new Dictionary<string, object?>
        {
            ["snapshotId"] = batch.SnapshotId,
            ["syncBatchId"] = batch.SyncBatchId,
            ["operationGroupId"] = batch.OperationGroupId,
            ["totalRecords"] = batch.TotalRecords,
            ["newCount"] = batch.NewCount,
            ["updatedCount"] = batch.UpdatedCount,
            ["unchangedCount"] = batch.UnchangedCount,
            ["deletedCount"] = batch.DeletedCount,
            ["succeededCount"] = batch.SucceededCount,
            ["failedCount"] = batch.FailedCount,
            ["skippedCount"] = batch.SkippedCount,
            ["status"] = status.ToString(),
        };

        if (!string.IsNullOrWhiteSpace(batch.RunId))
            payload["runId"] = batch.RunId;
        if (!string.IsNullOrWhiteSpace(batch.CorrelationId))
            payload["correlationId"] = batch.CorrelationId;

        var emitResult = await context.Signals.Events.EmitAsync(
            context.Signals.Correlation,
            signal,
            payload,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!emitResult.Emitted)
        {
            var reason = !string.IsNullOrWhiteSpace(emitResult.SkipReason)
                ? emitResult.SkipReason
                : emitResult.SkippedBecauseDisabled
                    ? "PlatformEvents:Enabled=false"
                    : "emit returned Emitted=false";
            throw OperatorExecutionResult.FailSummaryOnly(
                $"Sample.ApiPolling {signal} platform event was not written. "
                + reason
                + " Enable PlatformEvents on Worker Host, confirm IModulePlatformEventEmitter is registered (not Null), and re-run.");
        }

        context.Signals.Logs.LogOperational(
            OperationalLogLevel.Information,
            "apipolling.signal.emitted",
            $"Emitted {signal} module signal.",
            new Dictionary<string, object?>
            {
                ["signal"] = signal,
                ["eventId"] = emitResult.EventId,
                ["syncBatchId"] = batch.SyncBatchId,
                ["operationGroupId"] = batch.OperationGroupId,
            });

        return OperatorExecutionResult.SuccessSummaryOnly(new
        {
            step = "Complete",
            snapshotId = batch.SnapshotId,
            syncBatchId = batch.SyncBatchId,
            operationGroupId = batch.OperationGroupId,
            totalRecords = batch.TotalRecords,
            newCount = batch.NewCount,
            updatedCount = batch.UpdatedCount,
            unchangedCount = batch.UnchangedCount,
            deletedCount = batch.DeletedCount,
            succeededCount = batch.SucceededCount,
            failedCount = batch.FailedCount,
            skippedCount = batch.SkippedCount,
            status = status.ToString(),
            signal,
        });
    }
}
