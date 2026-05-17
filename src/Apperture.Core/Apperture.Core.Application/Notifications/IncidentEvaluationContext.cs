using System.Text.Json;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.Scheduling;

namespace Apperture.Core.Application.Notifications;

/// <summary>Incident-transition-driven evaluation input (e.g. reminder on ObservedStillActive).</summary>
public sealed record IncidentEvaluationContext(
    string IntegrationId,
    AttentionIncidentObserveTransition Transition,
    string? IncidentId,
    ScheduleExpectationSnapshot? Snapshot,
    DateTimeOffset ObservedAtUtc)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Synthetic platform event for index-stage evaluation when no envelope was appended
    /// (e.g. reminder on ObservedStillActive).
    /// </summary>
    public PlatformEventEnvelope ToSyntheticPlatformEvent()
    {
        var ev = Snapshot?.Evaluation;
        var data = new Dictionary<string, object?>();
        if (ev is not null)
        {
            data[PlatformEventDataProperties.EvaluationStatus] = ev.Status.ToString();
            data[PlatformEventDataProperties.OperatorSummaryLine] = ev.OperatorSummaryLine ?? "";
            data[PlatformEventDataProperties.MostRecentExpectedFireUtc] = ev.MostRecentExpectedFireUtc;
            data[PlatformEventDataProperties.ReferenceScheduleId] = Snapshot?.ReferenceScheduleId;
        }

        if (!string.IsNullOrWhiteSpace(IncidentId))
            data[PlatformEventDataProperties.AttentionIncidentId] = IncidentId;

        return new PlatformEventEnvelope(
            PlatformEventId.New(),
            PlatformEventTypes.ScheduleExpectedRunMissing,
            PlatformEventSources.PlatformReadiness,
            "1.0",
            "application/json",
            JsonSerializer.Serialize(data, JsonOptions),
            ObservedAtUtc,
            ObservedAtUtc,
            PlatformEventSeverities.Warning,
            Subject: $"{IntegrationId}:expected_run_missing",
            DedupeKey: $"{IntegrationId}:expected_run_missing",
            IntegrationId: IntegrationId,
            ModuleId: null,
            ProcedureId: null,
            RunId: null,
            ScheduleId: Snapshot?.ReferenceScheduleId,
            ActorId: null,
            CorrelationId: null,
            CausationId: null);
    }
}
