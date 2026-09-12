using Microsoft.Extensions.Options;

namespace Apperture.Core.Application.PlatformEvents;

public sealed class PlatformEventEmitter : IPlatformEventEmitter
{
    private readonly IPlatformEventCatalog _catalog;
    private readonly IPlatformEventStore _store;
    private readonly IOptions<PlatformEventsOptions> _options;
    private readonly TimeProvider _time;

    public PlatformEventEmitter(
        IPlatformEventCatalog catalog,
        IPlatformEventStore store,
        IOptions<PlatformEventsOptions> options,
        TimeProvider time)
    {
        _catalog = catalog;
        _store = store;
        _options = options;
        _time = time;
    }

    public async ValueTask<PlatformEventEnvelope> EmitAsync(
        PlatformEventEmitRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Value.Enabled)
        {
            throw new InvalidOperationException(
                "Platform event emission is disabled (PlatformEvents:Enabled=false).");
        }

        var descriptor = _catalog.GetRequired(request.Type);
        PlatformEventCatalog.ValidateRequest(request, descriptor);

        var recordedAt = _time.GetUtcNow();
        var envelope = new PlatformEventEnvelope(
            request.Id ?? PlatformEventId.New(),
            request.Type,
            request.Source,
            request.SpecVersion,
            request.DataContentType,
            request.DataJson,
            request.OccurredAtUtc,
            recordedAt,
            request.Severity,
            request.Subject,
            request.DedupeKey,
            request.IntegrationId,
            request.ModuleId,
            request.ProcedureId,
            request.RunId,
            request.ScheduleId,
            request.ActorId,
            request.CorrelationId,
            request.CausationId,
            request.ResourceContextId);

        return await _store.AppendAsync(envelope, cancellationToken).ConfigureAwait(false);
    }
}
