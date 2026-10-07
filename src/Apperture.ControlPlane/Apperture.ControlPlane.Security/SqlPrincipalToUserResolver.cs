using Apperture.Core.Application.Audit;
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
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;

    public SqlPrincipalToUserResolver(
        IUserRepository userRepository,
        IEntraPrimaryAuthenticationPolicy? entraPrimaryPolicy = null,
        ILogger<SqlPrincipalToUserResolver>? logger = null,
        IAuditedUnitOfWork? auditedUnitOfWork = null)
    {
        _userRepository = userRepository;
        _entraPrimaryPolicy = entraPrimaryPolicy;
        _logger = logger ?? NullLogger<SqlPrincipalToUserResolver>.Instance;
        _auditedUnitOfWork = auditedUnitOfWork;
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

            if (entraLink.IsProven)
            {
                // Session bookkeeping on a proven link: no security state changes, so no row (D29).
                await _userRepository.UpdateLastAuthenticatedAtAsync(key, DateTimeOffset.UtcNow, cancellationToken);
                return user.Id;
            }

            // A pending link (re-added by an administrator while this session lived) is proven by its first authenticated
            // use, as before, but in its own unit with its identity_link.entra.proven row (AUD-1.2 review). Binding does not
            // depend on the proof, so a proof that cannot be recorded leaves the link pending and the request resolved.
            if (await TryProvePendingAsync(user.Id, key, cancellationToken))
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

    /// <summary>
    /// Proves the session's pending link as the user this host-issued session belongs to (its user id and the exact tenant id
    /// and object id it was issued through). True when the proof committed; nothing is proven without its row.
    /// </summary>
    private async ValueTask<bool> TryProvePendingAsync(UserId userId, ProviderIdentityKey key, CancellationToken cancellationToken)
    {
        if (_auditedUnitOfWork is null)
            return false;

        var provenAt = DateTimeOffset.UtcNow;
        try
        {
            return await _auditedUnitOfWork.ExecuteAsync(
                new AuditedOperation(AuditActionNames.EntraLinkProven, AuditActor.AuthenticatedUser(userId)),
                async (transaction, ct) =>
                {
                    var current = await _userRepository.FindIdentityLinkAsync(key, ct).ConfigureAwait(false);
                    if (current is null || current.UserId != userId || current.Link.IsProven || current.Link.LinkId is not { } linkId)
                    {
                        transaction.DeclareNoOp();
                        return false;
                    }

                    await _userRepository.UpdateLastAuthenticatedAtAsync(key, provenAt, ct).ConfigureAwait(false);
                    await transaction.StageAsync(AuditEvents.EntraLinkProven(userId, linkId, key.TenantId!, key.ExternalId), ct).ConfigureAwait(false);
                    return true;
                },
                (_, _) => Task.FromResult(true),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogNotApplied(ex, "The pending Entra link of {UserId} could not be proven; it stays pending.", userId);
            return false;
        }
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
            LogNotApplied(ex, "Entra-primary local-authentication policy could not be applied for {UserId}.", userId);
        }
    }

    /// <summary>An unknown outcome may have applied the change: it is logged as unknown, with its operation id.</summary>
    private void LogNotApplied(Exception ex, string message, UserId userId)
    {
        if (ex is AuditOutcomeUnknownException unknown)
        {
            _logger.LogError(ex, "A change for {UserId} has an unknown outcome (operation {OperationEventId}); it may have been applied.", userId.Value, unknown.OperationEventId);
            return;
        }

        _logger.LogWarning(ex, message, userId.Value);
    }
}
