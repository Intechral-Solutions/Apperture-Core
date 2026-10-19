using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// The anti-escalation rules for changing roles, as decisions over an actor's <see cref="ActorAuthority"/> and the rows an
/// <see cref="AuthoritySnapshot"/> locked. Nothing here reads the database or the request: the same inputs always give the same
/// answer, and "holds" is only ever <see cref="ActorAuthority.Holds"/>.
/// </summary>
internal static class RoleAuthorityRules
{
    public static readonly PermissionDescriptor RoleManage =
        new(ActionConstants.RoleManage, new ResourceDescriptor(ResourceTypeConstants.Role, ScopeKeyConstants.Instance));

    public static readonly PermissionDescriptor PermissionAssign =
        new(ActionConstants.PermissionAssign, new ResourceDescriptor(ResourceTypeConstants.Role, ScopeKeyConstants.Instance));

    public static readonly PermissionDescriptor RoleAssign =
        new(ActionConstants.RoleAssign, new ResourceDescriptor(ResourceTypeConstants.Role, ScopeKeyConstants.Instance));

    /// <summary>
    /// The authority of the acting principal, from the roles a snapshot locked. A principal that is not a human who resolved
    /// to a user has none; a resolved legacy break-glass session has all.
    /// </summary>
    public static ActorAuthority AuthorityOf(
        IActor? actor, UserId? resolvedUserId, AuthoritySnapshot snapshot, IScopeContainmentEvaluator scope)
    {
        if (actor is not { Kind: ActorKind.Human } || resolvedUserId is null)
            return ActorAuthority.None;
        if (actor is HumanActor { IsBreakGlassSession: true })
            return ActorAuthority.Unrestricted;
        return ActorAuthority.FromSet(EffectivePermissionSet.FromRoles(snapshot.ActorRoles, scope));
    }

    /// <summary>Rule 1: the permissions in <paramref name="wanted"/> that the actor does not hold, distinct, in request order.</summary>
    public static IReadOnlyList<PermissionDescriptor> NotHeld(ActorAuthority authority, IEnumerable<PermissionDescriptor> wanted) =>
        wanted.Distinct().Where(p => !authority.Holds(p)).ToList();

    /// <summary>
    /// What replacing a role's grants adds, from the locked row: the permissions of <paramref name="requested"/> the role does
    /// not already hold. Against an unreadable stored list (no grants) every requested permission is an addition.
    /// </summary>
    public static IReadOnlyList<PermissionDescriptor> Added(Role lockedRole, IReadOnlyList<PermissionDescriptor> requested) =>
        PermissionSetChange.Between(lockedRole.Permissions, requested).Added;

    public static bool IsPlatformAdministratorRole(RoleId roleId) =>
        string.Equals(roleId.Value, BootstrapConstants.PlatformAdminRoleId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Rule 2 for assigning or removing <paramref name="role"/>: the Platform Administrator role only by a Platform
    /// Administrator, then every permission of the role held by the actor. Null when the actor is entitled to manage the role.
    /// The same test applies whether or not the request would change anything.
    /// </summary>
    public static RoleAuthorityRefusal? CheckEntitlement(ActorAuthority authority, Role role, bool removal)
    {
        if (IsPlatformAdministratorRole(role.Id) && !authority.HoldsPlatformAdministratorRole)
        {
            return new RoleAuthorityRefusal(
                Reason: "platform_administrator_restricted",
                Code: AuthorizationRefusalCodes.PlatformAdministratorRestricted,
                Message: "Only a Platform Administrator can assign or remove the Platform Administrator role.",
                Missing: []);
        }

        var missing = NotHeld(authority, role.Permissions);
        if (missing.Count == 0)
            return null;

        return removal
            ? new RoleAuthorityRefusal(
                "removal_exceeds_authority",
                AuthorizationRefusalCodes.RemovalExceedsAuthority,
                "You cannot remove a role that grants permissions you do not hold.",
                missing)
            : new RoleAuthorityRefusal(
                "exceeds_authority",
                AuthorizationRefusalCodes.AssignmentExceedsAuthority,
                "You cannot assign a role that grants permissions you do not hold.",
                missing);
    }
}

/// <summary>An escalation refusal: the reason recorded in the audit row, the client-facing code and message, and what is missing.</summary>
internal sealed record RoleAuthorityRefusal(string Reason, string Code, string Message, IReadOnlyList<PermissionDescriptor> Missing)
{
    public AuthorizationRefusalException ToException() => new(Code, Message, Missing);
}
