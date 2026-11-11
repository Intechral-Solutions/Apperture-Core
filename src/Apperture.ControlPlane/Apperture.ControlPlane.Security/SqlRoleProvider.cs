using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Persisted, read-only implementation of IRoleProvider. Delegates to IRoleRepository.
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
}
