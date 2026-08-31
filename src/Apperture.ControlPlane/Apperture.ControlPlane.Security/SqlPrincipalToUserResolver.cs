using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Resolves a Human IActor to UserId using persisted identity links.
/// Provider-agnostic: looks up by (IdentitySource, ExternalId) where actor.Id is the external id.
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
            user = await _userRepository.GetByIdentityLinkAsync(source, externalId, cancellationToken);
            if (user is not null)
            {
                // First Entra proof on this request path (test-auth). Subsequent API requests must not
                // re-apply policy; live OIDC re-evaluates from CompleteSignInAsync instead.
                var wasPendingEntra = source == IdentitySource.Entra && WasPendingEntra(user, externalId);
                await _userRepository.UpdateLastAuthenticatedAtAsync(source, externalId, DateTimeOffset.UtcNow, cancellationToken);
                if (wasPendingEntra)
                    await ApplyEntraPrimaryPolicyAsync(user.Id, cancellationToken);
            }
            return user?.Id;
        }

            user = await _userRepository.GetByIdentityLinkAsync(IdentitySource.Entra, externalId, cancellationToken);
            if (user is not null)
            {
                // First Entra proof on this request path (test-auth). Subsequent API requests must not
                // re-apply policy; live OIDC re-evaluates from CompleteSignInAsync instead.
                var wasPendingEntra = WasPendingEntra(user, externalId);
            await _userRepository.UpdateLastAuthenticatedAtAsync(IdentitySource.Entra, externalId, DateTimeOffset.UtcNow, cancellationToken);
            if (wasPendingEntra)
                await ApplyEntraPrimaryPolicyAsync(user.Id, cancellationToken);
            return user.Id;
        }

        user = await _userRepository.GetByIdentityLinkAsync(IdentitySource.Local, externalId, cancellationToken);
        if (user is not null)
            await _userRepository.UpdateLastAuthenticatedAtAsync(IdentitySource.Local, externalId, DateTimeOffset.UtcNow, cancellationToken);

        return user?.Id;
    }

    private static bool WasPendingEntra(User user, string externalId) =>
        user.IdentityLinks.Any(l =>
            l.Source == IdentitySource.Entra
            && string.Equals(l.ExternalId, externalId, StringComparison.OrdinalIgnoreCase)
            && !l.IsProven);

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
