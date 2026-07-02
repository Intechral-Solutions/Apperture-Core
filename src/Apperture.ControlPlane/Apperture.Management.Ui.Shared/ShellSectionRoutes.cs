namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Canonical landing routes for custom rail zone owners (logo / gear).
/// </summary>
public static class ShellSectionRoutes
{
    /// <summary>Platform dashboard landing; top logo navigates here.</summary>
    public const string DashboardHome = "/dashboard";

    /// <summary>Configuration section landing; bottom gear navigates here.</summary>
    public const string ConfigHome = "/config";

    /// <summary>Developer and internal stabilization utilities (Config gear zone).</summary>
    public const string DeveloperTasks = "/config/developer";
}
