using System.Text.Json;
using Apperture.Core.Application.Execution;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Captures operator-safe failure diagnostics from execution exceptions.
/// See Epic 22 diagnostics ADR.
/// </summary>
public static class RunFailureCapture
{
    public const int SchemaVersion = 1;
    public const int MaxPayloadBytes = 32 * 1024;

    public const string StatusNone = "none";
    public const string StatusSummaryOnly = "summary_only";
    public const string StatusCaptured = "captured";
    public const string StatusWithheld = "withheld";
    public const string StatusUnsupported = "unsupported";

    public const string DisplayPolicyInline = "inline";
    public const string DisplayPolicySummaryOnly = "summary_only";
    public const string DisplayPolicyWithheld = "withheld";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static RunFailureCaptureResult TryCapture(Exception ex, string displayPolicy = DisplayPolicySummaryOnly)
    {
        if (ex is OperatorExecutionFailureException declared)
            return TryCaptureDeclaredFailure(declared);

        var summary = Truncate(ex.Message, 512);

        if (string.Equals(displayPolicy, DisplayPolicyWithheld, StringComparison.Ordinal))
            return new RunFailureCaptureResult(summary, null, StatusWithheld, SchemaVersion, DisplayPolicyWithheld);

        if (string.Equals(displayPolicy, DisplayPolicySummaryOnly, StringComparison.Ordinal))
            return new RunFailureCaptureResult(summary, null, StatusSummaryOnly, SchemaVersion, DisplayPolicySummaryOnly);

        try
        {
            var json = JsonSerializer.Serialize(new { error = Truncate(ex.Message, 2048) }, JsonOptions);
            var bytes = System.Text.Encoding.UTF8.GetByteCount(json);
            if (bytes > MaxPayloadBytes)
                json = System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(json).AsSpan(0, MaxPayloadBytes)).TrimEnd();

            return new RunFailureCaptureResult(summary, json, StatusCaptured, SchemaVersion, DisplayPolicyInline);
        }
        catch
        {
            return new RunFailureCaptureResult(summary, null, StatusUnsupported, SchemaVersion, displayPolicy);
        }
    }

    private static RunFailureCaptureResult TryCaptureDeclaredFailure(OperatorExecutionFailureException declared)
    {
        var summary = Truncate(declared.Summary, 512);
        var policy = declared.DisplayPolicy switch
        {
            OperatorDiagnosticsDisplayPolicy.Inline => DisplayPolicyInline,
            OperatorDiagnosticsDisplayPolicy.SummaryOnly => DisplayPolicySummaryOnly,
            OperatorDiagnosticsDisplayPolicy.Withheld => DisplayPolicyWithheld,
            _ => DisplayPolicySummaryOnly
        };

        if (string.Equals(policy, DisplayPolicyWithheld, StringComparison.Ordinal))
            return new RunFailureCaptureResult(summary, null, StatusWithheld, SchemaVersion, policy);

        if (string.Equals(policy, DisplayPolicySummaryOnly, StringComparison.Ordinal))
            return new RunFailureCaptureResult(summary, null, StatusSummaryOnly, SchemaVersion, policy);

        try
        {
            var payload = declared.Diagnostics ?? new { error = Truncate(declared.Summary, 2048) };
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var bytes = System.Text.Encoding.UTF8.GetByteCount(json);
            if (bytes > MaxPayloadBytes)
                json = System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(json).AsSpan(0, MaxPayloadBytes)).TrimEnd();

            return new RunFailureCaptureResult(summary, json, StatusCaptured, SchemaVersion, DisplayPolicyInline);
        }
        catch
        {
            return new RunFailureCaptureResult(summary, null, StatusUnsupported, SchemaVersion, policy);
        }
    }

    private static string Truncate(string? s, int maxLen)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        return s.Length <= maxLen ? s : s[..maxLen];
    }
}

public sealed record RunFailureCaptureResult(
    string FailureSummary,
    string? FailureDiagnosticsJson,
    string FailureDiagnosticsStatus,
    int FailureDiagnosticsVersion,
    string? FailureDiagnosticsDisplayPolicy);
