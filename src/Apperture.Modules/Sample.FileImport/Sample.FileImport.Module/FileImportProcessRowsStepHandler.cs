using Apperture.Core.Application.Execution;
using Apperture.Core.Application.OperationItems;
using Sample.FileImport.Contracts;

namespace Sample.FileImport;

/// <summary>
/// Owns row-level success/failure/skipped outcomes in the process-local store
/// and appends durable platform operation items (SDK-3C).
/// </summary>
public sealed class FileImportProcessRowsStepHandler : IStepHandler
{
    private readonly IImportOutcomeStore _store;

    public FileImportProcessRowsStepHandler(IImportOutcomeStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public async ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var runId = context.Signals.Correlation.RunId ?? context.RunId
            ?? throw OperatorExecutionResult.FailSummaryOnly(
                "ProcessRows requires a run id to locate the open import batch.");

        var batch = _store.GetRunningBatchByRunId(runId)
            ?? throw OperatorExecutionResult.FailSummaryOnly(
                $"No running import batch found for run '{runId}'. Parse must create the batch first.");

        if (string.IsNullOrWhiteSpace(batch.OperationGroupId))
        {
            throw OperatorExecutionResult.FailSummaryOnly(
                $"Import batch '{batch.BatchId}' has no platform operation group id. Parse must begin itemization.");
        }

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
        var retryMode = FileImportParameterReader.GetBool(parameters, FileImportOperationItemRetryHandler.RetryModeParameter) == true;
        var retryRowNumber = FileImportParameterReader.GetInt(parameters, FileImportOperationItemRetryHandler.RetryRowNumberParameter);
        var now = DateTimeOffset.UtcNow;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (retryMode)
        {
            if (retryRowNumber is null or < 1)
            {
                throw OperatorExecutionResult.FailSummaryOnly(
                    "FileImport retry mode requires retryRowNumber >= 1.");
            }

            var target = parsed.Rows.FirstOrDefault(r => r.RowNumber == retryRowNumber.Value);
            if (target is null)
            {
                throw OperatorExecutionResult.FailSummaryOnly(
                    $"FileImport retry could not find row {retryRowNumber.Value} in fixture '{fixtureId}'.");
            }

            var outcome = FileImportRowProcessor.ValidateAndMap(batch.BatchId, target, seen, now);
            _store.AppendRowOutcome(outcome);

            var parentOperationItemId = FileImportParameterReader.GetString(
                parameters,
                FileImportOperationItemRetryHandler.ParentOperationItemIdParameter);
            var retryAttemptId = FileImportParameterReader.GetString(
                parameters,
                FileImportOperationItemRetryHandler.RetryAttemptIdParameter);

            try
            {
                await context.Signals.OperationItems.AppendItemAsync(
                    FileImportOperationItemization.ToAppendRequest(
                        batch.OperationGroupId!,
                        runId,
                        fixtureId,
                        outcome,
                        retryAttemptId: retryAttemptId,
                        parentOperationItemId: parentOperationItemId),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationItemizationException ex)
            {
                throw OperatorExecutionResult.FailSummaryOnly(
                    "Sample.FileImport could not append platform operation item: " + ex.Message);
            }

            return OperatorExecutionResult.SuccessSummaryOnly(new
            {
                step = "ProcessRows",
                retryMode = true,
                retryRowNumber = retryRowNumber.Value,
                parentOperationItemId,
                retryAttemptId,
                batchId = batch.BatchId,
                operationGroupId = batch.OperationGroupId,
                fixtureId,
                totalRows = 1,
                succeededCount = outcome.Status == ImportRowStatus.Succeeded ? 1 : 0,
                failedCount = outcome.Status == ImportRowStatus.Failed ? 1 : 0,
                skippedCount = outcome.Status == ImportRowStatus.Skipped ? 1 : 0,
            });
        }

        var succeeded = 0;
        var failed = 0;
        var skipped = 0;
        var stopAfterFailure = false;

        for (var i = 0; i < parsed.Rows.Count; i++)
        {
            var row = parsed.Rows[i];
            ImportRowOutcome outcome;

            if (stopAfterFailure)
            {
                outcome = new ImportRowOutcome(
                    batch.BatchId,
                    row.RowNumber,
                    row.ExternalId,
                    ImportRowStatus.Skipped,
                    ImportRowErrorCodes.SkippedFailOnFirstError,
                    "Skipped because failOnFirstError stopped processing after an earlier failure.",
                    FileImportRowProcessor.BuildSafeSummary(row),
                    now,
                    FileImportRowProcessor.BuildDisplayName(row),
                    string.IsNullOrWhiteSpace(row.Email) ? null : row.Email.Trim(),
                    string.IsNullOrWhiteSpace(row.Status) ? null : row.Status.Trim());
            }
            else if (i >= maxRows)
            {
                outcome = new ImportRowOutcome(
                    batch.BatchId,
                    row.RowNumber,
                    row.ExternalId,
                    ImportRowStatus.Skipped,
                    ImportRowErrorCodes.SkippedMaxRows,
                    $"Skipped because maxRowsPerImport={maxRows}.",
                    FileImportRowProcessor.BuildSafeSummary(row),
                    now,
                    FileImportRowProcessor.BuildDisplayName(row),
                    string.IsNullOrWhiteSpace(row.Email) ? null : row.Email.Trim(),
                    string.IsNullOrWhiteSpace(row.Status) ? null : row.Status.Trim());
            }
            else
            {
                outcome = FileImportRowProcessor.ValidateAndMap(batch.BatchId, row, seen, now);
            }

            _store.AppendRowOutcome(outcome);

            try
            {
                await context.Signals.OperationItems.AppendItemAsync(
                    FileImportOperationItemization.ToAppendRequest(
                        batch.OperationGroupId!,
                        runId,
                        fixtureId,
                        outcome),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationItemizationException ex)
            {
                throw OperatorExecutionResult.FailSummaryOnly(
                    "Sample.FileImport could not append platform operation item: " + ex.Message);
            }

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
        return OperatorExecutionResult.SuccessSummaryOnly(new
        {
            step = "ProcessRows",
            batchId = batch.BatchId,
            operationGroupId = batch.OperationGroupId,
            fixtureId,
            totalRows = total,
            succeededCount = succeeded,
            failedCount = failed,
            skippedCount = skipped,
        });
    }
}
