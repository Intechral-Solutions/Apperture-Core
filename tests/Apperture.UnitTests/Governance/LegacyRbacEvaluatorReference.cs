using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// A deliberate defect injected into <see cref="LegacyRbacEvaluatorReference"/>. The differential tests must detect every
/// one of these, which is the proof that their generated inputs are sensitive to each element of the evaluator's rule.
/// </summary>
public enum EvaluatorMutation
{
    None,
    DropImplication,
    ImplicationIgnoresGrantType,
    ImplicationIgnoresGrantScope,
    ImplicationAllowsCaseSensitiveAction,
    DropPlatformAdministratorFloor,
    FloorAppliesAtAnyScope,
    FloorMatchesActionIgnoringCase,
    FloorRoleIdCaseSensitive,
    ActionComparedCaseSensitively,
    ResourceTypeIgnored,
    NullResourceGrantMatchesNothing,
    TargetFromPermissionScope,
    ResourcePreferredOverIdentity,
    ContainmentReversed,
    FirstRoleOnly,
    MarkerIgnored,
    MarkerHonouredBeforeResolution,
    NonHumanActorsEvaluated,
}

/// <summary>
/// A frozen copy of <c>RbacAuthorizationPolicyEvaluator</c> as it was at master 1ab3b5f, before the held-set refactor,
/// kept as the oracle of the differential equivalence tests. It is deliberately self-contained: it shares no type with
/// the production implementation except the collaborator interfaces, and it spells out every action, resource type and
/// scope as a literal so that a change to a production constant cannot move the oracle with it.
/// With <see cref="EvaluatorMutation.None"/> (the only value production-facing tests use) its behaviour must never
/// change. It is retargeted, not deleted, when the <c>role.manage</c> implication is removed.
/// </summary>
internal sealed class LegacyRbacEvaluatorReference : IAuthorizationPolicyEvaluator
{
    private const string PlatformAdminRoleId = "role-platform-administrator";
    private const string Instance = "instance";

    private readonly IPrincipalToUserResolver _principalToUserResolver;
    private readonly IRoleProvider _roleProvider;
    private readonly IScopeContainmentEvaluator _scopeEvaluator;
    private readonly EvaluatorMutation _mutation;

    public LegacyRbacEvaluatorReference(
        IPrincipalToUserResolver principalToUserResolver,
        IRoleProvider roleProvider,
        IScopeContainmentEvaluator scopeEvaluator,
        EvaluatorMutation mutation = EvaluatorMutation.None)
    {
        _principalToUserResolver = principalToUserResolver;
        _roleProvider = roleProvider;
        _scopeEvaluator = scopeEvaluator;
        _mutation = mutation;
    }

    private bool Mutated(EvaluatorMutation mutation) => _mutation == mutation;

    public async ValueTask<bool> EvaluateAsync(
        IActor actor,
        PermissionDescriptor permission,
        ResourceDescriptor? resource,
        IGovernedResourceIdentity? resourceIdentity,
        CancellationToken cancellationToken = default)
    {
        if (actor is null)
            return false;

        if (actor.Kind == ActorKind.Human || Mutated(EvaluatorMutation.NonHumanActorsEvaluated))
        {
            if (Mutated(EvaluatorMutation.MarkerHonouredBeforeResolution) && actor is HumanActor { IsBreakGlassSession: true })
                return true;

            var userId = await _principalToUserResolver.ResolveUserIdAsync(actor, cancellationToken);
            if (userId is null)
                return false;

            if (!Mutated(EvaluatorMutation.MarkerIgnored) && actor is HumanActor { IsBreakGlassSession: true })
                return true;

            var roles = await _roleProvider.GetRolesForUserAsync(userId!, cancellationToken);
            if (Mutated(EvaluatorMutation.FirstRoleOnly))
                roles = roles.Take(1).ToList();

            var targetScopeKey = Mutated(EvaluatorMutation.TargetFromPermissionScope)
                ? permission.Resource?.ScopeKey ?? Instance
                : Mutated(EvaluatorMutation.ResourcePreferredOverIdentity)
                    ? resource?.ScopeKey ?? resourceIdentity?.ScopeKey ?? Instance
                    : resourceIdentity?.ScopeKey ?? resource?.ScopeKey ?? Instance;

            if (!Mutated(EvaluatorMutation.DropPlatformAdministratorFloor) &&
                UserHasPlatformAdministratorRole(roles) &&
                IsGovernanceOrAuthConfigurationSurface(permission, targetScopeKey))
                return true;

            var effectivePermissions = roles
                .SelectMany(r => r.Permissions)
                .Distinct()
                .ToList();

            foreach (var rolePerm in effectivePermissions)
            {
                var grantScopeKey = rolePerm.Resource?.ScopeKey ?? Instance;
                var contained = Mutated(EvaluatorMutation.ContainmentReversed)
                    ? _scopeEvaluator.IsContained(grantScopeKey, targetScopeKey)
                    : _scopeEvaluator.IsContained(targetScopeKey, grantScopeKey);
                if (!contained && !Mutated(EvaluatorMutation.ImplicationIgnoresGrantScope))
                    continue;

                if (!Mutated(EvaluatorMutation.DropImplication) && ImpliedByInstanceRoleManage(permission, rolePerm, targetScopeKey))
                    return true;

                if (!contained)
                    continue;

                var sameAction = Mutated(EvaluatorMutation.ActionComparedCaseSensitively)
                    ? string.Equals(rolePerm.Action, permission.Action, StringComparison.Ordinal)
                    : string.Equals(rolePerm.Action, permission.Action, StringComparison.OrdinalIgnoreCase);
                if (!sameAction)
                    continue;

                if (Mutated(EvaluatorMutation.NullResourceGrantMatchesNothing) && rolePerm.Resource is null)
                    continue;

                if (!Mutated(EvaluatorMutation.ResourceTypeIgnored) && permission.Resource is not null && rolePerm.Resource is not null)
                {
                    if (!string.Equals(rolePerm.Resource.ResourceType, permission.Resource.ResourceType, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                return true;
            }
        }

        return false;
    }

    private bool ImpliedByInstanceRoleManage(
        PermissionDescriptor required,
        PermissionDescriptor grant,
        string targetScopeKey)
    {
        var comparison = Mutated(EvaluatorMutation.ImplicationAllowsCaseSensitiveAction)
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        if (!string.Equals(grant.Action, "role.manage", comparison))
            return false;

        if (grant.Resource is null)
            return false;

        if (!Mutated(EvaluatorMutation.ImplicationIgnoresGrantType) &&
            !string.Equals(grant.Resource.ResourceType, "Role", StringComparison.OrdinalIgnoreCase))
            return false;

        return IsGovernanceOrAuthConfigurationSurface(required, targetScopeKey);
    }

    private bool UserHasPlatformAdministratorRole(IReadOnlyList<Role> roles) =>
        roles.Any(r => string.Equals(
            r.Id.Value,
            PlatformAdminRoleId,
            Mutated(EvaluatorMutation.FloorRoleIdCaseSensitive) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));

    private bool IsGovernanceOrAuthConfigurationSurface(PermissionDescriptor required, string targetScopeKey)
    {
        if (!Mutated(EvaluatorMutation.FloorAppliesAtAnyScope) &&
            !string.Equals(targetScopeKey, Instance, StringComparison.Ordinal))
            return false;

        if (required.Resource is null)
            return false;

        var action = Mutated(EvaluatorMutation.FloorMatchesActionIgnoringCase) ? required.Action?.ToLowerInvariant() : required.Action;

        if (!string.Equals(required.Resource.ResourceType, "User", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(required.Resource.ResourceType, "Role", StringComparison.OrdinalIgnoreCase))
            {
                return action is "user.read"
                    or "permission.assign"
                    or "role.assign";
            }

            if (string.Equals(required.Resource.ResourceType, "InstanceConfiguration", StringComparison.OrdinalIgnoreCase))
            {
                return action is "auth.configuration.read"
                    or "auth.ui_policy.manage"
                    or "auth.entra.manage";
            }

            return false;
        }

        return action is "user.read"
            or "user.manage"
            or "role.assign"
            or "credential.manage";
    }
}
