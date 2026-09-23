namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Capability tokens attached to menu items. Values match operator capability flags.
/// Missing tokens mean the item stays visible (no invented authorization taxonomy).
/// </summary>
public static class DomainMenuCapabilityTokens
{
    public const string ViewAudit = "ViewAudit";
    public const string ViewIntegrations = "ViewIntegrations";
    public const string ViewConnections = "ViewConnections";
    public const string ViewGovernance = "ViewGovernance";
    public const string ViewManagedContacts = "ViewManagedContacts";
    public const string ViewNotificationDefinitions = "ViewNotificationDefinitions";
    public const string ViewPlatformSettings = "ViewPlatformSettings";
    public const string ManageModules = "ManageModules";

    /// <summary>
    /// Derived, not a permission: true when the operator can read either Component family
    /// (connector definitions or notification definitions). Gates the Catalog leaf only.
    /// </summary>
    public const string ViewComponents = "ViewComponents";

    public const string ViewAuthConfiguration = "ViewAuthConfiguration";
    public const string ManageSchedules = "ManageSchedules";
}
