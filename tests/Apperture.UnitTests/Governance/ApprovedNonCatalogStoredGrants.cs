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
    /// <c>user.read</c> on Role is a stopgap read check that is pruned when the governance-read consumers switch to
    /// <c>user.read</c> on User. <c>workflow.execute</c> at instance scope lets the administrator satisfy every
    /// module-scoped check by containment, so it stays.
    /// </summary>
    public static readonly IReadOnlySet<PermissionDescriptor> Exceptions = new HashSet<PermissionDescriptor>
    {
        new(ActionConstants.UserRead, new ResourceDescriptor(ResourceTypeConstants.Role, ScopeKeyConstants.Instance)),
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
            .Concat(PlatformAdminRolePermissionRepair.RequiredOperatorUiPermissions);
}
