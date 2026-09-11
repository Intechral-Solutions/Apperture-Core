using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Observes schedule expectation truth on an interval and persists <c>expected_run_missing</c> incidents.
/// Still-active observations evaluate reminder and escalation through the platform notification pipeline.
/// </summary>
public sealed class AttentionIncidentObservationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<AttentionIncidentObservationOptions> _options;
    private readonly ILogger<AttentionIncidentObservationHostedService> _logger;

    public AttentionIncidentObservationHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<AttentionIncidentObservationOptions> options,
        ILogger<AttentionIncidentObservationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;
        if (!opts.Enabled)
        {
            _logger.LogInformation("Attention incident observation is disabled (AttentionIncidents:Observation:Enabled=false).");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ObserveCycleAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Attention incident observation cycle failed.");
            }

            try
            {
                await Task.Delay(opts.Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ObserveCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var integrations = scope.ServiceProvider.GetRequiredService<IIntegrationRepository>();
        var readiness = scope.ServiceProvider.GetRequiredService<IIntegrationReadinessService>();
        var tracker = scope.ServiceProvider.GetRequiredService<IAttentionIncidentTracker>();
        var platformEvents = scope.ServiceProvider.GetRequiredService<ScheduleExpectationPlatformEventDualEmitter>();
        var notificationEvaluation = scope.ServiceProvider.GetRequiredService<INotificationEvaluateOnlyCoordinator>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var observedAt = clock.GetUtcNow();

        var visible = await integrations.GetVisibleAsync(ScopeKeyConstants.Instance, cancellationToken).ConfigureAwait(false);
        foreach (var integration in visible)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await readiness.EvaluateScheduleExpectationForBackgroundAsync(integration.Id, cancellationToken)
                .ConfigureAwait(false);
            if (snapshot is null)
                continue;

            var observe = await tracker
                .ObserveScheduleExpectationAsync(integration.Id, snapshot, observedAt, cancellationToken)
                .ConfigureAwait(false);

            await platformEvents
                .TryEmitForObserveResultAsync(integration.Id, observe, snapshot, observedAt, cancellationToken)
                .ConfigureAwait(false);

            if (observe.Transition == AttentionIncidentObserveTransition.ObservedStillActive)
            {
                try
                {
                    await notificationEvaluation
                        .EvaluateIncidentAsync(
                            new IncidentEvaluationContext(
                                integration.Id.Value,
                                observe.Transition,
                                observe.IncidentId,
                                snapshot,
                                observedAt),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Notification reminder/escalation evaluation failed for integration {IntegrationId}.",
                        integration.Id.Value);
                }
            }
        }
    }
}
