using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Domain.Notifications;

namespace Apperture.ControlPlane.Security;

public interface INotificationInstanceQueryService
{
    ValueTask<IReadOnlyList<NotificationInstance>> ListAsync(
        NotificationInstanceListQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationInstance?> GetAsync(string id, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<NotificationDeliveryOutbox>> ListOutboxAsync(
        string instanceId,
        CancellationToken cancellationToken = default);
}

public sealed class NotificationInstanceQueryService : INotificationInstanceQueryService
{
    private readonly INotificationInstanceStore _store;
    private readonly NotificationDefinitionAuthorization _authorization;

    public NotificationInstanceQueryService(
        INotificationInstanceStore store,
        NotificationDefinitionAuthorization authorization)
    {
        _store = store;
        _authorization = authorization;
    }

    public async ValueTask<IReadOnlyList<NotificationInstance>> ListAsync(
        NotificationInstanceListQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(query.IntegrationId))
            await _authorization.RequireIntegrationReadAsync(query.IntegrationId, cancellationToken)
                .ConfigureAwait(false);
        else
            await _authorization.RequirePlatformReadAsync(cancellationToken).ConfigureAwait(false);

        return await _store.ListAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<NotificationInstance?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var row = await _store.GetByIdAsync(NotificationInstanceId.From(id), cancellationToken).ConfigureAwait(false);
        if (row is null)
            return null;

        await AuthorizeForDefinitionAsync(row.NotificationDefinitionId, row.IntegrationId, cancellationToken)
            .ConfigureAwait(false);
        return row;
    }

    public async ValueTask<IReadOnlyList<NotificationDeliveryOutbox>> ListOutboxAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        var row = await _store.GetByIdAsync(NotificationInstanceId.From(instanceId), cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return [];

        await AuthorizeForDefinitionAsync(row.NotificationDefinitionId, row.IntegrationId, cancellationToken)
            .ConfigureAwait(false);
        return await _store.ListOutboxForInstanceAsync(NotificationInstanceId.From(instanceId), cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask AuthorizeForDefinitionAsync(
        string definitionId,
        string? integrationId,
        CancellationToken cancellationToken)
    {
        await _authorization
            .RequireDefinitionAccessAsync(integrationId, manage: false, cancellationToken)
            .ConfigureAwait(false);
    }
}
