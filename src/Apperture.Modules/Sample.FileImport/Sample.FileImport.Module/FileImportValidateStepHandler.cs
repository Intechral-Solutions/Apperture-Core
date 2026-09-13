using Apperture.Core.Application.Execution;
using Sample.FileImport.Contracts;

namespace Sample.FileImport;

public sealed class FileImportValidateStepHandler : IStepHandler
{
    public ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        _ = context;
        _ = cancellationToken;

        var fixtureId = FileImportParameterReader.GetString(parameters, "fixtureId");
        if (string.IsNullOrWhiteSpace(fixtureId))
        {
            throw OperatorExecutionResult.FailSummaryOnly(
                "Sample.FileImport requires procedure parameter fixtureId.");
        }

        if (!FileImportFixtureIds.IsKnown(fixtureId))
        {
            throw OperatorExecutionResult.FailSummaryOnly(
                $"Unknown fixture id '{fixtureId}'. Known: {string.Join(", ", FileImportFixtureIds.All)}.");
        }

        var maxRows = FileImportParameterReader.ResolveMaxRows(parameters);
        if (maxRows <= 0)
        {
            throw OperatorExecutionResult.FailSummaryOnly("maxRowsPerImport must be greater than zero.");
        }

        return ValueTask.FromResult<object?>(
            OperatorExecutionResult.SuccessSummaryOnly(new
            {
                step = "Validate",
                fixtureId = fixtureId.Trim(),
                strictMode = FileImportParameterReader.ResolveStrictMode(parameters),
                maxRowsPerImport = maxRows,
                failOnFirstError = FileImportParameterReader.ResolveFailOnFirstError(parameters),
            }));
    }
}
