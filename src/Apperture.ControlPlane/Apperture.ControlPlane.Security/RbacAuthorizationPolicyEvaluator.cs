using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Implements IAuthorizationPolicyEvaluator using RBAC. Per ADR-0012: break-glass and relationship slots reserved.
/// Human actors with resolved UserId get role-based evaluation; Machine/System/RemoteInstance deny by default.
/// A break-glass session bypasses role evaluation only after it has resolved to its user.
/// </summary>
public sealed class RbacAuthorizationPolicyEvaluator : IAuthorizationPolicyEvaluator
{
    private readonly IPrincipalToUserResolver _principalToUserResolver;
    private readonly IRoleProvider _roleProvider;
    private readonly IScopeContainmentEvaluator _scopeEvaluator;

    public RbacAuthorizationPolicyEvaluator(
        IPrincipalToUserResolver principalToUserResolver,
        IRoleProvider roleProvider,
        IScopeContainmentEvaluator scopeEvaluator)
    {
        _principalToUserResolver = principalToUserResolver;
        _roleProvider = roleProvider;
        _scopeEvaluator = scopeEvaluator;
    }

    /// <inheritdoc />
    public async ValueTask<bool> EvaluateAsync(
        IActor actor,
        PermissionDescriptor permission,
        ResourceDescriptor? resource,
        IGovernedResourceIdentity? resourceIdentity,
        CancellationToken cancellationToken = default)
    {
        if (actor is null)
            return false;

        // Relationship slot: reserved, no-op

        // RBAC path for Human actors
        if (actor.Kind == ActorKind.Human)
        {
            // Every human session, break-glass included, must first resolve to its user through the identity link
            // it was issued through. A session whose link is gone authorizes nothing.
            var userId = await _principalToUserResolver.ResolveUserIdAsync(actor, cancellationToken);
            if (userId is null)
                return false;

            // Break-glass: a resolved session established via break-glass login grants all permission checks
            if (actor is HumanActor { IsBreakGlassSession: true })
                return true;

            var roles = await _roleProvider.GetRolesForUserAsync(userId!, cancellationToken);
            var targetScopeKey = resourceIdentity?.ScopeKey ?? resource?.ScopeKey ?? ScopeKeyConstants.Instance;

            // Assignment to the seeded platform administrator role grants config/governance surfaces even when
            // PermissionsJson is empty or failed to deserialize (operators would otherwise be fully locked out).
            if (PlatformAdministratorGovernancePermissions.UserHasPlatformAdministratorRole(roles) &&
                PlatformAdministratorGovernancePermissions.IsGovernanceOrAuthConfigurationSurface(permission, targetScopeKey))
                return true;

            var effectivePermissions = roles
                .SelectMany(r => r.Permissions)
                .Distinct()
                .ToList();

            foreach (var rolePerm in effectivePermissions)
            {
                var grantScopeKey = rolePerm.Resource?.ScopeKey ?? ScopeKeyConstants.Instance;
                if (!_scopeEvaluator.IsContained(targetScopeKey, grantScopeKey))
                    continue;

                // Platform "role.manage" at instance scope (seeded platform administrator): implies governance,
                // auth administration, and instance auth configuration checks at instance scope so operators are
                // not blocked when explicit user.read / auth.* grants are missing from persisted JSON.
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
        }

        // Machine, System, RemoteInstance: deny by default
        return false;
    }

    /// <summary>
    /// Instance-scoped <c>role.manage</c> on <see cref="ResourceTypeConstants.Role"/> implies governance and
    /// configuration-surface permissions only when the evaluated target scope is the instance (not module-scoped checks).
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
