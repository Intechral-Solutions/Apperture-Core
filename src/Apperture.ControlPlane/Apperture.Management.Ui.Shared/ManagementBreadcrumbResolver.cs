using Apperture.Management.Ui.Shared.Models;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Resolves the current route path to a compact breadcrumb trail for the shell top bar.
/// Uses explicit route rules (most specific first); no entity-name API lookups.
/// </summary>
public static class ManagementBreadcrumbResolver
{
    private delegate bool PathMatcher(string[] segments, out IReadOnlyList<BreadcrumbItem>? crumbs);

    private static readonly PathMatcher[] Rules =
    [
        MatchIntegrationsNotificationsDefinitionNew,
        MatchIntegrationsScheduleDiagnostics,
        MatchIntegrationsNotifications,
        MatchIntegrationsNew,
        MatchIntegrationsDetail,
        MatchConfigNotificationDefinitionEdit,
        MatchConfigNotificationDefinitionNew,
        MatchConfigNotificationDefinitionDetail,
        MatchConfigNotificationInstanceDetail,
        MatchConfigNotificationDiagnostics,
        MatchConfigNotificationDelivery,
        MatchConfigNotificationInstancesList,
        MatchConfigNotificationDefinitionsList,
        MatchConfigNotificationsLanding,
        MatchConfigGovernanceUserRoleDerivedAccess,
        MatchConfigGovernanceUserDetail,
        MatchConfigGovernanceUsersList,
        MatchConfigGovernanceRoleDetail,
        MatchConfigGovernanceRolesList,
        MatchConfigGovernanceCatalog,
        MatchConfigGovernanceLanding,
        MatchLegacyGovernanceUserRoleDerivedAccess,
        MatchLegacyGovernanceUserDetail,
        MatchLegacyGovernanceUsersList,
        MatchLegacyGovernanceRoleDetail,
        MatchLegacyGovernanceRolesList,
        MatchLegacyGovernanceCatalog,
        MatchConfigAuthSignIn,
        MatchConfigAuthEntraUsers,
        MatchConfigAuthEntraGroups,
        MatchConfigAuthTenants,
        MatchConfigAuthLanding,
        MatchConfigSecuritySignInRedirect,
        MatchConfigContactEditor,
        MatchConfigContactsList,
        MatchModulesDetail,
        MatchModulesList,
        MatchRunsDetail,
        MatchSchedulesNew,
        MatchSchedulesDetail,
        MatchSchedulesList,
        MatchRunsList,
        MatchControlCenter,
        MatchControlBlockedWork,
        MatchControlAudit,
        MatchExecute,
        MatchOperationsStart,
        MatchWorkflowsStart,
        MatchAttentionContactGroupDetail,
        MatchAttentionOperationalTeamDetail,
        MatchAttentionContactGroups,
        MatchAttentionOperationalTeams,
        MatchAttentionRoutingDefaults,
        MatchConnectionsNew,
        MatchConnectionsDetail,
        MatchConnectionsList,
        MatchDashboardLayout,
        MatchDashboardAdd,
        MatchDashboardManage,
        MatchDashboardDelete,
        MatchDashboardHome,
        MatchConfigLanding,
        MatchIntegrationsList,
        MatchRuntimeShutdownRequestDetail,
        MatchRuntimeShutdownRequestsList,
        MatchAccount,
        MatchPreferences,
    ];

    /// <summary>
    /// Resolves breadcrumbs for a request path (e.g. <see cref="Uri.AbsolutePath"/>).
    /// Returns an empty list when the path is excluded or unknown.
    /// </summary>
    public static IReadOnlyList<BreadcrumbItem> Resolve(string path) =>
        ResolveOrientation(path) switch
        {
            { Title: not null } o => [.. o.Parents, new BreadcrumbItem(o.Title, IsCurrent: true)],
            _ => [],
        };

    /// <summary>
    /// Resolves shell orientation: parent crumbs (all linkable), page title from the terminal segment, optional route subtitle.
    /// </summary>
    public static ManagementShellOrientation ResolveOrientation(string path)
    {
        var segments = SplitPath(path);
        if (segments.Length == 0 || IsExcludedPath(segments))
            return ManagementShellOrientation.Empty;

        foreach (var rule in Rules)
        {
            if (!rule(segments, out var crumbs) || crumbs is not { Count: > 0 })
                continue;

            var title = crumbs[^1].Label;
            var parents = crumbs.Count > 1
                ? crumbs.Take(crumbs.Count - 1)
                    .Select(c => new BreadcrumbItem(c.Label, c.Href, IsCurrent: false))
                    .ToList()
                : (IReadOnlyList<BreadcrumbItem>)[];
            return new ManagementShellOrientation(parents, title, ResolveRouteSubtitle(path));
        }

        return ManagementShellOrientation.Empty;
    }

    private static string? ResolveRouteSubtitle(string path)
    {
        var p = path.TrimEnd('/');
        if (p.Length == 0)
            p = "/";

        if (NavPathMatching.IsPathUnder(p, "/control/center"))
            return "Live operational console for targets, activity, and attention.";
        if (p.Equals("/dashboard", StringComparison.OrdinalIgnoreCase))
            return "Platform summary and drill-ins.";
        if (p.Equals("/runs", StringComparison.OrdinalIgnoreCase))
            return "Automation run history.";
        if (p.Equals("/schedules", StringComparison.OrdinalIgnoreCase))
            return "Schedule definitions and firing.";
        if (p.Equals("/integrations", StringComparison.OrdinalIgnoreCase))
            return "Integration fleet overview.";
        if (p.Equals("/connections", StringComparison.OrdinalIgnoreCase))
            return "Connection instances and connector definitions.";
        if (p.Equals("/config", StringComparison.OrdinalIgnoreCase))
            return "Authentication, governance, notifications, contacts, and modules.";
        if (p.Equals("/execute", StringComparison.OrdinalIgnoreCase))
            return "Start operations and workflows.";
        return null;
    }

    private static bool IsExcludedPath(string[] segments) =>
        segments[0].Equals("login", StringComparison.OrdinalIgnoreCase)
        || segments[0].Equals("demo", StringComparison.OrdinalIgnoreCase);

    private static string[] SplitPath(string path)
    {
        var p = path.Trim();
        if (p.Length == 0)
            return [];
        p = p.TrimEnd('/');
        if (p.Length == 0 || p == "/")
            return [];
        return p.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool SegmentEquals(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool IsParam(string segment) =>
        segment.Length > 2 && segment[0] == '{' && segment[^1] == '}';

    private static bool Matches(string[] segments, params string[] template)
    {
        if (segments.Length != template.Length)
            return false;
        for (var i = 0; i < template.Length; i++)
        {
            if (IsParam(template[i]))
                continue;
            if (!SegmentEquals(segments[i], template[i]))
                return false;
        }
        return true;
    }

    private static IReadOnlyList<BreadcrumbItem> Trail(params (string Label, string? Href)[] segments)
    {
        var list = new List<BreadcrumbItem>(segments.Length);
        for (var i = 0; i < segments.Length; i++)
        {
            var (label, href) = segments[i];
            var isLast = i == segments.Length - 1;
            list.Add(new BreadcrumbItem(label, isLast ? null : href, isLast));
        }
        return list;
    }

    private static IReadOnlyList<BreadcrumbItem> Config(params (string Label, string? Href)[] tail) =>
        Trail([(SectionOwnerConstants.Config, ShellSectionRoutes.ConfigHome), .. tail]);

    private static IReadOnlyList<BreadcrumbItem> Operations(params (string Label, string? Href)[] tail) =>
        Trail([("Operations", "/control/center"), .. tail]);

    private static IReadOnlyList<BreadcrumbItem> Automation(params (string Label, string? Href)[] tail) =>
        Trail([("Automation", "/runs"), .. tail]);

    private static IReadOnlyList<BreadcrumbItem> Integrations(params (string Label, string? Href)[] tail) =>
        Trail([("Integrations", "/integrations"), .. tail]);

    private static IReadOnlyList<BreadcrumbItem> Connections(params (string Label, string? Href)[] tail) =>
        Trail([("Connections", "/connections"), .. tail]);

    private static IReadOnlyList<BreadcrumbItem> Notifications(params (string Label, string? Href)[] tail)
    {
        var segments = new (string Label, string? Href)[2 + tail.Length];
        segments[0] = (SectionOwnerConstants.Config, ShellSectionRoutes.ConfigHome);
        segments[1] = ("Notifications", "/config/notifications");
        if (tail.Length > 0)
            Array.Copy(tail, 0, segments, 2, tail.Length);
        return Trail(segments);
    }

    // --- Integrations ---

    private static bool MatchIntegrationsNotificationsDefinitionNew(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "integrations", "{id}", "notifications", "definitions", "new"))
            return false;
        c = Integrations(
            ("Integration", $"/integrations/{s[1]}"),
            ("Notifications", $"/integrations/{s[1]}/notifications"),
            ("New definition", null));
        return true;
    }

    private static bool MatchIntegrationsScheduleDiagnostics(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "integrations", "{id}", "schedule-diagnostics"))
            return false;
        c = Integrations(
            ("Integration", $"/integrations/{s[1]}"),
            ("Schedule diagnostics", null));
        return true;
    }

    private static bool MatchIntegrationsNotifications(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "integrations", "{id}", "notifications"))
            return false;
        c = Integrations(
            ("Integration", $"/integrations/{s[1]}"),
            ("Notifications", null));
        return true;
    }

    private static bool MatchIntegrationsNew(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "integrations", "new"))
            return false;
        c = Integrations(("New", null));
        return true;
    }

    private static bool MatchIntegrationsDetail(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "integrations", "{id}"))
            return false;
        c = Integrations(("Integration", null));
        return true;
    }

    private static bool MatchIntegrationsList(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "integrations"))
            return false;
        c = Trail(("Integrations", null));
        return true;
    }

    // --- Config / notifications ---

    private static bool MatchConfigNotificationDefinitionEdit(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "notifications", "definitions", "{id}", "edit"))
            return false;
        c = Notifications(
            ("Definitions", "/config/notifications/definitions"),
            ("Definition", $"/config/notifications/definitions/{s[3]}"),
            ("Edit", null));
        return true;
    }

    private static bool MatchConfigNotificationDefinitionNew(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "notifications", "definitions", "new"))
            return false;
        c = Notifications(
            ("Definitions", "/config/notifications/definitions"),
            ("New", null));
        return true;
    }

    private static bool MatchConfigNotificationDefinitionDetail(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "notifications", "definitions", "{id}"))
            return false;
        c = Notifications(
            ("Definitions", "/config/notifications/definitions"),
            ("Definition", null));
        return true;
    }

    private static bool MatchConfigNotificationInstanceDetail(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "notifications", "instances", "{id}"))
            return false;
        c = Notifications(
            ("Instances", "/config/notifications/instances"),
            ("Instance", null));
        return true;
    }

    private static bool MatchConfigNotificationDiagnostics(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "notifications", "diagnostics"))
            return false;
        c = Notifications(("Diagnostics", null));
        return true;
    }

    private static bool MatchConfigNotificationDelivery(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "notifications", "delivery"))
            return false;
        c = Notifications(("Delivery", null));
        return true;
    }

    private static bool MatchConfigNotificationInstancesList(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "notifications", "instances"))
            return false;
        c = Notifications(("Instances", null));
        return true;
    }

    private static bool MatchConfigNotificationDefinitionsList(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "notifications", "definitions"))
            return false;
        c = Notifications(("Definitions", null));
        return true;
    }

    private static bool MatchConfigNotificationsLanding(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "notifications"))
            return false;
        c = Config(("Notifications", null));
        return true;
    }

    // --- Config / governance ---

    private static bool MatchConfigGovernanceUserRoleDerivedAccess(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "governance", "users", "{id}", "role-derived-access"))
            return false;
        c = Config(
            ("Governance", "/config/governance"),
            ("Users", "/config/governance/users"),
            ("User", $"/config/governance/users/{s[3]}"),
            ("Role-derived access", null));
        return true;
    }

    private static bool MatchConfigGovernanceUserDetail(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "governance", "users", "{id}"))
            return false;
        c = Config(
            ("Governance", "/config/governance"),
            ("Users", "/config/governance/users"),
            ("User", null));
        return true;
    }

    private static bool MatchConfigGovernanceUsersList(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "governance", "users"))
            return false;
        c = Config(
            ("Governance", "/config/governance"),
            ("Users", null));
        return true;
    }

    private static bool MatchConfigGovernanceRoleDetail(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "governance", "roles", "{id}"))
            return false;
        c = Config(
            ("Governance", "/config/governance"),
            ("Roles", "/config/governance/roles"),
            ("Role", null));
        return true;
    }

    private static bool MatchConfigGovernanceRolesList(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "governance", "roles"))
            return false;
        c = Config(
            ("Governance", "/config/governance"),
            ("Roles", null));
        return true;
    }

    private static bool MatchConfigGovernanceCatalog(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "governance", "catalog"))
            return false;
        c = Config(
            ("Governance", "/config/governance"),
            ("Permission catalog", null));
        return true;
    }

    private static bool MatchConfigGovernanceLanding(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "governance"))
            return false;
        c = Config(("Governance", null));
        return true;
    }

    private static bool MatchLegacyGovernanceUserRoleDerivedAccess(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "governance", "users", "{id}", "effective-access"))
            return false;
        c = Config(
            ("Governance", "/config/governance"),
            ("Users", "/config/governance/users"),
            ("User", $"/config/governance/users/{s[2]}"),
            ("Role-derived access", null));
        return true;
    }

    private static bool MatchLegacyGovernanceUserDetail(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "governance", "users", "{id}"))
            return false;
        c = Config(
            ("Governance", "/config/governance"),
            ("Users", "/config/governance/users"),
            ("User", null));
        return true;
    }

    private static bool MatchLegacyGovernanceUsersList(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "governance", "users"))
            return false;
        c = Config(
            ("Governance", "/config/governance"),
            ("Users", null));
        return true;
    }

    private static bool MatchLegacyGovernanceRoleDetail(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "governance", "roles", "{id}"))
            return false;
        c = Config(
            ("Governance", "/config/governance"),
            ("Roles", "/config/governance/roles"),
            ("Role", null));
        return true;
    }

    private static bool MatchLegacyGovernanceRolesList(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "governance", "roles"))
            return false;
        c = Config(
            ("Governance", "/config/governance"),
            ("Roles", null));
        return true;
    }

    private static bool MatchLegacyGovernanceCatalog(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "governance", "catalog"))
            return false;
        c = Config(
            ("Governance", "/config/governance"),
            ("Permission catalog", null));
        return true;
    }

    // --- Config / auth ---

    private static bool MatchConfigAuthSignIn(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "auth", "sign-in"))
            return false;
        c = Config(
            ("Auth", "/config/auth"),
            ("Sign-in experience", null));
        return true;
    }

    private static bool MatchConfigAuthEntraUsers(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "auth", "entra", "{id}", "users"))
            return false;
        c = Config(
            ("Auth", "/config/auth"),
            ("Directory users", null));
        return true;
    }

    private static bool MatchConfigAuthEntraGroups(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "auth", "entra", "{id}", "groups"))
            return false;
        c = Config(
            ("Auth", "/config/auth"),
            ("Directory groups", null));
        return true;
    }

    private static bool MatchConfigAuthTenants(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "auth", "tenants"))
            return false;
        c = Config(
            ("Auth", "/config/auth"),
            ("Entra tenants", null));
        return true;
    }

    private static bool MatchConfigAuthLanding(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "auth"))
            return false;
        c = Config(("Auth", null));
        return true;
    }

    private static bool MatchConfigSecuritySignInRedirect(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "security", "sign-in"))
            return false;
        c = Config(
            ("Auth", "/config/auth"),
            ("Sign-in experience", null));
        return true;
    }

    // --- Config / contacts ---

    private static bool MatchConfigContactEditor(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "contacts", "{id}"))
            return false;
        c = Config(
            ("Managed contacts", "/config/contacts"),
            ("Contact", null));
        return true;
    }

    private static bool MatchConfigContactsList(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config", "contacts"))
            return false;
        c = Config(("Managed contacts", null));
        return true;
    }

    // --- Modules ---

    private static bool MatchModulesDetail(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "modules", "{id}"))
            return false;
        c = Config(
            ("Modules", "/modules"),
            ("Module", null));
        return true;
    }

    private static bool MatchModulesList(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "modules"))
            return false;
        c = Config(("Modules", null));
        return true;
    }

    // --- Automation ---

    private static bool MatchRunsDetail(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "runs", "{id}"))
            return false;
        c = Automation(
            ("Runs", "/runs"),
            ("Run", null));
        return true;
    }

    private static bool MatchRunsList(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "runs"))
            return false;
        c = Automation(("Runs", null));
        return true;
    }

    private static bool MatchSchedulesNew(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "schedules", "new"))
            return false;
        c = Automation(
            ("Schedules", "/schedules"),
            ("New", null));
        return true;
    }

    private static bool MatchSchedulesDetail(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "schedules", "{id}"))
            return false;
        c = Automation(
            ("Schedules", "/schedules"),
            ("Schedule", null));
        return true;
    }

    private static bool MatchSchedulesList(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "schedules"))
            return false;
        c = Automation(("Schedules", null));
        return true;
    }

    // --- Operations ---

    private static bool MatchControlCenter(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "control", "center"))
            return false;
        c = Operations(("Control Center", null));
        return true;
    }

    private static bool MatchControlBlockedWork(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "control", "blocked-work"))
            return false;
        c = Operations(("Blocked Work", null));
        return true;
    }

    private static bool MatchControlAudit(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "control", "audit"))
            return false;
        c = Operations(("Audit", null));
        return true;
    }

    private static bool MatchExecute(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "execute"))
            return false;
        c = Operations(("Execute", null));
        return true;
    }

    private static bool MatchOperationsStart(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "operations", "start"))
            return false;
        c = Operations(("Start operation", null));
        return true;
    }

    private static bool MatchWorkflowsStart(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "workflows", "start"))
            return false;
        c = Automation(("Start workflow", null));
        return true;
    }

    // --- Attention ---

    private static bool MatchAttentionContactGroupDetail(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "attention", "contact-groups", "{id}"))
            return false;
        c = Operations(
            ("Operational teams", "/attention/operational-teams"),
            ("Team", null));
        return true;
    }

    private static bool MatchAttentionOperationalTeamDetail(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "attention", "operational-teams", "{id}"))
            return false;
        c = Operations(
            ("Operational teams", "/attention/operational-teams"),
            ("Team", null));
        return true;
    }

    private static bool MatchAttentionContactGroups(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "attention", "contact-groups"))
            return false;
        c = Operations(("Operational teams", null));
        return true;
    }

    private static bool MatchAttentionOperationalTeams(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "attention", "operational-teams"))
            return false;
        c = Operations(("Operational teams", null));
        return true;
    }

    private static bool MatchAttentionRoutingDefaults(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "attention", "routing-defaults"))
            return false;
        c = Operations(("Routing defaults", null));
        return true;
    }

    // --- Connections ---

    private static bool MatchConnectionsNew(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "connections", "new"))
            return false;
        c = Connections(("New", null));
        return true;
    }

    private static bool MatchConnectionsDetail(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "connections", "{id}"))
            return false;
        c = Connections(("Connection", null));
        return true;
    }

    private static bool MatchConnectionsList(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "connections"))
            return false;
        c = Trail(("Connections", null));
        return true;
    }

    // --- Dashboard ---

    private static bool MatchDashboardLayout(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "dashboard", "layout"))
            return false;
        c = Trail(
            ("Dashboard", ShellSectionRoutes.DashboardHome),
            ("Edit layout", null));
        return true;
    }

    private static bool MatchDashboardAdd(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "dashboard", "add"))
            return false;
        c = Trail(
            ("Dashboard", ShellSectionRoutes.DashboardHome),
            ("Add page", null));
        return true;
    }

    private static bool MatchDashboardManage(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "dashboard", "manage"))
            return false;
        c = Trail(
            ("Dashboard", ShellSectionRoutes.DashboardHome),
            ("Manage", null));
        return true;
    }

    private static bool MatchDashboardDelete(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "dashboard", "delete"))
            return false;
        c = Trail(
            ("Dashboard", ShellSectionRoutes.DashboardHome),
            ("Delete page", null));
        return true;
    }

    private static bool MatchDashboardHome(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "dashboard"))
            return false;
        c = Trail(("Dashboard", null));
        return true;
    }

    // --- Config landing ---

    private static bool MatchConfigLanding(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "config"))
            return false;
        c = Config(("Overview", null));
        return true;
    }

    // --- Runtime (deep-link) ---

    private static bool MatchRuntimeShutdownRequestDetail(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "runtimes", "{id}", "controls", "shutdown", "requests", "{req}"))
            return false;
        c = Operations(
            ("Runtime", $"/runtimes/{s[1]}/controls/shutdown/requests"),
            ("Request", null));
        return true;
    }

    private static bool MatchRuntimeShutdownRequestsList(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "runtimes", "{id}", "controls", "shutdown", "requests"))
            return false;
        c = Operations(
            ("Runtime", null));
        return true;
    }

    // --- Account / preferences ---

    private static bool MatchAccount(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "account"))
            return false;
        c = Trail(("Account", null));
        return true;
    }

    private static bool MatchPreferences(string[] s, out IReadOnlyList<BreadcrumbItem>? c)
    {
        c = null;
        if (!Matches(s, "preferences"))
            return false;
        c = Trail(("Preferences", null));
        return true;
    }
}
