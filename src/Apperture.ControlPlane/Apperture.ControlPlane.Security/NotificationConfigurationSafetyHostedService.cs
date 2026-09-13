using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Application.PlatformEvents;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Logs warnings at startup for dangerous notification flag combinations.
/// </summary>
public sealed class NotificationConfigurationSafetyHostedService : IHostedService
{
    private readonly IOptions<PlatformEventsOptions> _platformEvents;
    private readonly IOptions<NotificationEvaluationOptions> _evaluation;
    private readonly IOptions<NotificationInstanceOptions> _instances;
    private readonly IOptions<NotificationDeliveryOptions> _delivery;
    private readonly ILogger<NotificationConfigurationSafetyHostedService> _logger;

    public NotificationConfigurationSafetyHostedService(
        IOptions<PlatformEventsOptions> platformEvents,
        IOptions<NotificationEvaluationOptions> evaluation,
        IOptions<NotificationInstanceOptions> instances,
        IOptions<NotificationDeliveryOptions> delivery,
        ILogger<NotificationConfigurationSafetyHostedService> logger)
    {
        _platformEvents = platformEvents;
        _evaluation = evaluation;
        _instances = instances;
        _delivery = delivery;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var snapshot = new NotificationConfigurationSnapshot(
            _platformEvents.Value.Enabled,
            _evaluation.Value.Enabled,
            _instances.Value.Enabled,
            _delivery.Value.Enabled);

        foreach (var warning in NotificationConfigurationSafetyValidator.Validate(snapshot))
        {
            _logger.LogWarning(
                "Notification configuration safety [{Code}]: {Message}",
                warning.Code,
                warning.Message);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
