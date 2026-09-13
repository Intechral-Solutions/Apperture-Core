using Apperture.Core.Application.Execution;
using Apperture.Core.Application.OperationItems;
using Sample.FileImport.Contracts;

namespace Sample.FileImport;

/// <summary>
/// Loads the embedded fixture, validates structure, begins a durable platform operation group,
/// then opens a process-local Running batch linked to that group (SDK-3C).
/// Structurally unusable fixtures fail here before ProcessRows (no row outcomes / itemization).
/// </summary>
public sealed class FileImportParseStepHandler : IStepHandler
{
    private readonly IImportOutcomeStore _store;

    public FileImportParseStepHandler(IImportOutcomeStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public async ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
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
            // Hard-fail path: no ProcessRows / Complete / operation group. Validates parse failure, not itemization.
            throw OperatorExecutionResult.FailSummaryOnly(
                "Structurally unusable fixture (fails before normal row processing): "
                + (parsed.StructureError ?? "invalid structure"));
        }

        var runId = context.Signals.Correlation.RunId ?? context.RunId;
        var correlationId = context.Signals.Correlation.CorrelationId ?? context.CorrelationId;
        var strictMode = FileImportParameterReader.ResolveStrictMode(parameters);
        var failOnFirst = FileImportParameterReader.ResolveFailOnFirstError(parameters);
        var maxRows = FileImportParameterReader.ResolveMaxRows(parameters);

        // Begin platform group before local batch so a missing writer does not leave an orphan batch.
        OperationGroup group;
        try
        {
            group = await context.Signals.OperationItems.BeginGroupAsync(
                operationKind: FileImportOperationItemization.OperationKind,
                sourceLabel: fixtureId,
                summaryJson: FileImportOperationItemization.BuildGroupSummaryJson(
                    fixtureId,
                    batchId: "(pending)",
                    strictMode,
                    failOnFirst,
                    maxRows),
                metadataJson: FileImportOperationItemization.BuildGroupMetadataJson(fixtureId, parsed.Rows.Count),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationItemizationException ex)
        {
            throw OperatorExecutionResult.FailSummaryOnly(
                "Sample.FileImport could not begin platform operation itemization: " + ex.Message);
        }

        var batch = _store.CreateBatch(new ImportBatchCreateRequest(
            FixtureId: fixtureId,
            RunId: runId,
            CorrelationId: correlationId,
            OperationGroupId: group.OperationGroupId));

        return OperatorExecutionResult.SuccessSummaryOnly(new
        {
            step = "Parse",
            fixtureId,
            batchId = batch.BatchId,
            operationGroupId = group.OperationGroupId,
            rowCount = parsed.Rows.Count,
            structureValid = true,
        });
    }
}
