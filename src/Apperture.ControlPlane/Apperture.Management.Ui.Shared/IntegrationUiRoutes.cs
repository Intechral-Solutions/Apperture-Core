namespace Apperture.Management.Ui.Shared;

/// <summary>
/// One tab of the Integration Operations Hub (HUB-1). Mirrors <see cref="AccountSection"/> — a
/// real link, not a client-side view switch, so every section stays bookmarkable and behaves
/// like normal navigation for keyboard and screen-reader users.
/// </summary>
public sealed record IntegrationHubSection(string Key, string Label, string Href, string Description);

/// <summary>
/// Canonical Integration Operations Hub (HUB-1) routes. Showing a related resource under an
/// Integration here does not make the Integration its domain owner — Connections and Schedules
/// keep their own models; this only relocates/presents them in an Integration-scoped context.
/// Business Data and Services are deliberately not represented here (later ResourceContext work).
/// </summary>
public static class IntegrationUiRoutes
{
    public const string SectionOverview = "overview";
    public const string SectionConfiguration = "configuration";
    public const string SectionConnections = "connections";
    public const string SectionSchedules = "schedules";
    public const string SectionRuns = "runs";
    public const string SectionNotifications = "notifications";

    public static string Detail(string integrationId) =>
        $"/integrations/{Uri.EscapeDataString(integrationId)}";

    public static string Settings(string integrationId) =>
        $"/integrations/{Uri.EscapeDataString(integrationId)}/settings";

    public static string Connections(string integrationId) =>
        $"/integrations/{Uri.EscapeDataString(integrationId)}/connections";

    public static string Schedules(string integrationId) =>
        $"/integrations/{Uri.EscapeDataString(integrationId)}/schedules";

    public static string Runs(string integrationId) =>
        $"/integrations/{Uri.EscapeDataString(integrationId)}/runs";

    public static string Notifications(string integrationId) =>
        NotificationUiRoutes.IntegrationNotifications(integrationId);

    /// <summary>Deep link only — not a hub tab. See <see cref="ResolveSectionKey"/>.</summary>
    public static string ScheduleDiagnostics(string integrationId) =>
        NotificationUiRoutes.IntegrationScheduleDiagnostics(integrationId);

    /// <summary>
    /// The six HUB-1 sections for a given Integration, in canonical order. Notifications is
    /// included unconditionally here; callers filter it out client-side when the operator lacks
    /// <c>ViewNotificationDefinitions</c> (menu/tab hiding is not the authorization boundary --
    /// the Notifications page and API enforce independently).
    /// </summary>
    public static IReadOnlyList<IntegrationHubSection> Sections(string integrationId) =>
    [
        new(SectionOverview, "Overview", Detail(integrationId), "Status, readiness, and quick actions."),
        new(SectionConfiguration, "Configuration", Settings(integrationId), "Typed instance settings for this integration."),
        new(SectionConnections, "Connections", Connections(integrationId), "Connection requirements and bindings for this integration."),
        new(SectionSchedules, "Schedules", Schedules(integrationId), "Recurring workflow triggers owned by this integration."),
        new(SectionRuns, "Runs", Runs(integrationId), "Execution history for this integration."),
        new(SectionNotifications, "Notifications", Notifications(integrationId), "Integration-scoped notification configuration."),
    ];

    /// <summary>
    /// Resolves which hub section (if any) owns <paramref name="path"/>. Deep links that are
    /// deliberately not hub tabs (schedule diagnostics, notification definition editors, the
    /// legacy "new" route) resolve to <c>null</c> -- no tab highlights, matching that they are
    /// reached by drill-in, not primary navigation.
    /// </summary>
    public static string? ResolveSectionKey(string? path)
    {
        var segments = NormalizeSegments(path);
        if (segments.Length < 2 || !string.Equals(segments[0], "integrations", StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.Equals(segments[1], "new", StringComparison.OrdinalIgnoreCase))
            return null;
        if (segments.Length == 2)
            return SectionOverview;

        return segments[2].ToLowerInvariant() switch
        {
            "settings" => SectionConfiguration,
            "connections" => SectionConnections,
            "schedules" => SectionSchedules,
            "runs" => SectionRuns,
            "notifications" => SectionNotifications,
            _ => null
        };
    }

    private static string[] NormalizeSegments(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return [];

        var value = path.Trim();
        var cut = value.IndexOfAny(['?', '#']);
        if (cut >= 0)
            value = value[..cut];

        return value.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }
}
