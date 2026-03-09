using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Provides and resolves roles for actors. Supports system and custom roles.
/// </summary>
public interface IRoleProvider
{
    /// <summary>
    /// Gets all roles.
    /// </summary>
    ValueTask<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a role by id.
    /// </summary>
    ValueTask<Role?> GetRoleAsync(RoleId roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets roles assigned to a user.
    /// </summary>
    ValueTask<IReadOnlyList<Role>> GetRolesForUserAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a role to a user.
    /// </summary>
    ValueTask AssignRoleToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a role assignment from a user.
    /// </summary>
    ValueTask RemoveRoleFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default);
}
