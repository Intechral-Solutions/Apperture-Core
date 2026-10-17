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
    /// <summary>The removed <c>role.manage</c> implication is back, with its original shape.</summary>
    ReintroduceImplication,
    /// <summary>The removed implication is back and no longer looks at the type of the <c>role.manage</c> grant.</summary>
    ReintroduceImplicationIgnoringGrantType,
    /// <summary>The floor still contains <c>user.read</c> on Role, which only the removed stopgap consumers asked for.</summary>
    FloorKeepsGovernanceReadOnRole,
    /// <summary>The floor still contains <c>role.assign</c> on User, which nothing asks for.</summary>
    FloorKeepsRoleAssignOnUser,
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
/// The oracle of the differential equivalence tests: the authorization rule as it is after the <c>role.manage</c> implication
/// was removed (IAM-10.2a slice 3). It started as a frozen copy of the evaluator at master 1ab3b5f and was retargeted, not
/// replaced, by exactly the approved change: the implication is gone, and the Platform Administrator floor is the eight
/// combinations that remain after <c>user.read</c> on Role (its consumers moved to <c>user.read</c> on User) and
/// <c>role.assign</c> on User (nothing asks for it) left it. <see cref="PreRemovalRbacEvaluatorReference"/> keeps the rule as it
/// was, for the upgrade-window proof.
/// </summary>
/// <remarks>
/// Deliberately self-contained: it shares no type with the production implementation except the collaborator interfaces, and it
/// spells out every action, resource type and scope as a literal so that a change to a production constant cannot move the
/// oracle with it. With <see cref="EvaluatorMutation.None"/> (the only value production-facing tests use) its behaviour must
/// never change.
/// </remarks>
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
                if (!contained)
                    continue;

                if (ImpliedByRemovedRoleManageRule(permission, rolePerm, targetScopeKey))
                    return true;

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

    /// <summary>
    /// The rule slice 3 removed. It exists here only so a mutation can put it back; with every other mutation, and with none, a
    /// <c>role.manage</c> grant is an ordinary grant that allows <c>role.manage</c> and nothing else.
    /// </summary>
    private bool ImpliedByRemovedRoleManageRule(PermissionDescriptor required, PermissionDescriptor grant, string targetScopeKey)
    {
        var reintroduced = Mutated(EvaluatorMutation.ReintroduceImplication) || Mutated(EvaluatorMutation.ReintroduceImplicationIgnoringGrantType);
        if (!reintroduced)
            return false;

        if (!string.Equals(grant.Action, "role.manage", StringComparison.OrdinalIgnoreCase))
            return false;

        if (grant.Resource is null)
            return false;

        if (!Mutated(EvaluatorMutation.ReintroduceImplicationIgnoringGrantType) &&
            !string.Equals(grant.Resource.ResourceType, "Role", StringComparison.OrdinalIgnoreCase))
            return false;

        return RemovedFloorWithTenCombinations(required, targetScopeKey);
    }

    private bool UserHasPlatformAdministratorRole(IReadOnlyList<Role> roles) =>
        roles.Any(r => string.Equals(
            r.Id.Value,
            PlatformAdminRoleId,
            Mutated(EvaluatorMutation.FloorRoleIdCaseSensitive) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));

    /// <summary>The ten combinations the floor and the implication used to share, for the mutations that restore them.</summary>
    private static bool RemovedFloorWithTenCombinations(PermissionDescriptor required, string targetScopeKey) =>
        string.Equals(targetScopeKey, Instance, StringComparison.Ordinal) &&
        required.Resource is not null &&
        (required.Resource.ResourceType.ToUpperInvariant(), required.Action) switch
        {
            ("ROLE", "user.read" or "permission.assign" or "role.assign") => true,
            ("INSTANCECONFIGURATION", "auth.configuration.read" or "auth.ui_policy.manage" or "auth.entra.manage") => true,
            ("USER", "user.read" or "user.manage" or "role.assign" or "credential.manage") => true,
            _ => false,
        };

    /// <summary>The eight combinations of the floor after slice 3.</summary>
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
                return action is "permission.assign"
                    or "role.assign"
                    || (Mutated(EvaluatorMutation.FloorKeepsGovernanceReadOnRole) && action is "user.read");
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
            or "credential.manage"
            || (Mutated(EvaluatorMutation.FloorKeepsRoleAssignOnUser) && action is "role.assign");
    }
}
