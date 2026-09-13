using Apperture.Core.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Polls worker-emitted platform events and runs notification rule evaluation on Management Host.
/// </summary>
public sealed class PlatformEventNotificationEvaluationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<NotificationEvaluationOptions> _options;
    private readonly ILogger<PlatformEventNotificationEvaluationHostedService> _logger;

    public PlatformEventNotificationEvaluationHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<NotificationEvaluationOptions> options,
        ILogger<PlatformEventNotificationEvaluationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Max(5, _options.Value.PollIntervalSeconds));
        _logger.LogInformation(
            "Platform event notification evaluation started (Evaluation:Enabled={Enabled}, poll={PollSeconds}s).",
            _options.Value.Enabled,
            pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<PlatformEventNotificationEvaluationProcessor>();
                await processor.ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Platform event notification evaluation batch failed.");
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
