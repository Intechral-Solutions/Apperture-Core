using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Persisted implementation of IRoleProvider. Delegates to IRoleRepository.
/// </summary>
public sealed class SqlRoleProvider : IRoleProvider
{
    private readonly IRoleRepository _roleRepository;

    public SqlRoleProvider(IRoleRepository roleRepository)
    {
        _roleRepository = roleRepository;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default) =>
        _roleRepository.GetAllAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask<Role?> GetRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
        _roleRepository.GetAsync(roleId, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<Role>> GetRolesForUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
        _roleRepository.GetForUserAsync(userId, cancellationToken);

    /// <inheritdoc />
    public ValueTask AssignRoleToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
        _roleRepository.AssignToUserAsync(userId, roleId, cancellationToken);

    /// <inheritdoc />
    public ValueTask RemoveRoleFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
        _roleRepository.RemoveFromUserAsync(userId, roleId, cancellationToken);
}
