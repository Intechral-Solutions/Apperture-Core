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
    /// <summary>BDS-5 — demo customer business-record projection (not final Records nav).</summary>
    public const string DemoCustomers = DemoCustomerUiRoutes.List;
    public const string NotificationDiagnostics = "/config/notifications/diagnostics";
    public const string Preferences = "/preferences";
    /// <summary>SWM-7: worker runtime visibility lives on Control Center (no duplicate Developer surface).</summary>
    public const string WorkerRuntimeControlCenter = "/control/center";

    /// <summary>
    /// SWM-9a: points at the hosting guidance ops doc (markdown is shown via repo path in descriptions;
    /// UI uses Control Center for live status — this constant is for static/route tests).
    /// Prefer Control Center; docs path is relative for developers cloning the repo.
    /// </summary>
    public const string WorkerHostingGuidanceDoc = "/control/center";

    /// <summary>Label used in Developer/Internal for quarantined lifecycle — must not imply start/stop works.</summary>
    public const string WorkerLifecycleQuarantinedLabel = "Worker lifecycle (quarantined)";

    public static IReadOnlyList<string> DeferredDashboardRoutes { get; } =
    [
        ManageDashboards,
        AddDashboardPage,
        DeleteDashboardPage,
        EditDashboardLayout
    ];
}
