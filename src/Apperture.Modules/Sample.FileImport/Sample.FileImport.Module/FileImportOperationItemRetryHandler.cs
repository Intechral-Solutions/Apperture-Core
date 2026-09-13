using System.Text.Json;
using Apperture.Core.Application.OperationItems;
using Apperture.RuntimePlane.Abstractions;
using Sample.FileImport.Contracts;

namespace Sample.FileImport;

/// <summary>
/// SDK-4A — FileImport-only operation item retry handler.
/// Builds a child-run start plan from safe metadata (fixtureId + rowNumber); does not store raw CSV.
/// </summary>
public sealed class FileImportOperationItemRetryHandler : IOperationItemRetryHandler
{
    public const string RetryModeParameter = "retryMode";
    public const string RetryRowNumberParameter = "retryRowNumber";
    public const string ParentOperationItemIdParameter = "parentOperationItemId";
    public const string RetryAttemptIdParameter = "retryAttemptId";

    public string ModuleId => FileImportModule.ModuleIdValue;

    public string OperationKind => FileImportOperationItemization.OperationKind;

    public bool Supports(OperationItem item, OperationGroup group) =>
        string.Equals(group.ModuleId, ModuleId, StringComparison.Ordinal)
        && string.Equals(group.OperationKind, OperationKind, StringComparison.Ordinal);

    public OperationItemRetryEligibility Evaluate(OperationItem item, OperationGroup group)
    {
        _ = group;
        if (!string.Equals(item.Status, OperationItemStatuses.Failed, StringComparison.Ordinal))
        {
            return new OperationItemRetryEligibility(
                false,
                OperationItemRetryErrorCodes.NotFailed,
                $"Item status '{item.Status}' is not Failed.");
        }

        if (!item.IsRetryable)
        {
            return new OperationItemRetryEligibility(
                false,
                OperationItemRetryErrorCodes.NotRetryable,
                "Item is not marked retryable.");
        }

        if (!TryReadMetadata(item.MetadataJson, out var fixtureId, out var rowNumber, out var metaError))
        {
            return new OperationItemRetryEligibility(
                false,
                OperationItemRetryErrorCodes.MissingRetryMetadata,
                metaError);
        }

        if (!FileImportFixtureIds.IsKnown(fixtureId))
        {
            return new OperationItemRetryEligibility(
                false,
                OperationItemRetryErrorCodes.MissingRetryMetadata,
                $"Unknown fixtureId '{fixtureId}' in item metadata.");
        }

        if (!FileImportEmbeddedFixtures.TryLoadCsv(fixtureId, out var csv, out var loadError))
        {
            return new OperationItemRetryEligibility(
                false,
                OperationItemRetryErrorCodes.MissingRetryMetadata,
                loadError ?? "Failed to load fixture for retry.");
        }

        var parsed = FileImportCsvParser.Parse(csv);
        if (!parsed.StructureValid)
        {
            return new OperationItemRetryEligibility(
                false,
                OperationItemRetryErrorCodes.MissingRetryMetadata,
                parsed.StructureError ?? "Fixture is structurally invalid.");
        }

        if (parsed.Rows.All(r => r.RowNumber != rowNumber))
        {
            return new OperationItemRetryEligibility(
                false,
                OperationItemRetryErrorCodes.RowNotFound,
                $"Row {rowNumber} was not found in fixture '{fixtureId}'.");
        }

        return new OperationItemRetryEligibility(true);
    }

    public OperationItemRetryStartPlan BuildStartPlan(OperationItemRetryBuildContext context)
    {
        if (!TryReadMetadata(context.Item.MetadataJson, out var fixtureId, out var rowNumber, out var metaError))
            throw new OperationItemizationException(metaError);

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["fixtureId"] = fixtureId,
            [RetryModeParameter] = true,
            [RetryRowNumberParameter] = rowNumber,
            [ParentOperationItemIdParameter] = context.Item.OperationItemId,
            [RetryAttemptIdParameter] = context.RetryAttemptId,
            // Retry a single row — do not inherit fail-on-first from the original batch.
            ["failOnFirstError"] = false,
            ["strictMode"] = false,
        };

        return new OperationItemRetryStartPlan(
            FileImportModule.DefinitionId,
            parameters,
            context.Group.IntegrationId,
            context.Group.ProcedureId);
    }

    internal static bool TryReadMetadata(
        string? metadataJson,
        out string fixtureId,
        out int rowNumber,
        out string error)
    {
        fixtureId = "";
        rowNumber = 0;
        error = "";

        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            error = "Operation item metadata is missing (expected fixtureId and rowNumber).";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("fixtureId", out var fixtureEl)
                || fixtureEl.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(fixtureEl.GetString()))
            {
                error = "Operation item metadata is missing fixtureId.";
                return false;
            }

            if (!root.TryGetProperty("rowNumber", out var rowEl)
                || rowEl.ValueKind != JsonValueKind.Number
                || !rowEl.TryGetInt32(out var row)
                || row < 1)
            {
                error = "Operation item metadata is missing a valid rowNumber.";
                return false;
            }

            fixtureId = fixtureEl.GetString()!.Trim();
            rowNumber = row;
            return true;
        }
        catch (JsonException)
        {
            error = "Operation item metadata JSON is invalid.";
            return false;
        }
    }
}
