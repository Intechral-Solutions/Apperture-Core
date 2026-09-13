namespace Apperture.Core.Application.PlatformEvents;

public interface IPlatformEventStore
{
    ValueTask<PlatformEventEnvelope> AppendAsync(PlatformEventEnvelope envelope, CancellationToken cancellationToken = default);

    ValueTask<PlatformEventQueryPage> QueryAsync(PlatformEventQuery query, CancellationToken cancellationToken = default);
}
