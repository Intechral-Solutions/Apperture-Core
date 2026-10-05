using Apperture.Core.Application.Accounts;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Auth administration: user list/detail, user creation, identity-link and role management, local credential admin.
/// All operations are permission-protected and audited by the implementation.
/// </summary>
public interface IUserManagementService
{
    /// <summary>
    /// Gets a page of users, optionally filtered by <paramref name="query"/> (a username prefix or a display-name
    /// substring; see <see cref="UserSearchCriteria"/>). Take is clamped to 1 through 100. Requires user.read. The
    /// query is never written to audit.
    /// </summary>
    ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetUserPageAsync(int skip, int take, CancellationToken cancellationToken = default, string? query = null);

    /// <summary>
    /// Gets a user by id. Requires user.read.
    /// </summary>
    ValueTask<User?> GetUserByIdAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all roles. Requires user.read.
    /// </summary>
    ValueTask<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a role by id. Requires user.read. Returns null if not found.
    /// </summary>
    ValueTask<Role?> GetRoleAsync(RoleId roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the explicit permission assignments for a role. Requires user.read.
    /// </summary>
    ValueTask<IReadOnlyList<PermissionDescriptor>> GetRolePermissionsAsync(RoleId roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces explicit permission assignments for a role. Requires permission.assign.
    /// </summary>
    ValueTask SetRolePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a user-created role and optional initial grants in one operation. Requires role.manage.
    /// Non-empty initial grants also require permission.assign. Empty grants are allowed.
    /// </summary>
    ValueTask<Role> CreateRoleAsync(
        string name,
        string? description = null,
        IReadOnlyList<PermissionDescriptor>? permissions = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>
    /// Updates name and description only. Requires role.manage. System roles are refused.
    /// </summary>
    ValueTask<Role> UpdateRoleMetadataAsync(
        RoleId roleId,
        string name,
        string? description,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>
    /// Hard-deletes a user-created role with zero assignments. Requires role.manage.
    /// Does not unassign users. System roles and assigned roles are refused.
    /// </summary>
    ValueTask DeleteRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>
    /// Lists current holders of a role. Requires user.read. Returns null when the role is missing.
    /// </summary>
    ValueTask<RoleAssignmentList?> GetRoleAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>
    /// Creates a new user with one initial identity link. Local source requires a password hashed through
    /// the platform hasher (not a hash supplied by the caller). Requires user.manage.
    /// </summary>
    ValueTask<UserId> CreateUserAsync(
        string displayName,
        IdentitySource source,
        string externalId,
        string? password = null,
        CancellationToken cancellationToken = default,
        string? contactEmail = null,
        bool sendSetupInvitation = false);

    /// <summary>Sends or resends an Apperture account setup invitation. Requires user.manage.</summary>
    ValueTask<AccountInvitationResult> SendSetupInvitationAsync(
        UserId userId,
        string? contactEmail = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>
    /// Renames a user's username (including reclaiming one of the user's own retired names). The target must satisfy the
    /// current username policy; a name another user holds or held is refused (<see cref="UsernameErrorCodes.Taken"/>);
    /// Recovery Access is refused (<see cref="EmergencyAccessErrorCodes.UnsafeOperation"/>).
    /// Changes no credential, role or session. Audited when the name changes. Requires user.manage.
    /// </summary>
    ValueTask<UsernameRenameResult> RenameUsernameAsync(
        UserId userId,
        string requestedUsername,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>
    /// Adds an identity link to a user. For Local, externalId is optional and defaults to the user's username; when
    /// supplied it is normalized (trim, lowercase) and must equal the username. Entra requires an externalId.
    /// Requires user.manage.
    /// </summary>
    ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string? externalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an identity link. Fails if it is the user's last link. Requires user.manage.
    /// </summary>
    ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a role to a user. Requires role.assign.
    /// </summary>
    ValueTask AssignRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a role from a user. Requires role.assign.
    /// </summary>
    ValueTask RemoveRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets or resets the local password for a user. User must have a Local identity link. Creates credential row if missing. Requires credential.manage.
    /// </summary>
    ValueTask SetLocalPasswordAsync(UserId userId, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables the local credential for a user. User must have a Local identity link. Requires credential.manage.
    /// Re-enabling (disabled = false) also requires recent strong authentication.
    /// </summary>
    ValueTask SetLocalCredentialDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Instance emergency-access posture. Requires user.read. Does not include secrets.
    /// </summary>
    ValueTask<EmergencyAccessPosture> GetEmergencyAccessPostureAsync(CancellationToken cancellationToken = default);
}
