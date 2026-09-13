using Apperture.Core.Application.Execution;
using Apperture.Core.Application.OperationItems;
using Apperture.ModuleSdk.Abstractions.Signals;
using Sample.FileImport.Contracts;

namespace Sample.FileImport;

/// <summary>
/// Finalizes the process-local batch and platform operation group, then emits exactly one fail-loud import signal.
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

        if (string.IsNullOrWhiteSpace(open.OperationGroupId))
        {
            throw OperatorExecutionResult.FailSummaryOnly(
                $"Import batch '{open.BatchId}' has no platform operation group id.");
        }

        var outcomes = _store.GetOutcomesByBatchId(open.BatchId);
        var aggregator = FileImportOperationItemization.AggregateOutcomes(outcomes);
        var totals = aggregator.ToTotals();

        var strictMode = FileImportParameterReader.ResolveStrictMode(parameters);
        var failOnFirst = FileImportParameterReader.ResolveFailOnFirstError(parameters);
        var maxRows = FileImportParameterReader.ResolveMaxRows(parameters);
        var status = FileImportRowProcessor.ResolveBatchStatus(
            totals.SucceededCount,
            totals.FailedCount,
            totals.SkippedCount,
            strictMode);
        var groupStatus = FileImportOperationItemization.MapGroupStatus(status);

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
                    SummaryJson: FileImportOperationItemization.BuildGroupSummaryJson(
                        open.FixtureId,
                        open.BatchId,
                        strictMode,
                        failOnFirst,
                        maxRows)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationItemizationException ex)
        {
            throw OperatorExecutionResult.FailSummaryOnly(
                "Sample.FileImport could not complete platform operation group: " + ex.Message);
        }

        var batch = _store.CompleteBatch(
            open.BatchId,
            status,
            totals.TotalCount,
            totals.SucceededCount,
            totals.FailedCount,
            totals.SkippedCount);

        var signal = FileImportRowProcessor.SignalForBatchStatus(status);
        var payload = new Dictionary<string, object?>
        {
            ["fixtureId"] = batch.FixtureId,
            ["batchId"] = batch.BatchId,
            ["operationGroupId"] = batch.OperationGroupId,
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
                ["operationGroupId"] = batch.OperationGroupId,
            });

        return OperatorExecutionResult.SuccessSummaryOnly(new
        {
            step = "Complete",
            fixtureId = batch.FixtureId,
            batchId = batch.BatchId,
            operationGroupId = batch.OperationGroupId,
            totalRows = batch.TotalRows,
            succeededCount = batch.SucceededCount,
            failedCount = batch.FailedCount,
            skippedCount = batch.SkippedCount,
            status = status.ToString(),
            signal,
        });
    }
}
