namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Maps a Notification severity token (a free-form backend string, e.g. <c>"critical"</c>,
/// <c>"error"</c>, <c>"warning"</c>, <c>"info"</c>) onto a <see cref="ToastSeverity"/> plus an
/// optional explicit duration. <c>critical</c>/<c>error</c> resolve to <see cref="ToastSeverity.Error"/>
/// with an explicit 10-second duration, so Notification-originated error toasts auto-dismiss
/// (unlike today's feature-code error toasts, which persist until dismissed per
/// <see cref="ToastPolicy.ResolveDuration"/>). <c>warning</c>/<c>info</c> resolve to
/// <see cref="ToastSeverity.Warning"/>/<see cref="ToastSeverity.Info"/> with no explicit duration
/// (the normal default applies). Anything null, empty, or unrecognized resolves to
/// <see cref="ToastSeverity.Info"/>.
/// </summary>
/// <remarks>
/// The token literals below intentionally mirror <c>PlatformEventSeverities</c>
/// (<c>Apperture.Core.Application.PlatformEvents</c>) as local constants rather than a direct
/// reference: this project (<c>Apperture.Management.Ui.Shared</c>) has no <c>ProjectReference</c>
/// to any <c>Apperture.Core.*</c> project today (verified by grepping both this project's and
/// <c>Apperture.Management.Ui.Client</c>'s <c>.csproj</c> files and their source for existing
/// <c>Apperture.Core.*</c> usages before writing this file), and adding one solely for four string
/// constants was judged not worth the new cross-project dependency.
/// </remarks>
public static class NotificationToastSeverityMapper
{
    /// <summary>
    /// This type deliberately accepts only a raw severity token, never anything shaped like a
    /// Notification *kind* (e.g. "resolved"). A notification kind is orthogonal to severity, and
    /// admitting one here would create a path for "resolved" to influence toast presentation,
    /// which must never happen: nothing this mapper produces may ever be
    /// <see cref="ToastSeverity.Success"/> (see the switch below — no arm returns it).
    /// </summary>
    public static NotificationToastPresentation Resolve(string? severityToken) =>
        Parse(severityToken) switch
        {
            KnownNotificationSeverity.Critical => new NotificationToastPresentation(ToastSeverity.Error, TimeSpan.FromSeconds(10)),
            KnownNotificationSeverity.Error => new NotificationToastPresentation(ToastSeverity.Error, TimeSpan.FromSeconds(10)),
            KnownNotificationSeverity.Warning => new NotificationToastPresentation(ToastSeverity.Warning, null),
            KnownNotificationSeverity.Info => new NotificationToastPresentation(ToastSeverity.Info, null),
            KnownNotificationSeverity.Unknown => new NotificationToastPresentation(ToastSeverity.Info, null),

            // Deliberately NO discard (_) arm. This is the exhaustiveness guard, and it is a
            // two-layer guarantee, not one:
            //
            //   1. Compile time: the C# compiler flags a switch expression over a closed enum that
            //      does not cover every declared case (CS8509) when a case is missing. In this repo
            //      CS8509 is a warning by default (Directory.Build.props sets
            //      TreatWarningsAsErrors=false solution-wide), so on its own it would be easy to
            //      miss. Apperture.Management.Ui.Shared.csproj therefore scopes
            //      <WarningsAsErrors>CS8509</WarningsAsErrors> to itself only (not repo-wide, not
            //      Directory.Build.props) so that, in THIS project specifically, an unhandled case
            //      here fails the build rather than merely warning. (Verified empirically before
            //      applying: building this project with that property added, before this file
            //      existed, produced 0 warnings/0 errors, i.e. no other switch expression in this
            //      project was relying on non-exhaustive-but-warning-only compilation.)
            //   2. Runtime backstop: even without the project-level elevation, an unmatched case in
            //      a switch expression with no discard arm throws
            //      System.Runtime.CompilerServices.SwitchExpressionException at runtime — this is
            //      built into the C# language itself, not something implemented here. So a future
            //      severity added to KnownNotificationSeverity without a corresponding arm can never
            //      silently fall through to Info: it either fails the build (layer 1) or throws
            //      (layer 2). It can never silently resolve to a guessed/fallback severity.
        };

    private static KnownNotificationSeverity Parse(string? severityToken) => severityToken?.Trim().ToLowerInvariant() switch
    {
        // Local literal constants: see the class remarks above for why these are not sourced from
        // Apperture.Core.Application.PlatformEvents directly. Kept in sync manually; values
        // confirmed against that class's Critical/Error/Warning/Info constants at the time of
        // writing.
        "critical" => KnownNotificationSeverity.Critical,
        "error" => KnownNotificationSeverity.Error,
        "warning" => KnownNotificationSeverity.Warning,
        "info" => KnownNotificationSeverity.Info,

        // This switch DOES need (and correctly has) a discard arm: it converts an open-ended
        // string? into the closed KnownNotificationSeverity enum, and "unrecognized string ->
        // Unknown" is the correct, spec-required behavior for that conversion — not the same
        // anti-pattern as a discard arm over the closed enum itself would be.
        _ => KnownNotificationSeverity.Unknown,
    };
}

/// <summary>
/// Closed set of severities this mapper recognizes. Internal: this is an implementation detail of
/// the token-parsing step, not a public contract — callers only ever see
/// <see cref="NotificationToastPresentation"/>.
/// </summary>
internal enum KnownNotificationSeverity
{
    Critical,
    Error,
    Warning,
    Info,
    Unknown,
}

/// <summary>Resolved toast presentation for a Notification severity token.</summary>
/// <param name="Severity">Never <see cref="ToastSeverity.Success"/> — see <see cref="NotificationToastSeverityMapper"/>.</param>
/// <param name="Duration">
/// Explicit duration when the spec calls for one (10s for Critical/Error); <c>null</c> otherwise,
/// meaning the caller/<see cref="ToastPolicy"/> default applies.
/// </param>
public readonly record struct NotificationToastPresentation(ToastSeverity Severity, TimeSpan? Duration);
