using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Resolves a Human IActor to UserId using persisted identity links.
/// Provider-agnostic: looks up by (IdentitySource, ExternalId) where actor.Id is the external id.
/// </summary>
public sealed class SqlPrincipalToUserResolver : IPrincipalToUserResolver
{
    private readonly IUserRepository _userRepository;

    public SqlPrincipalToUserResolver(IUserRepository userRepository)
    {
        _userRepository = userRepository;
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
                await _userRepository.UpdateLastAuthenticatedAtAsync(source, externalId, DateTimeOffset.UtcNow, cancellationToken);
            return user?.Id;
        }

        user = await _userRepository.GetByIdentityLinkAsync(IdentitySource.Entra, externalId, cancellationToken);
        if (user is not null)
        {
            await _userRepository.UpdateLastAuthenticatedAtAsync(IdentitySource.Entra, externalId, DateTimeOffset.UtcNow, cancellationToken);
            return user.Id;
        }

        user = await _userRepository.GetByIdentityLinkAsync(IdentitySource.Local, externalId, cancellationToken);
        if (user is not null)
            await _userRepository.UpdateLastAuthenticatedAtAsync(IdentitySource.Local, externalId, DateTimeOffset.UtcNow, cancellationToken);

        return user?.Id;
    }
}
