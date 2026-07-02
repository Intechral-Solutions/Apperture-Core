namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Routes for developer, internal, and deferred stabilization utilities.
/// Kept out of primary operator navigation; surfaced via <see cref="ShellSectionRoutes.DeveloperTasks"/>.
/// </summary>
public static class DeveloperTaskRoutes
{
    public const string ManageDashboards = "/dashboard/manage";
    public const string AddDashboardPage = "/dashboard/add";
    public const string DeleteDashboardPage = "/dashboard/delete";
    public const string EditDashboardLayout = "/dashboard/layout";
    public const string NestedNavDemo = "/demo/nested-nav";
    public const string NotificationDiagnostics = "/config/notifications/diagnostics";
    public const string Preferences = "/preferences";

    public static IReadOnlyList<string> DeferredDashboardRoutes { get; } =
    [
        ManageDashboards,
        AddDashboardPage,
        DeleteDashboardPage,
        EditDashboardLayout
    ];
}
