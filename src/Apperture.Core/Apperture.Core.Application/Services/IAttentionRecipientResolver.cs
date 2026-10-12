using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Integration;

namespace Apperture.Core.Application.Services;

public interface IAttentionRecipientResolver
{
    ValueTask<AttentionRecipientResolution> ResolveAsync(
        IntegrationId integrationId,
        string conditionKey,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves routing using only the instance default row (no integration override).</summary>
    ValueTask<AttentionRecipientResolution> ResolveInstanceDefaultsAsync(
        string conditionKey,
        CancellationToken cancellationToken = default);
}

public interface IAttentionRoutingManagementService
{
    ValueTask<AttentionRecipientResolution> GetEffectiveRoutingAsync(
        IntegrationId integrationId,
        string conditionKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates an Integration's attention-routing override for one condition. The write runs in one transaction that
    /// first takes the integration row lock and requires the integration to exist, so an integration delete that wins the lock
    /// makes this refuse, and an override that commits first is visible to the delete's blocker check (AUD-1.4).
    /// </summary>
    /// <param name="reauthorize">
    /// The caller's own authorization check, run again once the lock is held, so a decision taken before an ownership move
    /// committed is not acted on afterwards. The override belongs to the integration, so its authorization depends on the
    /// integration: a caller must pass the check it made before the call and must not leave it out to skip the revalidation.
    /// Null is legitimate only for a caller that made no integration-dependent decision. The routing endpoint passes it, and the
    /// check it makes is <c>integration.update</c> on the integration (reading it is not enough to change its routing).
    /// </param>
    /// <exception cref="InvalidOperationException">The integration, or a named operational team, does not exist.</exception>
    ValueTask UpsertIntegrationRoutingAsync(
        IntegrationId integrationId,
        string conditionKey,
        AttentionRoutingUpsertCommand command,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, ValueTask>? reauthorize = null);

    ValueTask UpsertInstanceDefaultRoutingAsync(
        string conditionKey,
        AttentionRoutingUpsertCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an Integration's attention-routing override for one condition, reverting it to the instance
    /// default. Returns false when no override existed.
    /// </summary>
    /// <remarks>
    /// CTX-3B Plan 6a: Integration hard-delete blocks on attention-routing overrides, and before this
    /// operation existed there was no way for an operator to clear that blocker. The service previously
    /// exposed upsert only.
    /// <para>The removal runs in one transaction that first takes the integration row lock, like
    /// <see cref="UpsertIntegrationRoutingAsync"/>, so an integration delete that wins the lock makes it refuse and an ownership
    /// move cannot commit between the caller's authorization and the removal. A missing override is still a no-op that returns
    /// false.</para>
    /// </remarks>
    /// <param name="reauthorize">
    /// The caller's own authorization check, run again once the lock is held. As for the upsert, a caller whose authorization
    /// depends on the integration must pass the check it made before the call and must not leave it out to skip the
    /// revalidation. The routing endpoint passes <c>integration.update</c> on the integration.
    /// </param>
    /// <exception cref="InvalidOperationException">The integration does not exist.</exception>
    ValueTask<bool> ClearIntegrationRoutingAsync(
        IntegrationId integrationId,
        string conditionKey,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, ValueTask>? reauthorize = null);
}
