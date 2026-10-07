using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Audit;

// AUD-1.2: the typed producers of the role, permission, assignment, user, identity-link and credential actions. Each takes
// exactly what its action may record and nothing else: no password, hash, token, search text or free text.
public static partial class AuditEvents
{
    /// <summary>A role was created, with the permissions it was created with.</summary>
    public static AuditEvent RoleCreated(RoleId role, string name, IReadOnlyList<Apperture.Shared.Kernel.PermissionDescriptor> granted) =>
        Event(
            AuditActionNames.RoleCreated,
            Target(AuditTargetTypes.Role, role.Value),
            AuditOutcomeCodes.Succeeded,
            ("roleId", role.Value),
            ("name", name),
            ("permissionCount", granted.Count),
            ("permissionsAdded", granted.Count == 0 ? null : Identities(granted.OrderBy(PermissionSetChange.Identity, StringComparer.Ordinal))));

    /// <summary>A role's name or description changed.</summary>
    public static AuditEvent RoleMetadataUpdated(RoleId role, string previousName, string newName, bool descriptionChanged) =>
        Event(
            AuditActionNames.RoleMetadataUpdated,
            Target(AuditTargetTypes.Role, role.Value),
            AuditOutcomeCodes.Succeeded,
            ("roleId", role.Value),
            ("previousName", previousName),
            ("newName", newName),
            ("descriptionChanged", descriptionChanged));

    /// <summary>A role was deleted. The name is a snapshot: the role no longer exists.</summary>
    public static AuditEvent RoleDeleted(RoleId role, string name) =>
        Event(
            AuditActionNames.RoleDeleted,
            Target(AuditTargetTypes.Role, role.Value),
            AuditOutcomeCodes.Succeeded,
            ("roleId", role.Value),
            ("name", name));

    /// <summary>
    /// A role's permission set was replaced. The additions and removals are the ones that committed (D9); recording them is
    /// attribution, not a statement that the change was appropriate.
    /// </summary>
    public static AuditEvent PermissionAssignmentUpdated(RoleId role, PermissionSetChange change, int permissionCount) =>
        Event(
            AuditActionNames.PermissionAssignmentUpdated,
            Target(AuditTargetTypes.Role, role.Value),
            AuditOutcomeCodes.Succeeded,
            ("roleId", role.Value),
            ("permissionCount", permissionCount),
            ("permissionsAdded", change.Added.Count == 0 ? null : Identities(change.Added)),
            ("permissionsRemoved", change.Removed.Count == 0 ? null : Identities(change.Removed)));

    /// <summary>A change to a role was refused. <paramref name="reason"/> is <c>system_role</c> or <c>assignments_remain</c>.</summary>
    public static AuditEvent RoleMutationRejected(RoleId role, string reason, int? assignmentCount = null) =>
        Event(
            AuditActionNames.RoleMutationRejected,
            Target(AuditTargetTypes.Role, role.Value),
            AuditOutcomeCodes.Rejected,
            ("roleId", role.Value),
            ("reason", reason),
            ("assignmentCount", assignmentCount));

    /// <summary>
    /// A local user was created. <paramref name="localSignInReady"/> is true when a password was set at creation, so the
    /// user can sign in at once (otherwise a setup invitation completes it). The password itself is never recorded.
    /// </summary>
    public static AuditEvent UserCreated(UserId user, string username, string displayName, bool localSignInReady, bool setupInvitationRequested) =>
        Event(
            AuditActionNames.UserCreated,
            Target(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Succeeded,
            ("userId", user.Value),
            ("username", username),
            ("displayName", displayName),
            ("localSignInReady", localSignInReady),
            ("setupInvitationRequested", setupInvitationRequested));

    /// <summary>A role was assigned to a user.</summary>
    public static AuditEvent RoleAssigned(UserId user, RoleId role) =>
        Event(
            AuditActionNames.RoleAssigned,
            Target(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Succeeded,
            ("userId", user.Value),
            ("roleId", role.Value));

    /// <summary>A role was removed from a user.</summary>
    public static AuditEvent RoleRemoved(UserId user, RoleId role) =>
        Event(
            AuditActionNames.RoleRemoved,
            Target(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Succeeded,
            ("userId", user.Value),
            ("roleId", role.Value));

    /// <summary>A Microsoft identity was associated with a user by an administrator and is pending proof.</summary>
    public static AuditEvent EntraLinkPending(UserId user, string linkId, string tenantId, string objectId) =>
        Event(
            AuditActionNames.EntraLinkPending,
            Target(AuditTargetTypes.IdentityLink, linkId),
            AuditOutcomeCodes.Succeeded,
            ("userId", user.Value),
            ("linkId", linkId),
            ("tenantId", tenantId),
            ("objectId", objectId));

    /// <summary>An identity link other than a pending Microsoft association was added to a user.</summary>
    public static AuditEvent IdentityLinkAdded(UserId user, string linkId, IdentitySource source, string externalId) =>
        Event(
            AuditActionNames.IdentityLinkAdded,
            Target(AuditTargetTypes.IdentityLink, linkId),
            AuditOutcomeCodes.Succeeded,
            ("userId", user.Value),
            ("linkId", linkId),
            ("source", SourceName(source)),
            ("username", source == IdentitySource.Local ? externalId : null),
            ("objectId", source == IdentitySource.Local ? null : externalId));

    /// <summary>An identity link was removed from a user. The link no longer exists; its id and key are the snapshot.</summary>
    public static AuditEvent IdentityLinkRemoved(UserId user, string linkId, IdentitySource source, string externalId, string? tenantId) =>
        Event(
            AuditActionNames.IdentityLinkRemoved,
            Target(AuditTargetTypes.IdentityLink, linkId),
            AuditOutcomeCodes.Succeeded,
            ("userId", user.Value),
            ("linkId", linkId),
            ("source", SourceName(source)),
            ("username", source == IdentitySource.Local ? externalId : null),
            ("objectId", source == IdentitySource.Local ? null : externalId),
            ("tenantId", tenantId));

    /// <summary>A user's local password was set. The password, its hash and its salt are never recorded.</summary>
    public static AuditEvent PasswordSet(UserId user) =>
        Event(
            AuditActionNames.PasswordSet,
            Target(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Succeeded,
            ("userId", user.Value));

    /// <summary>A user's local credential was disabled.</summary>
    public static AuditEvent CredentialDisabled(UserId user) =>
        Event(
            AuditActionNames.CredentialDisabled,
            Target(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Succeeded,
            ("userId", user.Value));

    /// <summary>A user's local credential was re-enabled after recent strong authentication.</summary>
    public static AuditEvent LocalCredentialReenabled(UserId user) =>
        Event(
            AuditActionNames.LocalCredentialReenabled,
            Target(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Succeeded,
            ("userId", user.Value));

    /// <summary>Removing a user's last sign-in method was refused for the last platform administrator.</summary>
    public static AuditEvent BlockedLastAdminLinkRemoved(UserId user, string linkId, IdentitySource source) =>
        Event(
            AuditActionNames.BlockedLastAdminLinkRemoved,
            Target(AuditTargetTypes.IdentityLink, linkId),
            AuditOutcomeCodes.Rejected,
            ("userId", user.Value),
            ("linkId", linkId),
            ("source", SourceName(source)));

    /// <summary>Removing the last platform administrator role assignment was refused.</summary>
    public static AuditEvent BlockedLastPlatformAdminRemoval(UserId user, RoleId role) =>
        Event(
            AuditActionNames.BlockedLastPlatformAdminRemoval,
            Target(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Rejected,
            ("userId", user.Value),
            ("roleId", role.Value));

    /// <summary>Disabling the only local credential of the last platform administrator was refused.</summary>
    public static AuditEvent BlockedLastAdminCredentialDisabled(UserId user) =>
        Event(
            AuditActionNames.BlockedLastAdminCredentialDisabled,
            Target(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Rejected,
            ("userId", user.Value));

    /// <summary>A change that needs recent strong authentication was refused because it was not recent enough.</summary>
    public static AuditEvent StrongAuthenticationInsufficient(UserId user, string code) =>
        Event(
            AuditActionNames.StrongAuthenticationInsufficient,
            Target(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Denied,
            ("userId", user.Value),
            ("code", code));

    /// <summary>
    /// A change to the system-managed Recovery Access account was refused. The actor is the person who attempted it and the
    /// target is the protected user: the earlier rows recorded the protected account as the actor (gate §15.3).
    /// </summary>
    public static AuditEvent UnsafeOperationBlocked(UserId protectedUser, string operation) =>
        Event(
            AuditActionNames.UnsafeOperationBlocked,
            Target(AuditTargetTypes.User, protectedUser.Value),
            AuditOutcomeCodes.Rejected,
            ("userId", protectedUser.Value),
            ("operation", operation));

    private static string SourceName(IdentitySource source) => source.ToString().ToLowerInvariant();
}
