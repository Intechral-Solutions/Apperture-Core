using Apperture.Core.Application.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Evaluates notification rules for worker-persisted platform events (module signals) not yet linked to instances.
/// </summary>
public sealed class PlatformEventNotificationEvaluationProcessor
{
    private readonly IPlatformEventNotificationEvaluationStore _pendingEvents;
    private readonly INotificationEvaluateOnlyCoordinator _coordinator;
    private readonly IOptions<NotificationEvaluationOptions> _options;
    private readonly ILogger<PlatformEventNotificationEvaluationProcessor> _logger;

    public PlatformEventNotificationEvaluationProcessor(
        IPlatformEventNotificationEvaluationStore pendingEvents,
        INotificationEvaluateOnlyCoordinator coordinator,
        IOptions<NotificationEvaluationOptions> options,
        ILogger<PlatformEventNotificationEvaluationProcessor> logger)
    {
        _pendingEvents = pendingEvents;
        _coordinator = coordinator;
        _options = options;
        _logger = logger;
    }

    public async ValueTask<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Value.Enabled)
            return 0;

        var batchSize = Math.Max(1, _options.Value.BatchSize);
        var pending = await _pendingEvents
            .ListPendingEvaluationAsync(batchSize, cancellationToken)
            .ConfigureAwait(false);
        if (pending.Count == 0)
            return 0;

        var processed = 0;
        foreach (var envelope in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _coordinator
                    .EvaluatePlatformEventAsync(envelope, cancellationToken)
                    .ConfigureAwait(false);
                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Notification evaluation failed for platform event {EventId} type {EventType}.",
                    envelope.Id.Value,
                    envelope.Type);
            }
        }

        if (processed > 0)
        {
            _logger.LogInformation(
                "Evaluated {Count} worker platform event(s) for notifications.",
                processed);
        }

        return processed;
    }
}
