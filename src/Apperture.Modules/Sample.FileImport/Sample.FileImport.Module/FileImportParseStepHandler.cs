using Apperture.Core.Application.Execution;
using Sample.FileImport.Contracts;

namespace Sample.FileImport;

/// <summary>
/// Loads the embedded fixture, validates structure, and opens a Running batch.
/// Structurally unusable fixtures fail here before ProcessRows (no row outcomes expected).
/// </summary>
public sealed class FileImportParseStepHandler : IStepHandler
{
    private readonly IImportOutcomeStore _store;

    public FileImportParseStepHandler(IImportOutcomeStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var fixtureId = FileImportParameterReader.GetString(parameters, "fixtureId")?.Trim();
        if (string.IsNullOrWhiteSpace(fixtureId) || !FileImportFixtureIds.IsKnown(fixtureId))
        {
            throw OperatorExecutionResult.FailSummaryOnly(
                "Parse requires a known fixtureId (Validate should have rejected unknown ids).");
        }

        if (!FileImportEmbeddedFixtures.TryLoadCsv(fixtureId, out var csv, out var loadError))
        {
            throw OperatorExecutionResult.FailSummaryOnly(loadError ?? "Failed to load fixture.");
        }

        var parsed = FileImportCsvParser.Parse(csv);
        if (!parsed.StructureValid)
        {
            // Hard-fail path: no ProcessRows / Complete. Documented — import.failed may not emit.
            throw OperatorExecutionResult.FailSummaryOnly(
                "Structurally unusable fixture (fails before normal row processing): "
                + (parsed.StructureError ?? "invalid structure"));
        }

        var batch = _store.CreateBatch(new ImportBatchCreateRequest(
            FixtureId: fixtureId,
            RunId: context.Signals.Correlation.RunId ?? context.RunId,
            CorrelationId: context.Signals.Correlation.CorrelationId ?? context.CorrelationId));

        return ValueTask.FromResult<object?>(
            OperatorExecutionResult.SuccessSummaryOnly(new
            {
                step = "Parse",
                fixtureId,
                batchId = batch.BatchId,
                rowCount = parsed.Rows.Count,
                structureValid = true,
            }));
    }
}
