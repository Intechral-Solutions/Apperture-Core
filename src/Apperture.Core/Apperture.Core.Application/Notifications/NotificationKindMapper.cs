using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformEvents;

namespace Apperture.Core.Application.Notifications;

/// <summary>Maps evaluation inputs to notification kind / template variant. Epic 77 Phase 4.</summary>
public static class NotificationKindMapper
{
    public static string? MapNotificationKind(NotificationRuleEvaluationContext context) =>
        MapNotificationKinds(context).FirstOrDefault();

    public static IReadOnlyList<string> MapNotificationKinds(NotificationRuleEvaluationContext context)
    {
        if (context.SourceKind == NotificationEvaluationSourceKind.IncidentTransition)
        {
            var incident = context.Incident
                ?? throw new InvalidOperationException("Incident context is required.");
            return incident.Transition switch
            {
                AttentionIncidentObserveTransition.ObservedStillActive =>
                    [NotificationKinds.Reminder, NotificationKinds.Escalation],
                AttentionIncidentObserveTransition.Opened => [NotificationKinds.Activation],
                AttentionIncidentObserveTransition.Resolved => [NotificationKinds.Resolved],
                _ => [],
            };
        }

        var envelope = context.PlatformEvent
            ?? throw new InvalidOperationException("Platform event is required.");
        if (envelope.Type == PlatformEventTypes.ScheduleExpectedRunMissing)
            return [NotificationKinds.Activation];
        if (envelope.Type == PlatformEventTypes.ScheduleExpectedRunCleared)
            return [NotificationKinds.Resolved];

        if (envelope.Type.StartsWith("apperture.module.", StringComparison.Ordinal))
            return [NotificationKinds.Activation];

        return [];
    }

    /// <summary>Reminder and escalation cannot be produced from platform-event append alone.</summary>
    public static bool IsAllowedForSource(string notificationKind, NotificationRuleEvaluationContext context)
    {
        if (context.SourceKind == NotificationEvaluationSourceKind.PlatformEvent
            && notificationKind is NotificationKinds.Reminder or NotificationKinds.Escalation)
        {
            return false;
        }

        return true;
    }
}
