using Apperture.Core.Application.Execution;
using Apperture.ModuleSdk.Abstractions.Signals;
using Sample.FileImport.Contracts;

namespace Sample.FileImport;

/// <summary>
/// Finalizes the batch via CompleteBatch and emits exactly one fail-loud import signal.
/// </summary>
public sealed class FileImportCompleteStepHandler : IStepHandler
{
    private readonly IImportOutcomeStore _store;

    public FileImportCompleteStepHandler(IImportOutcomeStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public async ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var runId = context.Signals.Correlation.RunId ?? context.RunId
            ?? throw OperatorExecutionResult.FailSummaryOnly(
                "Complete requires a run id to locate the import batch.");

        var open = _store.GetRunningBatchByRunId(runId);
        if (open is null)
        {
            throw OperatorExecutionResult.FailSummaryOnly(
                $"No running import batch found for run '{runId}'.");
        }

        var outcomes = _store.GetOutcomesByBatchId(open.BatchId);
        var succeeded = outcomes.Count(o => o.Status == ImportRowStatus.Succeeded);
        var failed = outcomes.Count(o => o.Status == ImportRowStatus.Failed);
        var skipped = outcomes.Count(o => o.Status == ImportRowStatus.Skipped);
        var total = outcomes.Count;

        var strictMode = FileImportParameterReader.ResolveStrictMode(parameters);
        var status = FileImportRowProcessor.ResolveBatchStatus(succeeded, failed, skipped, strictMode);
        var batch = _store.CompleteBatch(open.BatchId, status, total, succeeded, failed, skipped);

        var signal = FileImportRowProcessor.SignalForBatchStatus(status);
        var payload = new Dictionary<string, object?>
        {
            ["fixtureId"] = batch.FixtureId,
            ["batchId"] = batch.BatchId,
            ["totalRows"] = batch.TotalRows,
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
                $"Sample.FileImport {signal} platform event was not written. "
                + reason
                + " Enable PlatformEvents on Worker Host, confirm IModulePlatformEventEmitter is registered (not Null), and re-run.");
        }

        context.Signals.Logs.LogOperational(
            OperationalLogLevel.Information,
            "fileimport.signal.emitted",
            $"Emitted {signal} module signal.",
            new Dictionary<string, object?>
            {
                ["signal"] = signal,
                ["eventId"] = emitResult.EventId,
                ["batchId"] = batch.BatchId,
            });

        return OperatorExecutionResult.SuccessSummaryOnly(new
        {
            step = "Complete",
            fixtureId = batch.FixtureId,
            batchId = batch.BatchId,
            totalRows = batch.TotalRows,
            succeededCount = batch.SucceededCount,
            failedCount = batch.FailedCount,
            skippedCount = batch.SkippedCount,
            status = status.ToString(),
            signal,
        });
    }
}
