using Apperture.Core.Application.Notifications.Instances;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Polls notification delivery outbox and dispatches email from stored instance snapshots.
/// </summary>
public sealed class NotificationDispatchHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<NotificationDeliveryOptions> _options;
    private readonly ILogger<NotificationDispatchHostedService> _logger;

    public NotificationDispatchHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<NotificationDeliveryOptions> options,
        ILogger<NotificationDispatchHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Max(5, _options.Value.PollIntervalSeconds));
        _logger.LogInformation(
            "Notification dispatch hosted service started (Delivery:Enabled={Enabled}, poll={PollSeconds}s).",
            _options.Value.Enabled,
            pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<NotificationDispatchProcessor>();
                var processed = await processor.ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
                if (processed > 0)
                {
                    _logger.LogDebug("Notification dispatch processed {Count} outbox rows.", processed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification dispatch batch failed.");
            }

            try
            {
                await Task.Delay(pollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
