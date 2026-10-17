using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// The one place that names the stored grants of the Platform Administrator role that are deliberately not catalog
/// permissions. Every other grant the startup repair forces must be a registered catalog permission.
/// </summary>
internal static class ApprovedNonCatalogStoredGrants
{
    /// <summary>
    /// <c>workflow.execute</c> at instance scope lets the administrator satisfy every module-scoped check by containment, so it
    /// stays. The other exception the design named, <c>user.read</c> on Role (a stopgap read check), left the repair lists when
    /// its three consumers moved to <c>user.read</c> on User; the repair now removes it as an obsolete grant.
    /// </summary>
    public static readonly IReadOnlySet<PermissionDescriptor> Exceptions = new HashSet<PermissionDescriptor>
    {
        new("workflow.execute", new ResourceDescriptor(ResourceTypeConstants.WorkflowInstance, ScopeKeyConstants.Instance)),
    };

    /// <summary>Every fixed grant the startup repair adds to the administrator role (module-scoped grants are derived from the catalog).</summary>
    public static IEnumerable<PermissionDescriptor> AllRepairGrants() =>
        PlatformAdminRolePermissionRepair.RequiredAuthAdminPermissions
            .Concat(PlatformAdminRolePermissionRepair.RequiredGovernanceSurfacePermissions)
            .Concat(PlatformAdminRolePermissionRepair.RequiredManagedContactPermissions)
            .Concat(PlatformAdminRolePermissionRepair.RequiredNotificationDefinitionPermissions)
            .Concat(PlatformAdminRolePermissionRepair.RequiredInstanceAuthConfigurationPermissions)
            .Concat(PlatformAdminRolePermissionRepair.RequiredPlatformSettingsPermissions)
            .Concat(PlatformAdminRolePermissionRepair.RequiredResourceContextPermissions)
            .Concat(PlatformAdminRolePermissionRepair.RequiredIntegrationPermissions)
            .Concat(PlatformAdminRolePermissionRepair.RequiredConnectorOperatorPermissions)
            .Concat(PlatformAdminRolePermissionRepair.RequiredConnectionBrowsePermissions)
            .Concat(PlatformAdminRolePermissionRepair.RequiredOperationalTeamPermissions)
            .Concat(PlatformAdminRolePermissionRepair.RequiredDirectorySearchPermissions)
            .Concat(PlatformAdminRolePermissionRepair.RequiredConnectionVerifyPermissions)
            .Concat(PlatformAdminRolePermissionRepair.RequiredOperatorUiPermissions);
}
