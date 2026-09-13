using Apperture.Core.Application.Integration;

namespace Apperture.Core.Application.Notifications;

/// <summary>Rule trigger incident transition tokens (camelCase). Epic 77.</summary>
public static class NotificationIncidentTransitions
{
    public const string Opened = "opened";
    public const string ObservedStillActive = "observedStillActive";
    public const string Resolved = "resolved";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Opened, ObservedStillActive, Resolved,
    };

    public static string? FromObserveTransition(AttentionIncidentObserveTransition transition) =>
        transition switch
        {
            AttentionIncidentObserveTransition.Opened => Opened,
            AttentionIncidentObserveTransition.ObservedStillActive => ObservedStillActive,
            AttentionIncidentObserveTransition.Resolved => Resolved,
            _ => null,
        };
}
