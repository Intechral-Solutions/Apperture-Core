using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// A frozen copy of the evaluator as it was before the <c>role.manage</c> implication was removed (master 1ab3b5f, which the
/// held-set refactor reproduced exactly): the Platform Administrator floor of ten combinations and the instance-level
/// <c>role.manage</c> on Role implication. It is the "old evaluator" of the upgrade window (P-39, A10.2.5): new code running over
/// data the preserving step has not yet migrated may deny what this evaluator allowed, and must never allow what it denied.
/// </summary>
/// <remarks>
/// Self-contained on purpose: it shares no type with the production implementation except the collaborator interfaces, and it
/// spells out every action, resource type and scope as a literal, so a change to a production constant or rule cannot move it.
/// It has no injectable defects and its behaviour must never change.
/// </remarks>
internal sealed class PreRemovalRbacEvaluatorReference : IAuthorizationPolicyEvaluator
{
    private const string PlatformAdminRoleId = "role-platform-administrator";
    private const string Instance = "instance";

    private readonly IPrincipalToUserResolver _principalToUserResolver;
    private readonly IRoleProvider _roleProvider;
    private readonly IScopeContainmentEvaluator _scopeEvaluator;

    public PreRemovalRbacEvaluatorReference(
        IPrincipalToUserResolver principalToUserResolver,
        IRoleProvider roleProvider,
        IScopeContainmentEvaluator scopeEvaluator)
    {
        _principalToUserResolver = principalToUserResolver;
        _roleProvider = roleProvider;
        _scopeEvaluator = scopeEvaluator;
    }

    public async ValueTask<bool> EvaluateAsync(
        IActor actor,
        PermissionDescriptor permission,
        ResourceDescriptor? resource,
        IGovernedResourceIdentity? resourceIdentity,
        CancellationToken cancellationToken = default)
    {
        if (actor is null)
            return false;

        if (actor.Kind == ActorKind.Human)
        {
            var userId = await _principalToUserResolver.ResolveUserIdAsync(actor, cancellationToken);
            if (userId is null)
                return false;

            if (actor is HumanActor { IsBreakGlassSession: true })
                return true;

            var roles = await _roleProvider.GetRolesForUserAsync(userId!, cancellationToken);
            var targetScopeKey = resourceIdentity?.ScopeKey ?? resource?.ScopeKey ?? Instance;

            if (UserHasPlatformAdministratorRole(roles) && IsGovernanceOrAuthConfigurationSurface(permission, targetScopeKey))
                return true;

            var effectivePermissions = roles
                .SelectMany(r => r.Permissions)
                .Distinct()
                .ToList();

            foreach (var rolePerm in effectivePermissions)
            {
                var grantScopeKey = rolePerm.Resource?.ScopeKey ?? Instance;
                if (!_scopeEvaluator.IsContained(targetScopeKey, grantScopeKey))
                    continue;

                if (ImpliedByInstanceRoleManage(permission, rolePerm, targetScopeKey))
                    return true;

                if (!string.Equals(rolePerm.Action, permission.Action, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (permission.Resource is not null && rolePerm.Resource is not null)
                {
                    if (!string.Equals(rolePerm.Resource.ResourceType, permission.Resource.ResourceType, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                return true;
            }
        }

        return false;
    }

    private static bool ImpliedByInstanceRoleManage(PermissionDescriptor required, PermissionDescriptor grant, string targetScopeKey)
    {
        if (!string.Equals(grant.Action, "role.manage", StringComparison.OrdinalIgnoreCase))
            return false;

        if (grant.Resource is null)
            return false;

        if (!string.Equals(grant.Resource.ResourceType, "Role", StringComparison.OrdinalIgnoreCase))
            return false;

        return IsGovernanceOrAuthConfigurationSurface(required, targetScopeKey);
    }

    private static bool UserHasPlatformAdministratorRole(IReadOnlyList<Role> roles) =>
        roles.Any(r => string.Equals(r.Id.Value, PlatformAdminRoleId, StringComparison.OrdinalIgnoreCase));

    private static bool IsGovernanceOrAuthConfigurationSurface(PermissionDescriptor required, string targetScopeKey)
    {
        if (!string.Equals(targetScopeKey, Instance, StringComparison.Ordinal))
            return false;

        if (required.Resource is null)
            return false;

        if (!string.Equals(required.Resource.ResourceType, "User", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(required.Resource.ResourceType, "Role", StringComparison.OrdinalIgnoreCase))
            {
                return required.Action is "user.read"
                    or "permission.assign"
                    or "role.assign";
            }

            if (string.Equals(required.Resource.ResourceType, "InstanceConfiguration", StringComparison.OrdinalIgnoreCase))
            {
                return required.Action is "auth.configuration.read"
                    or "auth.ui_policy.manage"
                    or "auth.entra.manage";
            }

            return false;
        }

        return required.Action is "user.read"
            or "user.manage"
            or "role.assign"
            or "credential.manage";
    }
}
