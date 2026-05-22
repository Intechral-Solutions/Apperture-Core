using System.Text.Json;

namespace Apperture.Core.Application.PlatformEvents;

/// <summary>Static v1 platform event catalog (Epic 77 Phase 1).</summary>
public sealed class PlatformEventCatalog : IPlatformEventCatalog
{
    public const int DefaultMaxDataJsonLength = 16_384;

    private static readonly IReadOnlyList<PlatformEventTypeDescriptor> Descriptors =
    [
        new PlatformEventTypeDescriptor(
            PlatformEventTypes.ScheduleExpectedRunMissing,
            PlatformEventSources.PlatformReadiness,
            [PlatformEventSeverities.Warning, PlatformEventSeverities.Error],
            RequiresIntegrationId: true,
            RequiresScheduleId: false,
            RequiredDataProperties:
            [
                PlatformEventDataProperties.EvaluationStatus,
                PlatformEventDataProperties.OperatorSummaryLine,
            ],
            DefaultMaxDataJsonLength),
        new PlatformEventTypeDescriptor(
            PlatformEventTypes.ScheduleExpectedRunCleared,
            PlatformEventSources.PlatformReadiness,
            [PlatformEventSeverities.Info],
            RequiresIntegrationId: true,
            RequiresScheduleId: false,
            RequiredDataProperties:
            [
                PlatformEventDataProperties.EvaluationStatus,
                PlatformEventDataProperties.ResolutionReason,
            ],
            DefaultMaxDataJsonLength),
    ];

    private readonly Dictionary<string, PlatformEventTypeDescriptor> _byType =
        Descriptors.ToDictionary(d => d.Type, StringComparer.Ordinal);

    public bool IsKnownType(string eventType) =>
        !string.IsNullOrWhiteSpace(eventType) && _byType.ContainsKey(eventType);

    public PlatformEventTypeDescriptor GetRequired(string eventType) =>
        _byType.TryGetValue(eventType, out var d)
            ? d
            : throw new PlatformEventValidationException($"Unknown platform event type '{eventType}'.");

    public IReadOnlyList<PlatformEventTypeDescriptor> GetAll() => Descriptors;

    /// <summary>Validates an emit request against a type descriptor (module harness and emitters).</summary>
    public static void ValidateEmitRequest(PlatformEventEmitRequest request, PlatformEventTypeDescriptor descriptor) =>
        ValidateRequest(request, descriptor);

    internal static void ValidateRequest(PlatformEventEmitRequest request, PlatformEventTypeDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(request.Type))
            throw new PlatformEventValidationException("Event type is required.");

        if (!string.Equals(request.Type, descriptor.Type, StringComparison.Ordinal))
            throw new PlatformEventValidationException($"Event type '{request.Type}' does not match descriptor '{descriptor.Type}'.");

        if (string.IsNullOrWhiteSpace(request.Source))
            throw new PlatformEventValidationException("Event source is required.");

        if (!string.Equals(request.Source, descriptor.DefaultSource, StringComparison.Ordinal))
            throw new PlatformEventValidationException(
                $"Event source must be '{descriptor.DefaultSource}' for type '{descriptor.Type}'.");

        if (string.IsNullOrWhiteSpace(request.Severity)
            || !descriptor.AllowedSeverities.Contains(request.Severity, StringComparer.Ordinal))
        {
            throw new PlatformEventValidationException(
                $"Severity '{request.Severity}' is not allowed for type '{descriptor.Type}'.");
        }

        if (descriptor.RequiresIntegrationId && string.IsNullOrWhiteSpace(request.IntegrationId))
            throw new PlatformEventValidationException($"integrationId is required for type '{descriptor.Type}'.");

        if (descriptor.RequiresScheduleId && string.IsNullOrWhiteSpace(request.ScheduleId))
            throw new PlatformEventValidationException($"scheduleId is required for type '{descriptor.Type}'.");

        if (descriptor.RequiresRunId && string.IsNullOrWhiteSpace(request.RunId))
            throw new PlatformEventValidationException($"runId is required for type '{descriptor.Type}'.");

        if (string.IsNullOrWhiteSpace(request.DataJson))
            throw new PlatformEventValidationException("dataJson is required.");

        if (request.DataJson.Length > descriptor.MaxDataJsonLength)
        {
            throw new PlatformEventValidationException(
                $"dataJson exceeds maximum length of {descriptor.MaxDataJsonLength} characters.");
        }

        ValidateDataProperties(request.DataJson, descriptor);
    }

    private static void ValidateDataProperties(string dataJson, PlatformEventTypeDescriptor descriptor)
    {
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new PlatformEventValidationException("dataJson root must be a JSON object.");

            foreach (var prop in descriptor.RequiredDataProperties)
            {
                if (!doc.RootElement.TryGetProperty(prop, out var el)
                    || el.ValueKind == JsonValueKind.Null
                    || (el.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(el.GetString())))
                {
                    throw new PlatformEventValidationException(
                        $"dataJson missing required property '{prop}' for type '{descriptor.Type}'.");
                }
            }
        }
        catch (JsonException ex)
        {
            throw new PlatformEventValidationException("dataJson is not valid JSON.", ex);
        }
    }
}
