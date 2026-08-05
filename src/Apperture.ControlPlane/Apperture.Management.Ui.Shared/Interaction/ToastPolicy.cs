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
    /// Resolves how long a toast stays visible. Errors return <c>null</c> (persist until
    /// dismissed) so a failure cannot disappear before it is read.
    /// </summary>
    public static TimeSpan? ResolveDuration(ToastSeverity severity, TimeSpan? requested)
    {
        if (requested is { } explicitDuration && explicitDuration > TimeSpan.Zero)
            return explicitDuration;

        return severity == ToastSeverity.Error ? null : DefaultDuration;
    }

    /// <summary>Errors are announced assertively; everything else politely.</summary>
    public static string ResolveAriaLive(ToastSeverity severity) =>
        severity == ToastSeverity.Error ? "assertive" : "polite";

    /// <summary>
    /// Identity used to suppress duplicate announcements. Repeating the same outcome
    /// re-arms the existing toast instead of stacking another announcement.
    /// </summary>
    public static string BuildSignature(ToastSeverity severity, string message, string? title) =>
        string.Join('\u001f', severity.ToString(), title?.Trim() ?? "", message?.Trim() ?? "");
}
