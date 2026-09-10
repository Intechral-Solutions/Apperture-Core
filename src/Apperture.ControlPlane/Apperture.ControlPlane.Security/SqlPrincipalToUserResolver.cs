using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Resolves a Human IActor to UserId using persisted identity links.
/// Local identities resolve by source + external id. Entra identities resolve by tenant id + object id.
/// </summary>
public sealed class SqlPrincipalToUserResolver : IPrincipalToUserResolver
{
    private readonly IUserRepository _userRepository;
    private readonly IEntraPrimaryAuthenticationPolicy? _entraPrimaryPolicy;
    private readonly ILogger<SqlPrincipalToUserResolver> _logger;

    public SqlPrincipalToUserResolver(
        IUserRepository userRepository,
        IEntraPrimaryAuthenticationPolicy? entraPrimaryPolicy = null,
        ILogger<SqlPrincipalToUserResolver>? logger = null)
    {
        _userRepository = userRepository;
        _entraPrimaryPolicy = entraPrimaryPolicy;
        _logger = logger ?? NullLogger<SqlPrincipalToUserResolver>.Instance;
    }

    /// <inheritdoc />
    public async ValueTask<UserId?> ResolveUserIdAsync(IActor actor, CancellationToken cancellationToken = default)
    {
        if (actor is null || actor.Kind != ActorKind.Human)
            return null;

        var externalId = actor.Id.Value;
        if (string.IsNullOrEmpty(externalId))
            return null;

        User? user;
        if (actor is HumanActor human && human.PreferredIdentitySource is { } source)
        {
            if (source == IdentitySource.Entra)
            {
                var tenantId = EntraTokenClaims.Normalize(human.TenantId);
                if (string.IsNullOrEmpty(tenantId) || !EntraTokenClaims.IsValidObjectId(tenantId))
                    return null;

                var key = ProviderIdentityKey.Entra(tenantId, externalId);
                user = await _userRepository.GetByIdentityLinkAsync(key, cancellationToken);
                if (user is not null)
                {
                    var wasPendingEntra = WasPendingEntra(user, key);
                    await _userRepository.UpdateLastAuthenticatedAtAsync(key, DateTimeOffset.UtcNow, cancellationToken);
                    if (wasPendingEntra)
                        await ApplyEntraPrimaryPolicyAsync(user.Id, cancellationToken);
                }

                return user?.Id;
            }

            user = await _userRepository.GetByIdentityLinkAsync(source, externalId, cancellationToken);
            if (user is not null)
                await _userRepository.UpdateLastAuthenticatedAtAsync(source, externalId, DateTimeOffset.UtcNow, cancellationToken);
            return user?.Id;
        }

        if (actor is HumanActor withTenant && !string.IsNullOrEmpty(withTenant.TenantId))
        {
            var tenantId = EntraTokenClaims.Normalize(withTenant.TenantId);
            if (!string.IsNullOrEmpty(tenantId) && EntraTokenClaims.IsValidObjectId(tenantId) && EntraTokenClaims.IsValidObjectId(externalId))
            {
                var key = ProviderIdentityKey.Entra(tenantId, externalId);
                user = await _userRepository.GetByIdentityLinkAsync(key, cancellationToken);
                if (user is not null)
                {
                    var wasPendingEntra = WasPendingEntra(user, key);
                    await _userRepository.UpdateLastAuthenticatedAtAsync(key, DateTimeOffset.UtcNow, cancellationToken);
                    if (wasPendingEntra)
                        await ApplyEntraPrimaryPolicyAsync(user.Id, cancellationToken);
                    return user.Id;
                }
            }
        }

        user = await _userRepository.GetByIdentityLinkAsync(IdentitySource.Local, externalId, cancellationToken);
        if (user is not null)
            await _userRepository.UpdateLastAuthenticatedAtAsync(IdentitySource.Local, externalId, DateTimeOffset.UtcNow, cancellationToken);

        return user?.Id;
    }

    private static bool WasPendingEntra(User user, ProviderIdentityKey identity) =>
        user.IdentityLinks.Any(l => identity.Matches(l) && !l.IsProven);

    private async ValueTask ApplyEntraPrimaryPolicyAsync(UserId userId, CancellationToken cancellationToken)
    {
        if (_entraPrimaryPolicy is null)
            return;
        try
        {
            await _entraPrimaryPolicy.ApplyAfterEntraProvenAsync(userId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Entra-primary local-authentication policy could not be applied for {UserId}.", userId.Value);
        }
    }
}
