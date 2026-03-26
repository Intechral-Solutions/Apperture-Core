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
}
