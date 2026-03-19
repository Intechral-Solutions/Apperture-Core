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
    /// Gets a page of users. Requires user.read.
    /// </summary>
    ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetUserPageAsync(int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user by id. Requires user.read.
    /// </summary>
    ValueTask<User?> GetUserByIdAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all roles. Requires user.read.
    /// </summary>
    ValueTask<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the explicit permission assignments for a role. Requires user.read.
    /// </summary>
    ValueTask<IReadOnlyList<PermissionDescriptor>> GetRolePermissionsAsync(RoleId roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces explicit permission assignments for a role. Requires permission.assign.
    /// </summary>
    ValueTask SetRolePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new user with one initial identity link. Optional password when source is Local. Requires user.manage.
    /// </summary>
    ValueTask<UserId> CreateUserAsync(string displayName, IdentitySource source, string externalId, string? password = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an identity link to a user. For Local, externalId is normalized (trim, lowercase). Requires user.manage.
    /// </summary>
    ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default);

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
    /// </summary>
    ValueTask SetLocalCredentialDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default);
}
