using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Provides and resolves roles for actors. Supports system and custom roles. Read-only: who holds a role changes only through the
/// audited user-management and provisioning units, never through this provider.
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
}
