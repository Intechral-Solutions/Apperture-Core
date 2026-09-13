using Apperture.Core.Application.PlatformEvents;

namespace Apperture.Core.Application.Notifications;

public sealed record NotificationRuleEvaluationContext(
    NotificationEvaluationSourceKind SourceKind,
    PlatformEventEnvelope? PlatformEvent,
    IncidentEvaluationContext? Incident,
    DateTimeOffset EvaluatedAtUtc)
{
    public PlatformEventEnvelope EffectivePlatformEvent =>
        PlatformEvent
        ?? Incident?.ToSyntheticPlatformEvent()
        ?? throw new InvalidOperationException("Evaluation context requires a platform event or incident context.");
}
