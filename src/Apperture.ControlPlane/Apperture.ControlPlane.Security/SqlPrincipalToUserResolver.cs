using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Resolves the human actor of an application session to its user: by user id, then requires the identity link
/// the session was issued through to still belong to that user (session-link binding), then touches that link's
/// last-authenticated time. An Entra session needs the exact tenant id + object id link; a local session
/// (Recovery Access included) needs a Local link, whatever its current name, so a username rename does not end
/// the session but removing the link does. No caching: every call reads the user.
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
        // Only a human actor mapped from an application session carries the provenance needed for binding.
        if (actor is not HumanActor human || human.PreferredIdentitySource is not { } source)
            return null;

        var userIdValue = human.Id.Value;
        if (string.IsNullOrWhiteSpace(userIdValue))
            return null;

        var user = await _userRepository.GetByIdAsync(new UserId(userIdValue), cancellationToken);
        if (user is null)
            return null;

        if (source == IdentitySource.Entra)
        {
            var tenantId = EntraTokenClaims.Normalize(human.TenantId);
            var objectId = EntraTokenClaims.Normalize(human.EntraObjectId);
            if (!EntraTokenClaims.IsValidObjectId(tenantId) || !EntraTokenClaims.IsValidObjectId(objectId))
                return null;

            // The links come from the user just loaded, so a link that moved to another user cannot match.
            var key = ProviderIdentityKey.Entra(tenantId!, objectId!);
            var entraLink = user.IdentityLinks.FirstOrDefault(key.Matches);
            if (entraLink is null)
                return null;

            await _userRepository.UpdateLastAuthenticatedAtAsync(key, DateTimeOffset.UtcNow, cancellationToken);
            if (!entraLink.IsProven)
                await ApplyEntraPrimaryPolicyAsync(user.Id, cancellationToken);
            return user.Id;
        }

        if (source != IdentitySource.Local)
            return null;

        // Bound to the user's Local link by existence, not by name: it mirrors the current username.
        var localLink = user.IdentityLinks.FirstOrDefault(l => l.Source == IdentitySource.Local);
        if (localLink is null)
            return null;

        await _userRepository.UpdateLastAuthenticatedAtAsync(IdentitySource.Local, localLink.ExternalId, DateTimeOffset.UtcNow, cancellationToken);
        return user.Id;
    }

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
