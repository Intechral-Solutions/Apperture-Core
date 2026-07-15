using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using Sample.FileImport.Contracts;

namespace Sample.FileImport;

public static class FileImportParameterReader
{
    public static string? GetString(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
            return null;

        return value switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            _ => value.ToString(),
        };
    }

    public static bool? GetBool(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
            return null;

        return value switch
        {
            bool b => b,
            JsonElement je when je.ValueKind == JsonValueKind.True => true,
            JsonElement je when je.ValueKind == JsonValueKind.False => false,
            string s when bool.TryParse(s, out var parsed) => parsed,
            _ => null,
        };
    }

    public static int? GetInt(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
            return null;

        return value switch
        {
            int i => i,
            long l => checked((int)l),
            JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var n) => n,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    public static bool ResolveStrictMode(IReadOnlyDictionary<string, object?> parameters) =>
        GetBool(parameters, "strictMode") ?? FileImportSettingDefaults.StrictModeDefault;

    public static bool ResolveFailOnFirstError(IReadOnlyDictionary<string, object?> parameters) =>
        GetBool(parameters, "failOnFirstError") ?? FileImportSettingDefaults.FailOnFirstError;

    public static int ResolveMaxRows(IReadOnlyDictionary<string, object?> parameters)
    {
        var max = GetInt(parameters, "maxRowsPerImport") ?? FileImportSettingDefaults.MaxRowsPerImport;
        return max <= 0 ? FileImportSettingDefaults.MaxRowsPerImport : max;
    }
}

public static class FileImportRowProcessor
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Active",
        "Inactive",
        "Pending",
    };

    public static ImportRowOutcome ValidateAndMap(
        string batchId,
        ParsedCustomerRow row,
        HashSet<string> seenExternalIds,
        DateTimeOffset nowUtc)
    {
        if (!row.StructurallyValid)
        {
            return Failed(
                batchId,
                row.RowNumber,
                null,
                ImportRowErrorCodes.ParseError,
                row.ParseErrorMessage ?? "Row is not structurally parseable.",
                BuildSafeSummary(row),
                nowUtc);
        }

        if (string.IsNullOrWhiteSpace(row.ExternalId))
        {
            return Failed(
                batchId,
                row.RowNumber,
                null,
                ImportRowErrorCodes.MissingExternalId,
                "externalId is required.",
                BuildSafeSummary(row),
                nowUtc);
        }

        if (!seenExternalIds.Add(row.ExternalId))
        {
            return Failed(
                batchId,
                row.RowNumber,
                row.ExternalId,
                ImportRowErrorCodes.DuplicateExternalId,
                $"Duplicate externalId '{row.ExternalId}'.",
                BuildSafeSummary(row),
                nowUtc);
        }

        if (!IsValidEmail(row.Email))
        {
            return Failed(
                batchId,
                row.RowNumber,
                row.ExternalId,
                ImportRowErrorCodes.InvalidEmail,
                $"Invalid email '{row.Email}'.",
                BuildSafeSummary(row),
                nowUtc);
        }

        if (string.IsNullOrWhiteSpace(row.Status) || !AllowedStatuses.Contains(row.Status))
        {
            return Failed(
                batchId,
                row.RowNumber,
                row.ExternalId,
                ImportRowErrorCodes.InvalidStatus,
                $"Invalid status '{row.Status}'. Allowed: Active, Inactive, Pending.",
                BuildSafeSummary(row),
                nowUtc);
        }

        if (!DateOnly.TryParse(row.EffectiveDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return Failed(
                batchId,
                row.RowNumber,
                row.ExternalId,
                ImportRowErrorCodes.InvalidDate,
                $"Invalid effectiveDate '{row.EffectiveDate}'. Use yyyy-MM-dd.",
                BuildSafeSummary(row),
                nowUtc);
        }

        return new ImportRowOutcome(
            batchId,
            row.RowNumber,
            row.ExternalId,
            ImportRowStatus.Succeeded,
            null,
            null,
            BuildSafeSummary(row),
            nowUtc);
    }

    public static ImportBatchStatus ResolveBatchStatus(int succeeded, int failed, int skipped, bool strictMode)
    {
        _ = skipped;
        if (failed > 0 && (strictMode || succeeded == 0))
            return ImportBatchStatus.Failed;

        if (failed > 0 && succeeded > 0)
            return ImportBatchStatus.CompletedWithErrors;

        return ImportBatchStatus.Succeeded;
    }

    public static string SignalForBatchStatus(ImportBatchStatus status) =>
        status switch
        {
            ImportBatchStatus.Succeeded => "import.completed",
            ImportBatchStatus.CompletedWithErrors => "import.completed.with.errors",
            ImportBatchStatus.Failed => "import.failed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Running batches cannot emit."),
        };

    public static string BuildSafeSummary(ParsedCustomerRow row)
    {
        var id = row.ExternalId ?? "(no-id)";
        var email = row.Email ?? "(no-email)";
        return $"row={row.RowNumber}; id={id}; email={email}";
    }

    private static ImportRowOutcome Failed(
        string batchId,
        int rowNumber,
        string? sourceKey,
        string code,
        string message,
        string summary,
        DateTimeOffset nowUtc) =>
        new(batchId, rowNumber, sourceKey, ImportRowStatus.Failed, code, message, summary, nowUtc);

    private static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            _ = new MailAddress(email);
            return email.Contains('@', StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
