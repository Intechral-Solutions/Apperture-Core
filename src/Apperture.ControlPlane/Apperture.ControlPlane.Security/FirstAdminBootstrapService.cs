using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// First-admin bootstrap only; succeeds when zero platform users exist. Creates one user, one identity link, and assigns platform administrator role in a single transaction.
/// </summary>
public sealed class FirstAdminBootstrapService : IFirstAdminBootstrapService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;

    public FirstAdminBootstrapService(IUserRepository userRepository, IRoleRepository roleRepository)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
    }

    /// <inheritdoc />
    public async Task<FirstAdminBootstrapResult> TryCreateFirstAdminAsync(
        string displayName,
        IdentitySource source,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        var count = await _userRepository.GetUserCountAsync(cancellationToken);
        if (count > 0)
            return FirstAdminBootstrapResult.AlreadyExists;

        var role = await _roleRepository.GetAsync(new RoleId(BootstrapConstants.PlatformAdminRoleId), cancellationToken);
        if (role is null)
            return FirstAdminBootstrapResult.RoleNotFound;

        var existing = await _userRepository.GetByIdentityLinkAsync(source, externalId, cancellationToken);
        if (existing is not null)
            return FirstAdminBootstrapResult.DuplicateIdentityLink;

        var userId = new UserId(Guid.NewGuid().ToString("N")[..24]);
        var user = new User(
            userId,
            displayName,
            new[] { new IdentityLink(source, externalId, LastAuthenticatedAt: null) },
            new[] { new RoleId(BootstrapConstants.PlatformAdminRoleId) },
            IsBreakGlassEligible: false);

        await _userRepository.AddAsync(user, cancellationToken);
        return FirstAdminBootstrapResult.Success(userId);
    }
}
