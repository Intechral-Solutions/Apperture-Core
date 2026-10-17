using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// The one definition of what a user's role assignments allow. It reproduces the authorization rule of
/// <see cref="RbacAuthorizationPolicyEvaluator"/> exactly, so the evaluator and every caller that needs to know what an
/// actor <em>holds</em> share a single implementation:
/// <list type="bullet">
/// <item>the Platform Administrator floor (<see cref="PlatformAdministratorGovernancePermissions"/>) applies at instance
/// scope whenever any assigned role is the seeded administrator role, whatever its stored permissions say;</item>
/// <item>the grants of all assigned roles are one de-duplicated union, with no deny and no precedence;</item>
/// <item>a grant matches on action (ignoring case), on resource type (only when both sides carry one) and on scope
/// containment; a grant with no resource is a broad instance-scope grant;</item>
/// <item>the target scope is an explicit input and is never read from <see cref="PermissionDescriptor.Resource"/> of the
/// permission being checked.</item>
/// </list>
/// A role whose stored permissions could not be read reaches this type as a role with no permissions, which contributes
/// nothing. The set takes its roles as read-only input: it neither refreshes nor mutates them.
/// </summary>
public sealed class EffectivePermissionSet
{
    private readonly IReadOnlyList<Role> _roles;
    private readonly IScopeContainmentEvaluator _scopeEvaluator;
    private readonly Lazy<bool> _holdsPlatformAdministratorRole;
    private readonly Lazy<IReadOnlyList<PermissionDescriptor>> _grants;

    private EffectivePermissionSet(IReadOnlyList<Role> roles, IScopeContainmentEvaluator scopeEvaluator)
    {
        _roles = roles;
        _scopeEvaluator = scopeEvaluator;

        // Both are evaluated on first use, and in the order the evaluator always used (floor first, grants second), so a
        // role list that cannot be enumerated fails at the same point it always did.
        _holdsPlatformAdministratorRole = new Lazy<bool>(() =>
            PlatformAdministratorGovernancePermissions.UserHasPlatformAdministratorRole(_roles));
        _grants = new Lazy<IReadOnlyList<PermissionDescriptor>>(() =>
            _roles.SelectMany(r => r.Permissions).Distinct().ToList());
    }

    /// <summary>A set built from the roles assigned to one user (their unconstrained assignments).</summary>
    public static EffectivePermissionSet FromRoles(IReadOnlyList<Role> roles, IScopeContainmentEvaluator scopeEvaluator) =>
        new(roles, scopeEvaluator);

    /// <summary>
    /// The scope a check is made against: the governed resource identity's scope, else the resource's scope, else the
    /// instance. The scope of the permission being checked is deliberately not consulted.
    /// </summary>
    public static string ResolveTargetScope(ResourceDescriptor? resource, IGovernedResourceIdentity? resourceIdentity) =>
        resourceIdentity?.ScopeKey ?? resource?.ScopeKey ?? ScopeKeyConstants.Instance;

    /// <summary>True when the roles allow <paramref name="permission"/> against <paramref name="targetScopeKey"/>.</summary>
    public bool Allows(PermissionDescriptor permission, string targetScopeKey)
    {
        // Assignment to the seeded platform administrator role grants config/governance surfaces even when
        // PermissionsJson is empty or failed to deserialize (operators would otherwise be fully locked out).
        if (_holdsPlatformAdministratorRole.Value &&
            PlatformAdministratorGovernancePermissions.IsGovernanceOrAuthConfigurationSurface(permission, targetScopeKey))
            return true;

        foreach (var rolePerm in _grants.Value)
        {
            var grantScopeKey = rolePerm.Resource?.ScopeKey ?? ScopeKeyConstants.Instance;
            if (!_scopeEvaluator.IsContained(targetScopeKey, grantScopeKey))
                continue;

            if (ImpliedByInstanceRoleManage(permission, rolePerm, targetScopeKey))
                return true;

            if (!string.Equals(rolePerm.Action, permission.Action, StringComparison.OrdinalIgnoreCase))
                continue;

            // Resource type match: if permission has Resource, role permission must grant it (null = broad grant)
            if (permission.Resource is not null && rolePerm.Resource is not null)
            {
                if (!string.Equals(rolePerm.Resource.ResourceType, permission.Resource.ResourceType, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>Allows with the target scope taken the way the evaluator takes it from its arguments.</summary>
    public bool Allows(PermissionDescriptor permission, ResourceDescriptor? resource, IGovernedResourceIdentity? resourceIdentity) =>
        Allows(permission, ResolveTargetScope(resource, resourceIdentity));

    /// <summary>
    /// True when the roles hold <paramref name="grant"/>: the check the evaluator makes for the same permission as its
    /// own resource, so that the scope that is checked is the scope of the grant itself.
    /// </summary>
    public bool Holds(PermissionDescriptor grant) =>
        Allows(grant, ResolveTargetScope(grant.Resource, resourceIdentity: null));

    /// <summary>
    /// Transitional: instance-scoped <c>role.manage</c> on <see cref="ResourceTypeConstants.Role"/> implies governance and
    /// configuration-surface permissions only when the evaluated target scope is the instance (not module-scoped checks).
    /// It is kept so that introducing the set changes no result, and is removed together with its data step.
    /// </summary>
    private static bool ImpliedByInstanceRoleManage(
        PermissionDescriptor required,
        PermissionDescriptor grant,
        string targetScopeKey)
    {
        if (!string.Equals(grant.Action, ActionConstants.RoleManage, StringComparison.OrdinalIgnoreCase))
            return false;

        if (grant.Resource is null)
            return false;

        if (!string.Equals(grant.Resource.ResourceType, ResourceTypeConstants.Role, StringComparison.OrdinalIgnoreCase))
            return false;

        return PlatformAdministratorGovernancePermissions.IsGovernanceOrAuthConfigurationSurface(required, targetScopeKey);
    }
}
