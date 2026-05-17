namespace Apperture.Core.Application.Notifications;

/// <summary>Canonical notification intent keys (template variant keys). Epic 77.</summary>
public static class NotificationKinds
{
    public const string Activation = "activation";
    public const string Reminder = "reminder";
    public const string Resolved = "resolved";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Activation, Reminder, Resolved,
    };
}
