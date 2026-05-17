using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformEvents;

namespace Apperture.Core.Application.Notifications;

/// <summary>Maps evaluation inputs to notification kind / template variant. Epic 77 Phase 4.</summary>
public static class NotificationKindMapper
{
    public static string? MapNotificationKind(NotificationRuleEvaluationContext context)
    {
        if (context.SourceKind == NotificationEvaluationSourceKind.IncidentTransition)
        {
            var incident = context.Incident
                ?? throw new InvalidOperationException("Incident context is required.");
            return incident.Transition switch
            {
                AttentionIncidentObserveTransition.ObservedStillActive => NotificationKinds.Reminder,
                AttentionIncidentObserveTransition.Opened => NotificationKinds.Activation,
                AttentionIncidentObserveTransition.Resolved => NotificationKinds.Resolved,
                _ => null,
            };
        }

        var envelope = context.PlatformEvent
            ?? throw new InvalidOperationException("Platform event is required.");
        if (envelope.Type == PlatformEventTypes.ScheduleExpectedRunMissing)
            return NotificationKinds.Activation;
        if (envelope.Type == PlatformEventTypes.ScheduleExpectedRunCleared)
            return NotificationKinds.Resolved;
        return null;
    }

    /// <summary>Reminder cannot be produced from platform-event append alone.</summary>
    public static bool IsAllowedForSource(string notificationKind, NotificationRuleEvaluationContext context)
    {
        if (notificationKind == NotificationKinds.Reminder &&
            context.SourceKind == NotificationEvaluationSourceKind.PlatformEvent)
            return false;
        return true;
    }
}
