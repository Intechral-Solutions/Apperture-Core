namespace Apperture.Management.Ui.Shared;

/// <summary>
/// UIX-6 — shared toast behavior so transient feedback is consistent across surfaces.
/// Toasts carry outcomes the operator does not need to refer back to; errors that require
/// remediation stay visible until dismissed and should also appear as inline feedback.
/// </summary>
public static class ToastPolicy
{
    /// <summary>Default visible duration for non-error toasts.</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Ceiling for an explicit duration. Timers cannot be armed beyond ~49 days, so an absurd caller
    /// value is clamped instead of throwing in the middle of an enqueue.
    /// </summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(24);

    /// <summary>
    /// Resolves how long a toast stays visible. Errors return <c>null</c> (persist until
    /// dismissed) so a failure cannot disappear before it is read.
    /// </summary>
    public static TimeSpan? ResolveDuration(ToastSeverity severity, TimeSpan? requested)
    {
        if (requested is { } explicitDuration && explicitDuration > TimeSpan.Zero)
            return explicitDuration < MaxDuration ? explicitDuration : MaxDuration;

        return severity == ToastSeverity.Error ? null : DefaultDuration;
    }

    /// <summary>
    /// How long a leaving toast stays in the stack so its exit animation can play before it is removed.
    /// Must equal <c>--ui-toast-exit-duration</c> in <c>management-ui-primitives.css</c> (guarded by a test);
    /// removal is driven by this timer, never by animation events, so it cannot race the animation.
    /// </summary>
    public static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Safety bound on toasts held in memory. Viewport height clips and scrolls the stack; this is a
    /// separate, generous ceiling that only a runaway caller reaches. When exceeded the oldest
    /// auto-dismissing toast is evicted first, and persistent (error) toasts are evicted last.
    /// </summary>
    public const int MaxToasts = 100;

    /// <summary>How long a screen-reader announcement stays in the live region before it is cleared.</summary>
    public static readonly TimeSpan AnnouncementLifetime = TimeSpan.FromSeconds(5);

    /// <summary>Errors are announced assertively; everything else politely.</summary>
    public static string ResolveAriaLive(ToastSeverity severity) =>
        severity == ToastSeverity.Error ? "assertive" : "polite";

    /// <summary>Text cue for severity, so colour and icon are never the only signal.</summary>
    public static string ResolveSeverityLabel(ToastSeverity severity) => severity switch
    {
        ToastSeverity.Success => "Success",
        ToastSeverity.Info => "Information",
        ToastSeverity.Warning => "Warning",
        ToastSeverity.Error => "Error",
        _ => "Notice"
    };

    /// <summary>The single string a screen reader hears when a toast appears.</summary>
    public static string BuildAnnouncement(ToastSeverity severity, string message, string? title)
    {
        var label = ResolveSeverityLabel(severity);
        var heading = title?.Trim();
        var body = message?.Trim() ?? "";
        return string.IsNullOrEmpty(heading)
            ? $"{label}: {body}"
            : $"{label}: {heading}. {body}";
    }

    /// <summary>
    /// Identity used to suppress duplicate announcements. Repeating the same outcome
    /// re-arms the existing toast instead of stacking another announcement.
    /// </summary>
    public static string BuildSignature(ToastSeverity severity, string message, string? title) =>
        string.Join('\u001f', severity.ToString(), title?.Trim() ?? "", message?.Trim() ?? "");
}
