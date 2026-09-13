using System.Text.Json;
using Apperture.Core.Application.OperationItems;

namespace Apperture.ControlPlane.Security.DemoBusinessRecords;

/// <summary>Pure helpers for BDS-6 activity filtering and mapping.</summary>
public static class DemoBusinessRecordActivityMapper
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public static readonly int[] LookbackDayOptions = [7, 30, 90];

    public static int ClampTake(int take) =>
        take <= 0 ? DefaultPageSize : Math.Min(take, MaxPageSize);

    public static int ClampSkip(int skip) => Math.Max(0, skip);

    public static bool IsAttentionItem(
        string status,
        bool isRetryable = false,
        bool hasActiveAcknowledgement = false,
        bool hasSucceededRetry = false)
    {
        _ = isRetryable; // retained for call-site compatibility; attention is outcome + ack + retry success.
        return OperationItemAttentionRules.NeedsAttention(status, hasActiveAcknowledgement, hasSucceededRetry);
    }

    public static string? ModuleIdForSourceKind(string? sourceKind) =>
        sourceKind?.Trim().ToLowerInvariant() switch
        {
            DemoBusinessRecordSourceKinds.FileImport => "Sample.FileImport",
            DemoBusinessRecordSourceKinds.ApiPolling => "Sample.ApiPolling",
            _ => null,
        };

    public static bool MatchesCustomerRecordMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return false;

        // Lightweight prefilter: confirmed customer recordType (business key may live on SubjectKey).
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (!doc.RootElement.TryGetProperty(OperationItemBusinessMetadataKeys.RecordType, out var rt))
                return false;
            return SharedRecordIdentityRules.RecordTypeComparer.Equals(
                rt.GetString(),
                SharedRecordTypes.Customer);
        }
        catch (JsonException)
        {
            return metadataJson.Contains(
                MetadataContainsCustomerRecordLiteral,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string MetadataContainsCustomerRecordLiteral { get; } =
        $"\"{OperationItemBusinessMetadataKeys.RecordType}\":\"{OperationItemBusinessMetadataKeys.CustomerRecordType}\"";

    public static DateTimeOffset ResolveOccurredAt(
        DateTimeOffset? completedAtUtc,
        DateTimeOffset createdAtUtc,
        string? metadataJson)
    {
        if (!string.IsNullOrWhiteSpace(metadataJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(metadataJson);
                if (doc.RootElement.TryGetProperty(OperationItemBusinessMetadataKeys.ObservedAt, out var obsEl))
                {
                    if (obsEl.ValueKind == JsonValueKind.String
                        && DateTimeOffset.TryParse(obsEl.GetString(), out var parsed))
                        return parsed;
                    try { return obsEl.GetDateTimeOffset(); }
                    catch (InvalidOperationException) { /* keep fallback */ }
                    catch (FormatException) { /* keep fallback */ }
                }
            }
            catch (JsonException)
            {
                // keep fallback
            }
        }

        return completedAtUtc ?? createdAtUtc;
    }

    public static string? ReadChangeKind(string? metadataJson, string? changeSummaryJson)
    {
        var fromMeta = ReadString(metadataJson, OperationItemBusinessMetadataKeys.ChangeKind);
        if (!string.IsNullOrWhiteSpace(fromMeta))
            return fromMeta;

        return ReadString(changeSummaryJson, OperationItemBusinessMetadataKeys.ChangeKind);
    }

    public static string ResolveIntegrationLabel(
        string? integrationDisplayName,
        string? groupSourceLabel,
        string sourceKindLabel) =>
        !string.IsNullOrWhiteSpace(integrationDisplayName)
            ? integrationDisplayName.Trim()
            : !string.IsNullOrWhiteSpace(groupSourceLabel)
                ? groupSourceLabel.Trim()
                : sourceKindLabel;

    public static string ResolveOperationLabel(string? operationKind, string? changeKind)
    {
        if (!string.IsNullOrWhiteSpace(changeKind))
            return HumanizeToken(changeKind);
        if (!string.IsNullOrWhiteSpace(operationKind))
            return HumanizeToken(operationKind);
        return "Customer update";
    }

    public static string HumanizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return value.Trim().Replace('_', ' ');
    }

    static string? ReadString(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(property, out var el) || el.ValueKind != JsonValueKind.String)
                return null;
            var s = el.GetString();
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
