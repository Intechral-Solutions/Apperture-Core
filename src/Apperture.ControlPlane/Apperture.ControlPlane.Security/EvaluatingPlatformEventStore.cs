using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Persistence.SqlServer;

namespace Apperture.ControlPlane.Security;

/// <summary>SQL platform event store with evaluate-only notification rule matching after append.</summary>
public sealed class EvaluatingPlatformEventStore : IPlatformEventStore
{
    private readonly SqlPlatformEventStore _inner;
    private readonly INotificationEvaluateOnlyCoordinator _coordinator;

    public EvaluatingPlatformEventStore(
        SqlPlatformEventStore inner,
        INotificationEvaluateOnlyCoordinator coordinator)
    {
        _inner = inner;
        _coordinator = coordinator;
    }

    public async ValueTask<PlatformEventEnvelope> AppendAsync(
        PlatformEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        var saved = await _inner.AppendAsync(envelope, cancellationToken).ConfigureAwait(false);
        await _coordinator.EvaluatePlatformEventAsync(saved, cancellationToken).ConfigureAwait(false);
        return saved;
    }

    public ValueTask<PlatformEventQueryPage> QueryAsync(
        PlatformEventQuery query,
        CancellationToken cancellationToken = default) =>
        _inner.QueryAsync(query, cancellationToken);
}
