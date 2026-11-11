using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Stub implementation of IRoleProvider. Returns no roles.
/// Used when role persistence is not yet implemented.
/// </summary>
public sealed class StubRoleProvider : IRoleProvider
{
    private static readonly IReadOnlyList<Role> EmptyRoles = [];

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(EmptyRoles);

    /// <inheritdoc />
    public ValueTask<Role?> GetRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<Role?>(null);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<Role>> GetRolesForUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(EmptyRoles);
}
