using System.Text.Json;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Captures the last step's return value for operator-visible run output. See ADR-0019.
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
        if (value is null)
            return new RunOutputCaptureResult(null, null, StatusEmpty, SchemaVersion);

        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            var bytes = System.Text.Encoding.UTF8.GetByteCount(json);

            if (bytes <= MaxPayloadBytes)
            {
                var summary = TryExtractSummary(value, json);
                return new RunOutputCaptureResult(json, summary, StatusCaptured, SchemaVersion);
            }

            var truncated = TruncateToMaxBytes(json);
            return new RunOutputCaptureResult(truncated, "Output truncated (exceeded max size).", StatusTruncated, SchemaVersion);
        }
        catch
        {
            return new RunOutputCaptureResult(null, null, StatusUnsupported, SchemaVersion);
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
    int CapturedOutputVersion);
