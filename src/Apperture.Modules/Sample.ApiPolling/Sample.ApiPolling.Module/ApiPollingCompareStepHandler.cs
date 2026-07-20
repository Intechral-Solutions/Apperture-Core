using Apperture.Core.Application.Execution;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling;

/// <summary>
/// Diffs polled records against process-local last-seen fingerprints.
/// </summary>
public sealed class ApiPollingCompareStepHandler : IStepHandler
{
    private readonly IApiPollingStateStore _store;

    public ApiPollingCompareStepHandler(IApiPollingStateStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var runId = context.Signals.Correlation.RunId ?? context.RunId
            ?? throw OperatorExecutionResult.FailSummaryOnly(
                "Compare requires a run id to locate the open sync batch.");

        var batch = _store.GetRunningSyncByRunId(runId)
            ?? throw OperatorExecutionResult.FailSummaryOnly(
                $"No running sync batch found for run '{runId}'. Poll must create the batch first.");

        var polled = _store.GetPolledRecords(batch.SyncBatchId);
        var lastSeen = _store.GetLastSeen(batch.ScopeKey);
        var includeDeleted = ApiPollingParameterReader.ResolveIncludeDeleted(parameters);
        var maxRecords = ApiPollingParameterReader.ResolveMaxRecords(parameters);
        var failOnRecordError = ApiPollingParameterReader.ResolveFailOnRecordError(parameters);
        var now = DateTimeOffset.UtcNow;

        var classifications = ApiPollingClassifier.Classify(
            batch.SyncBatchId,
            polled,
            lastSeen,
            includeDeleted,
            maxRecords,
            failOnRecordError,
            now);

        _store.SetClassifications(batch.SyncBatchId, classifications);

        var newCount = classifications.Count(c => c.ChangeKind == ApiPollingChangeKinds.New);
        var updatedCount = classifications.Count(c => c.ChangeKind == ApiPollingChangeKinds.Updated);
        var unchangedCount = classifications.Count(c => c.ChangeKind == ApiPollingChangeKinds.Unchanged);
        var deletedCount = classifications.Count(c => c.ChangeKind == ApiPollingChangeKinds.Deleted);
        var failedCount = classifications.Count(c => c.ChangeKind == ApiPollingChangeKinds.Failed);
        var skippedCount = classifications.Count(c => c.ChangeKind == ApiPollingChangeKinds.Skipped);

        return ValueTask.FromResult<object?>(
            OperatorExecutionResult.SuccessSummaryOnly(new
            {
                step = "Compare",
                syncBatchId = batch.SyncBatchId,
                snapshotId = batch.SnapshotId,
                totalRecords = classifications.Count,
                newCount,
                updatedCount,
                unchangedCount,
                deletedCount,
                failedCount,
                skippedCount,
            }));
    }
}
