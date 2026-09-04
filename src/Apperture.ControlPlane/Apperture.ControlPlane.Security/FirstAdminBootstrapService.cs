using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// First-admin bootstrap only; succeeds when zero platform users exist. Creates one user, one identity link, and assigns platform administrator role in a single transaction.
/// For Local source, externalId is normalized (trim, lowercase). Optional password creates local credential when source is Local.
/// </summary>
public sealed class FirstAdminBootstrapService : IFirstAdminBootstrapService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ILocalCredentialRepository? _localCredentialRepository;
    private readonly ILocalPasswordHasher? _passwordHasher;

    public FirstAdminBootstrapService(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        ILocalCredentialRepository? localCredentialRepository = null,
        ILocalPasswordHasher? passwordHasher = null)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _localCredentialRepository = localCredentialRepository;
        _passwordHasher = passwordHasher;
    }

    /// <inheritdoc />
    public async Task<FirstAdminBootstrapResult> TryCreateFirstAdminAsync(
        string displayName,
        IdentitySource source,
        string externalId,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var count = await _userRepository.GetUserCountAsync(cancellationToken);
        if (count > 0)
            return FirstAdminBootstrapResult.AlreadyExists;

        var role = await _roleRepository.GetAsync(new RoleId(BootstrapConstants.PlatformAdminRoleId), cancellationToken);
        if (role is null)
            return FirstAdminBootstrapResult.RoleNotFound;

        // For Local, use normalized username as IdentityLink.ExternalId (canonical for login).
        var linkExternalId = source == IdentitySource.Local
            ? LocalAuthHelpers.NormalizeUsername(externalId)
            : externalId.Trim();

        if (source == IdentitySource.Local && EmergencyAccessIdentity.IsReservedLocalUsername(linkExternalId))
            return FirstAdminBootstrapResult.DuplicateIdentityLink;

        var existing = await _userRepository.GetByIdentityLinkAsync(source, linkExternalId, cancellationToken);
        if (existing is not null)
            return FirstAdminBootstrapResult.DuplicateIdentityLink;

        var userId = new UserId(Guid.NewGuid().ToString("N")[..24]);
        var user = new User(
            userId,
            displayName,
            new[] { new IdentityLink(source, linkExternalId, LastAuthenticatedAt: null) },
            new[] { new RoleId(BootstrapConstants.PlatformAdminRoleId) },
            IsBreakGlassEligible: false);

        if (source == IdentitySource.Local && !string.IsNullOrEmpty(password) &&
            _localCredentialRepository is not null && _passwordHasher is not null)
        {
            LocalPasswordPolicy.Require(
                password,
                LocalPasswordPolicy.ForLocalIdentity(linkExternalId, displayName));
        }

        await _userRepository.AddAsync(user, cancellationToken);

        if (source == IdentitySource.Local && !string.IsNullOrEmpty(password) &&
            _localCredentialRepository is not null && _passwordHasher is not null)
        {
            var (hash, salt) = _passwordHasher.HashPassword(password);
            await _localCredentialRepository.SetPasswordAsync(userId, hash, salt, _passwordHasher.AlgorithmName, cancellationToken);
        }

        return FirstAdminBootstrapResult.Success(userId);
    }

    /// <inheritdoc />
    public async Task<BootstrapSetupStatus> GetSetupStatusAsync(CancellationToken cancellationToken = default)
    {
        var count = await _userRepository.GetUserCountAsync(cancellationToken);
        return BootstrapSetupStatus.FromUserCount(count);
    }
}

/// <summary>
/// Helpers for local auth. Normalized username is trim + lowercase (canonical for IdentityLink.ExternalId when source is Local).
/// </summary>
public static class LocalAuthHelpers
{
    public static string NormalizeUsername(string username) => username.Trim().ToLowerInvariant();
}
