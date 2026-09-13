using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Persistence for platform roles and user-role assignments.
/// </summary>
public interface IRoleRepository
{
    /// <summary>
    /// Gets a role by id.
    /// </summary>
    ValueTask<Role?> GetAsync(RoleId roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all roles.
    /// </summary>
    ValueTask<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets roles assigned to a user.
    /// </summary>
    ValueTask<IReadOnlyList<Role>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts how many users have the given role assigned.
    /// </summary>
    ValueTask<int> CountUserAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists user ids that currently have the given role. Default is empty so test doubles need not implement it.
    /// </summary>
    ValueTask<IReadOnlyList<UserId>> ListUserIdsWithRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<UserId>>(Array.Empty<UserId>());

    /// <summary>
    /// Assigns a role to a user.
    /// </summary>
    ValueTask AssignToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a role assignment from a user.
    /// </summary>
    ValueTask RemoveFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the permissions for a role. Replaces PermissionsJson.
    /// </summary>
    ValueTask UpdatePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default);
}
