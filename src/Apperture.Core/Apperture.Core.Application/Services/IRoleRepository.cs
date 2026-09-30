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

    /// <summary>
    /// Inserts a user-created role row, including its initial permission JSON, in one save.
    /// Throws <see cref="InvalidOperationException"/> when the name collides with <c>IX_Roles_Name</c>.
    /// </summary>
    ValueTask CreateAsync(Role role, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>
    /// Replaces name and description. Does not change permissions or <c>IsSystemRole</c>.
    /// Throws <see cref="InvalidOperationException"/> when the role is missing or the name collides.
    /// </summary>
    ValueTask UpdateMetadataAsync(RoleId roleId, string name, string? description, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>
    /// Deletes the role row only when a locked assignment count is zero. Never removes assignments.
    /// </summary>
    ValueTask<RoleUnassignedDeleteResult> TryDeleteUnassignedAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
