using Apperture.Core.Application.Execution;
using Sample.FileImport.Contracts;

namespace Sample.FileImport;

/// <summary>
/// Owns row-level success/failure/skipped outcomes in the process-local store.
/// </summary>
public sealed class FileImportProcessRowsStepHandler : IStepHandler
{
    private readonly IImportOutcomeStore _store;

    public FileImportProcessRowsStepHandler(IImportOutcomeStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var runId = context.Signals.Correlation.RunId ?? context.RunId
            ?? throw OperatorExecutionResult.FailSummaryOnly(
                "ProcessRows requires a run id to locate the open import batch.");

        var batch = _store.GetRunningBatchByRunId(runId)
            ?? throw OperatorExecutionResult.FailSummaryOnly(
                $"No running import batch found for run '{runId}'. Parse must create the batch first.");

        var fixtureId = batch.FixtureId;
        if (!FileImportEmbeddedFixtures.TryLoadCsv(fixtureId, out var csv, out var loadError))
        {
            throw OperatorExecutionResult.FailSummaryOnly(loadError ?? "Failed to reload fixture.");
        }

        var parsed = FileImportCsvParser.Parse(csv);
        if (!parsed.StructureValid)
        {
            throw OperatorExecutionResult.FailSummaryOnly(
                "ProcessRows expected a structurally valid fixture. "
                + (parsed.StructureError ?? "invalid structure"));
        }

        var maxRows = FileImportParameterReader.ResolveMaxRows(parameters);
        var failOnFirst = FileImportParameterReader.ResolveFailOnFirstError(parameters);
        var now = DateTimeOffset.UtcNow;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var succeeded = 0;
        var failed = 0;
        var skipped = 0;
        var stopAfterFailure = false;

        for (var i = 0; i < parsed.Rows.Count; i++)
        {
            var row = parsed.Rows[i];

            if (stopAfterFailure)
            {
                var skip = new ImportRowOutcome(
                    batch.BatchId,
                    row.RowNumber,
                    row.ExternalId,
                    ImportRowStatus.Skipped,
                    ImportRowErrorCodes.SkippedFailOnFirstError,
                    "Skipped because failOnFirstError stopped processing after an earlier failure.",
                    FileImportRowProcessor.BuildSafeSummary(row),
                    now);
                _store.AppendRowOutcome(skip);
                skipped++;
                continue;
            }

            if (i >= maxRows)
            {
                var skip = new ImportRowOutcome(
                    batch.BatchId,
                    row.RowNumber,
                    row.ExternalId,
                    ImportRowStatus.Skipped,
                    ImportRowErrorCodes.SkippedMaxRows,
                    $"Skipped because maxRowsPerImport={maxRows}.",
                    FileImportRowProcessor.BuildSafeSummary(row),
                    now);
                _store.AppendRowOutcome(skip);
                skipped++;
                continue;
            }

            var outcome = FileImportRowProcessor.ValidateAndMap(batch.BatchId, row, seen, now);
            _store.AppendRowOutcome(outcome);

            switch (outcome.Status)
            {
                case ImportRowStatus.Succeeded:
                    succeeded++;
                    break;
                case ImportRowStatus.Failed:
                    failed++;
                    if (failOnFirst)
                        stopAfterFailure = true;
                    break;
                case ImportRowStatus.Skipped:
                    skipped++;
                    break;
            }
        }

        var total = succeeded + failed + skipped;
        return ValueTask.FromResult<object?>(
            OperatorExecutionResult.SuccessSummaryOnly(new
            {
                step = "ProcessRows",
                batchId = batch.BatchId,
                fixtureId,
                totalRows = total,
                succeededCount = succeeded,
                failedCount = failed,
                skippedCount = skipped,
            }));
    }
}
