using System.Text.Json;

namespace Apperture.Core.Application.OperationItems;

public static class OperationItemizationValidation
{
    public static void EnsureRunId(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new OperationItemizationException(
                "Operation itemization requires a platform RunId. Detached groups/items are not allowed.");
        }

        EnsureMaxLength("RunId", runId, OperationItemizationBounds.IdMaxLength);
    }

    public static void ValidateBegin(BeginOperationGroupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureRunId(request.RunId);
        EnsureRequired("ModuleId", request.ModuleId, OperationItemizationBounds.ModuleIdMaxLength);
        EnsureRequired("OperationKind", request.OperationKind, OperationItemizationBounds.OperationKindMaxLength);
        EnsureOptionalMaxLength("IntegrationId", request.IntegrationId, OperationItemizationBounds.IdMaxLength);
        EnsureOptionalMaxLength("ProcedureId", request.ProcedureId, OperationItemizationBounds.ProcedureIdMaxLength);
        EnsureOptionalMaxLength("SourceLabel", request.SourceLabel, OperationItemizationBounds.SourceLabelMaxLength);
        EnsureJsonField("SummaryJson", request.SummaryJson, OperationItemizationBounds.SummaryJsonMaxChars);
        EnsureJsonField("MetadataJson", request.MetadataJson, OperationItemizationBounds.MetadataJsonMaxChars);
    }

    public static void ValidateAppend(AppendOperationItemRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureRunId(request.RunId);
        EnsureRequired("OperationGroupId", request.OperationGroupId, OperationItemizationBounds.IdMaxLength);
        if (request.Sequence < 0)
            throw new OperationItemizationException("Sequence must be >= 0.");

        if (!OperationItemStatuses.All.Contains(request.Status))
            throw new OperationItemizationException($"Unknown item status '{request.Status}'.");

        EnsureOptionalMaxLength("SubjectKey", request.SubjectKey, OperationItemizationBounds.SubjectKeyMaxLength);
        EnsureOptionalMaxLength("DisplayLabel", request.DisplayLabel, OperationItemizationBounds.DisplayLabelMaxLength);
        EnsureOptionalMaxLength("ErrorCode", request.ErrorCode, OperationItemizationBounds.ErrorCodeMaxLength);
        EnsureOptionalMaxLength("ErrorMessage", request.ErrorMessage, OperationItemizationBounds.ErrorMessageMaxLength);
        if (request.RetryCount < 0)
            throw new OperationItemizationException("RetryCount must be >= 0.");

        EnsureJsonField("ChangeSummaryJson", request.ChangeSummaryJson, OperationItemizationBounds.ChangeSummaryJsonMaxChars);
        EnsureJsonField("MetadataJson", request.MetadataJson, OperationItemizationBounds.MetadataJsonMaxChars);
        RejectObviousSecrets(request.ErrorMessage, request.ChangeSummaryJson, request.MetadataJson, request.DisplayLabel);
    }

    public static void ValidateComplete(CompleteOperationGroupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureRequired("OperationGroupId", request.OperationGroupId, OperationItemizationBounds.IdMaxLength);
        if (!OperationGroupStatuses.All.Contains(request.Status))
            throw new OperationItemizationException($"Unknown group status '{request.Status}'.");
        if (request.Status == OperationGroupStatuses.Running)
            throw new OperationItemizationException("CompleteGroup cannot leave status Running.");

        if (request.TotalCount < 0 || request.SucceededCount < 0 || request.FailedCount < 0 || request.SkippedCount < 0)
            throw new OperationItemizationException("Counts must be >= 0.");

        if (request.SucceededCount + request.FailedCount + request.SkippedCount != request.TotalCount)
        {
            throw new OperationItemizationException(
                "TotalCount must equal SucceededCount + FailedCount + SkippedCount.");
        }

        EnsureJsonField("SummaryJson", request.SummaryJson, OperationItemizationBounds.SummaryJsonMaxChars);
    }

    private static void EnsureRequired(string name, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new OperationItemizationException($"{name} is required.");
        EnsureMaxLength(name, value, maxLength);
    }

    private static void EnsureOptionalMaxLength(string name, string? value, int maxLength)
    {
        if (value is null)
            return;
        EnsureMaxLength(name, value, maxLength);
    }

    private static void EnsureMaxLength(string name, string value, int maxLength)
    {
        if (value.Length > maxLength)
        {
            throw new OperationItemizationException(
                $"{name} exceeds max length {maxLength} (was {value.Length}). Silent truncation is not allowed.");
        }
    }

    private static void EnsureJsonField(string name, string? json, int maxChars)
    {
        if (json is null)
            return;

        if (json.Length > maxChars)
        {
            throw new OperationItemizationException(
                $"{name} exceeds max length {maxChars} characters (was {json.Length}). Silent truncation is not allowed.");
        }

        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new OperationItemizationException($"{name} must be valid JSON.", ex);
        }
    }

    private static void RejectObviousSecrets(params string?[] fields)
    {
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field))
                continue;

            var lower = field.ToLowerInvariant();
            if (lower.Contains("password=", StringComparison.Ordinal)
                || lower.Contains("secret=", StringComparison.Ordinal)
                || lower.Contains("client_secret", StringComparison.Ordinal)
                || lower.Contains("connectionstring", StringComparison.Ordinal)
                || lower.Contains("bearer ", StringComparison.Ordinal))
            {
                throw new OperationItemizationException(
                    "Operation itemization fields must not contain secrets (password/secret/connection string/bearer tokens).");
            }
        }
    }
}

public sealed class OperationItemizationException : InvalidOperationException
{
    public OperationItemizationException(string message) : base(message) { }

    public OperationItemizationException(string message, Exception inner) : base(message, inner) { }
}
