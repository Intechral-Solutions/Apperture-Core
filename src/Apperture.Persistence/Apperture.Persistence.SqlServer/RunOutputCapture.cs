using System.Text.Json;
using Apperture.Core.Application.Execution;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Captures the last step's return value for operator-visible run output. See ADR-0019 and ADR-0020.
/// </summary>
public static class RunOutputCapture
{
    public const int SchemaVersion = 1;
    public const int MaxPayloadBytes = 64 * 1024;

    public const string StatusNone = "none";
    public const string StatusEmpty = "empty";
    public const string StatusCaptured = "captured";
    public const string StatusTruncated = "truncated";
    public const string StatusUnsupported = "unsupported";
    public const string StatusSummaryOnly = "summary_only";
    public const string StatusWithheld = "withheld";

    public const string DisplayPolicyInline = "inline";
    public const string DisplayPolicySummaryOnly = "summary_only";
    public const string DisplayPolicyWithheld = "withheld";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Attempts to capture the step return value for persistence.
    /// </summary>
    public static RunOutputCaptureResult TryCapture(object? value)
    {
        if (value is OperatorRunOutput wrapped)
            return TryCaptureWrapped(wrapped);

        return TryCaptureLegacy(value, displayPolicy: null);
    }

    private static RunOutputCaptureResult TryCaptureWrapped(OperatorRunOutput wrapped)
    {
        var policyString = wrapped.DisplayPolicy switch
        {
            OperatorOutputDisplayPolicy.Inline => DisplayPolicyInline,
            OperatorOutputDisplayPolicy.SummaryOnly => DisplayPolicySummaryOnly,
            OperatorOutputDisplayPolicy.Withheld => DisplayPolicyWithheld,
            _ => DisplayPolicyInline
        };

        if (wrapped.DisplayPolicy == OperatorOutputDisplayPolicy.Withheld)
            return new RunOutputCaptureResult(null, null, StatusWithheld, SchemaVersion, DisplayPolicyWithheld);

        var payload = wrapped.Payload;
        if (payload is null)
            return new RunOutputCaptureResult(null, null, StatusEmpty, SchemaVersion, policyString);

        if (wrapped.DisplayPolicy == OperatorOutputDisplayPolicy.SummaryOnly)
            return TryCaptureSummaryOnly(payload, policyString);

        return TryCaptureLegacy(payload, DisplayPolicyInline);
    }

    private static RunOutputCaptureResult TryCaptureSummaryOnly(object payload, string displayPolicySummaryOnly)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var bytes = System.Text.Encoding.UTF8.GetByteCount(json);
            string summary;
            if (bytes <= MaxPayloadBytes)
            {
                summary = TryExtractSummary(payload, json) ?? "Output summary only (no extractable headline).";
            }
            else
            {
                var truncated = TruncateToMaxBytes(json);
                summary = TryExtractSummary(payload, truncated)
                          ?? "Output truncated before summary-only capture (exceeded max size).";
            }

            return new RunOutputCaptureResult(null, summary, StatusSummaryOnly, SchemaVersion, displayPolicySummaryOnly);
        }
        catch
        {
            return new RunOutputCaptureResult(null, null, StatusUnsupported, SchemaVersion, displayPolicySummaryOnly);
        }
    }

    private static RunOutputCaptureResult TryCaptureLegacy(object? value, string? displayPolicy)
    {
        if (value is null)
            return new RunOutputCaptureResult(null, null, StatusEmpty, SchemaVersion, displayPolicy);

        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            var bytes = System.Text.Encoding.UTF8.GetByteCount(json);

            if (bytes <= MaxPayloadBytes)
            {
                var summary = TryExtractSummary(value, json);
                return new RunOutputCaptureResult(json, summary, StatusCaptured, SchemaVersion, displayPolicy);
            }

            var truncated = TruncateToMaxBytes(json);
            return new RunOutputCaptureResult(truncated, "Output truncated (exceeded max size).", StatusTruncated, SchemaVersion, displayPolicy);
        }
        catch
        {
            return new RunOutputCaptureResult(null, null, StatusUnsupported, SchemaVersion, displayPolicy);
        }
    }

    private static string? TryExtractSummary(object value, string json)
    {
        if (value is string s)
            return TruncateForSummary(s, 256);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            foreach (var prop in new[] { "message", "summary", "Message", "Summary" })
            {
                if (root.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String)
                {
                    var v = el.GetString();
                    return TruncateForSummary(v ?? "", 256);
                }
            }
        }
        catch
        {
            // Ignore; summary is optional
        }

        return null;
    }

    private static string? TruncateForSummary(string? s, int maxLen)
    {
        if (string.IsNullOrEmpty(s)) return null;
        return s.Length <= maxLen ? s : s[..maxLen] + "...";
    }

    private static string TruncateToMaxBytes(string json)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        if (bytes.Length <= MaxPayloadBytes)
            return json;

        var truncated = System.Text.Encoding.UTF8.GetString(bytes.AsSpan(0, MaxPayloadBytes));
        var lastComplete = truncated.LastIndexOf('}');
        if (lastComplete >= 0)
            return truncated[..(lastComplete + 1)];
        return truncated.TrimEnd();
    }
}

public sealed record RunOutputCaptureResult(
    string? CapturedOutputJson,
    string? CapturedOutputSummary,
    string CapturedOutputStatus,
    int CapturedOutputVersion,
    string? CapturedOutputDisplayPolicy = null);
