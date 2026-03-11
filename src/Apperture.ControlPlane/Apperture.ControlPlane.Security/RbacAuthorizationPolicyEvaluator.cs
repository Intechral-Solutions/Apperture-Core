using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Implements IAuthorizationPolicyEvaluator using RBAC. Per ADR-0012: break-glass and relationship slots reserved.
/// Human actors with resolved UserId get role-based evaluation; Machine/System/RemoteInstance deny by default.
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

        // Break-glass slot: reserved, no-op
        // Relationship slot: reserved, no-op

        // RBAC path for Human actors
        if (actor.Kind == ActorKind.Human)
        {
            var userId = await _principalToUserResolver.ResolveUserIdAsync(actor, cancellationToken);
            if (userId is null)
                return false;

            var roles = await _roleProvider.GetRolesForUserAsync(userId!, cancellationToken);
            var effectivePermissions = roles
                .SelectMany(r => r.Permissions)
                .Distinct()
                .ToList();

            var targetScopeKey = resourceIdentity?.ScopeKey ?? resource?.ScopeKey ?? ScopeKeyConstants.Instance;
            var targetResourceType = resourceIdentity?.ResourceType ?? resource?.ResourceType;

            foreach (var rolePerm in effectivePermissions)
            {
                if (!string.Equals(rolePerm.Action, permission.Action, StringComparison.Ordinal))
                    continue;

                // Resource type match: if permission has Resource, role permission must grant it (null = broad grant)
                if (permission.Resource is not null && rolePerm.Resource is not null)
                {
                    if (!string.Equals(rolePerm.Resource.ResourceType, permission.Resource.ResourceType, StringComparison.Ordinal))
                        continue;
                }

                // Scope containment: target scope must be contained in role permission scope
                var grantScopeKey = rolePerm.Resource?.ScopeKey ?? ScopeKeyConstants.Instance;
                if (!_scopeEvaluator.IsContained(targetScopeKey, grantScopeKey))
                    continue;

                return true;
            }
        }

        // Machine, System, RemoteInstance: deny by default
        return false;
    }
}
