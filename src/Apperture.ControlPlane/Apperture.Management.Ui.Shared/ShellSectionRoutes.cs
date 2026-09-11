namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Canonical landing routes for custom rail zone owners (logo / gear).
/// </summary>
public static class ShellSectionRoutes
{
    /// <summary>Operator home; top logo navigates here.</summary>
    public const string ControlCenterHome = "/control/center";

    /// <summary>Compatibility route; redirects to <see cref="ControlCenterHome"/>.</summary>
    public const string DashboardHome = "/dashboard";

    /// <summary>Configuration section landing; bottom gear navigates here.</summary>
    public const string ConfigHome = "/config";

    /// <summary>Developer and internal utilities (Config → System).</summary>
    public const string DeveloperTasks = "/config/developer";

    /// <summary>Config inventory explorer.</summary>
    public const string Inventory = "/config/inventory";

    /// <summary>Business Data collections landing.</summary>
    public const string BusinessDataHome = "/business-data";
}
