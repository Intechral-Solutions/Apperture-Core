using System.Text.Json;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Domain.Integration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Emits schedule-expectation platform events when an Attention incident opens or resolves.
/// Does not send email. Reminder and escalation instances are created from still-active observation, not from these events.
/// </summary>
public sealed class ScheduleExpectationPlatformEventEmitter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IPlatformEventEmitter _emitter;
    private readonly IOptions<PlatformEventsOptions> _options;
    private readonly ILogger<ScheduleExpectationPlatformEventEmitter> _logger;

    public ScheduleExpectationPlatformEventEmitter(
        IPlatformEventEmitter emitter,
        IOptions<PlatformEventsOptions> options,
        ILogger<ScheduleExpectationPlatformEventEmitter> logger)
    {
        _emitter = emitter;
        _options = options;
        _logger = logger;
    }

    public async ValueTask TryEmitForObserveResultAsync(
        IntegrationId integrationId,
        AttentionIncidentObserveResult observe,
        ScheduleExpectationSnapshot snapshot,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Value.Enabled)
            return;

        try
        {
            switch (observe.Transition)
            {
                case AttentionIncidentObserveTransition.Opened when observe.IncidentId is { } openedId:
                    await EmitMissingAsync(integrationId, snapshot, observedAtUtc, openedId, observe.ResourceContextId, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case AttentionIncidentObserveTransition.Resolved when observe.IncidentId is { } resolvedId:
                    await EmitClearedAsync(integrationId, snapshot, observedAtUtc, resolvedId, observe.ResourceContextId, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case AttentionIncidentObserveTransition.NoChange:
                case AttentionIncidentObserveTransition.ObservedStillActive:
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Schedule-expectation platform event emit failed for integration {IntegrationId} transition {Transition}.",
                integrationId.Value,
                observe.Transition);
        }
    }

    private async ValueTask EmitMissingAsync(
        IntegrationId integrationId,
        ScheduleExpectationSnapshot snapshot,
        DateTimeOffset observedAtUtc,
        string incidentId,
        string? resourceContextId,
        CancellationToken cancellationToken)
    {
        var ev = snapshot.Evaluation;
        int? missingMinutes = null;
        if (ev.MostRecentExpectedFireUtc is { } fire && observedAtUtc > fire)
            missingMinutes = (int)Math.Floor((observedAtUtc - fire).TotalMinutes);

        var data = new Dictionary<string, object?>
        {
            [PlatformEventDataProperties.EvaluationStatus] = ev.Status.ToString(),
            [PlatformEventDataProperties.OperatorSummaryLine] = ev.OperatorSummaryLine ?? "",
            [PlatformEventDataProperties.MostRecentExpectedFireUtc] = ev.MostRecentExpectedFireUtc,
            [PlatformEventDataProperties.ReferenceScheduleId] = snapshot.ReferenceScheduleId,
            [PlatformEventDataProperties.MissingMinutes] = missingMinutes,
            [PlatformEventDataProperties.AttentionIncidentId] = incidentId,
        };

        var subject = $"{integrationId.Value}:expected_run_missing";
        var request = new PlatformEventEmitRequest(
            Type: PlatformEventTypes.ScheduleExpectedRunMissing,
            Source: PlatformEventSources.PlatformReadiness,
            DataJson: JsonSerializer.Serialize(data, JsonOptions),
            OccurredAtUtc: observedAtUtc,
            Severity: PlatformEventSeverities.Warning,
            Subject: subject,
            DedupeKey: subject,
            IntegrationId: integrationId.Value,
            ScheduleId: snapshot.ReferenceScheduleId,
            ResourceContextId: resourceContextId);

        await _emitter.EmitAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask EmitClearedAsync(
        IntegrationId integrationId,
        ScheduleExpectationSnapshot snapshot,
        DateTimeOffset observedAtUtc,
        string incidentId,
        string? resourceContextId,
        CancellationToken cancellationToken)
    {
        var data = new Dictionary<string, object?>
        {
            [PlatformEventDataProperties.EvaluationStatus] = snapshot.Evaluation.Status.ToString(),
            [PlatformEventDataProperties.ResolutionReason] = AttentionIncidentResolutionReasons.ExpectationCleared,
            [PlatformEventDataProperties.AttentionIncidentId] = incidentId,
        };

        var request = new PlatformEventEmitRequest(
            Type: PlatformEventTypes.ScheduleExpectedRunCleared,
            Source: PlatformEventSources.PlatformReadiness,
            DataJson: JsonSerializer.Serialize(data, JsonOptions),
            OccurredAtUtc: observedAtUtc,
            Severity: PlatformEventSeverities.Info,
            Subject: $"{integrationId.Value}:expected_run_missing:cleared",
            IntegrationId: integrationId.Value,
            ScheduleId: snapshot.ReferenceScheduleId,
            ResourceContextId: resourceContextId);

        await _emitter.EmitAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
