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

    ValueTask UpsertIntegrationRoutingAsync(
        IntegrationId integrationId,
        string conditionKey,
        AttentionRoutingUpsertCommand command,
        CancellationToken cancellationToken = default);

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
    /// </remarks>
    ValueTask<bool> ClearIntegrationRoutingAsync(
        IntegrationId integrationId,
        string conditionKey,
        CancellationToken cancellationToken = default);
}
