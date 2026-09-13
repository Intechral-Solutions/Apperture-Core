namespace Apperture.Core.Application.PlatformEvents;

public interface IPlatformEventEmitter
{
    ValueTask<PlatformEventEnvelope> EmitAsync(PlatformEventEmitRequest request, CancellationToken cancellationToken = default);
}
