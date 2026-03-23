using Apperture.Core.Application;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Users assigned the seeded platform administrator role should reach Config → Governance and
/// Config → Authentication even when <see cref="Role.Permissions"/> JSON is missing or failed to deserialize.
/// </summary>
internal static class PlatformAdministratorGovernancePermissions
{
    public static bool UserHasPlatformAdministratorRole(IReadOnlyList<Role> roles) =>
        roles.Any(r => string.Equals(r.Id.Value, BootstrapConstants.PlatformAdminRoleId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Instance-scoped permissions that power operator capabilities and governance/auth config APIs.
    /// </summary>
    public static bool IsGovernanceOrAuthConfigurationSurface(PermissionDescriptor required, string targetScopeKey)
    {
        if (!string.Equals(targetScopeKey, ScopeKeyConstants.Instance, StringComparison.Ordinal))
            return false;

        if (required.Resource is null)
            return false;

        if (!string.Equals(required.Resource.ResourceType, ResourceTypeConstants.User, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(required.Resource.ResourceType, ResourceTypeConstants.Role, StringComparison.OrdinalIgnoreCase))
            {
                return required.Action is ActionConstants.UserRead
                    or ActionConstants.PermissionAssign
                    or ActionConstants.RoleAssign;
            }

            if (string.Equals(required.Resource.ResourceType, ResourceTypeConstants.InstanceConfiguration, StringComparison.OrdinalIgnoreCase))
            {
                return required.Action is ActionConstants.AuthConfigurationRead
                    or ActionConstants.AuthUiPolicyManage
                    or ActionConstants.AuthEntraManage;
            }

            return false;
        }

        return required.Action is ActionConstants.UserRead
            or ActionConstants.UserManage
            or ActionConstants.RoleAssign
            or ActionConstants.CredentialManage;
    }
}
