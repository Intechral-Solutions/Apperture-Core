using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.PlatformSettings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

public sealed class EmailNotificationOptionsPlatformSettingsPostConfigure : IPostConfigureOptions<EmailNotificationOptions>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailNotificationOptionsPlatformSettingsPostConfigure> _logger;

    public EmailNotificationOptionsPlatformSettingsPostConfigure(
        IServiceScopeFactory scopeFactory,
        ILogger<EmailNotificationOptionsPlatformSettingsPostConfigure> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void PostConfigure(string? name, EmailNotificationOptions options)
    {
        PlatformSettingsOptionsOverlay.ApplyString(
            _scopeFactory, _logger, PlatformSettingKeys.NotificationsEmailSmtpHost, value => options.SmtpHost = value);
        PlatformSettingsOptionsOverlay.ApplyInt(
            _scopeFactory, _logger, PlatformSettingKeys.NotificationsEmailSmtpPort, value => options.SmtpPort = value);
        PlatformSettingsOptionsOverlay.ApplyBool(
            _scopeFactory, _logger, PlatformSettingKeys.NotificationsEmailSmtpUseSsl, value => options.SmtpUseSsl = value);
        PlatformSettingsOptionsOverlay.ApplyString(
            _scopeFactory, _logger, PlatformSettingKeys.NotificationsEmailSmtpUser, value => options.SmtpUser = value);
        PlatformSettingsOptionsOverlay.ApplyString(
            _scopeFactory, _logger, PlatformSettingKeys.NotificationsEmailFromAddress, value => options.FromAddress = value);
        PlatformSettingsOptionsOverlay.ApplyString(
            _scopeFactory, _logger, PlatformSettingKeys.NotificationsEmailFromDisplayName, value => options.FromDisplayName = value);
        // The reference is applied with where it came from: only the one stored in the setting's row is subject to the ownership rule.
        PlatformSettingsOptionsOverlay.ApplySecretReference(
            _scopeFactory, _logger, PlatformSettingKeys.NotificationsEmailSmtpPasswordRef,
            (value, fromRow) =>
            {
                options.SmtpPasswordRef = value;
                options.SmtpPasswordRefSource = fromRow ? SmtpPasswordRefSource.SettingRow : SmtpPasswordRefSource.Configuration;
            });
    }
}

public sealed class NotificationEvaluationOptionsPlatformSettingsPostConfigure : IPostConfigureOptions<NotificationEvaluationOptions>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationEvaluationOptionsPlatformSettingsPostConfigure> _logger;

    public NotificationEvaluationOptionsPlatformSettingsPostConfigure(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationEvaluationOptionsPlatformSettingsPostConfigure> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void PostConfigure(string? name, NotificationEvaluationOptions options) =>
        PlatformSettingsOptionsOverlay.ApplyBool(
            _scopeFactory,
            _logger,
            PlatformSettingKeys.NotificationsEvaluationEnabled,
            value => options.Enabled = value);
}

public sealed class NotificationInstanceOptionsPlatformSettingsPostConfigure : IPostConfigureOptions<NotificationInstanceOptions>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationInstanceOptionsPlatformSettingsPostConfigure> _logger;

    public NotificationInstanceOptionsPlatformSettingsPostConfigure(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationInstanceOptionsPlatformSettingsPostConfigure> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void PostConfigure(string? name, NotificationInstanceOptions options) =>
        PlatformSettingsOptionsOverlay.ApplyBool(
            _scopeFactory,
            _logger,
            PlatformSettingKeys.NotificationsInstancesEnabled,
            value => options.Enabled = value);
}

public sealed class NotificationDeliveryOptionsPlatformSettingsPostConfigure : IPostConfigureOptions<NotificationDeliveryOptions>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationDeliveryOptionsPlatformSettingsPostConfigure> _logger;

    public NotificationDeliveryOptionsPlatformSettingsPostConfigure(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationDeliveryOptionsPlatformSettingsPostConfigure> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void PostConfigure(string? name, NotificationDeliveryOptions options)
    {
        PlatformSettingsOptionsOverlay.ApplyBool(
            _scopeFactory,
            _logger,
            PlatformSettingKeys.NotificationsDeliveryEnabled,
            value => options.Enabled = value);
        PlatformSettingsOptionsOverlay.ApplyInt(
            _scopeFactory,
            _logger,
            PlatformSettingKeys.NotificationsDeliveryPollIntervalSeconds,
            value => options.PollIntervalSeconds = value);
        PlatformSettingsOptionsOverlay.ApplyInt(
            _scopeFactory,
            _logger,
            PlatformSettingKeys.NotificationsDeliveryBatchSize,
            value => options.BatchSize = value);
    }
}

public sealed class PlatformEventsOptionsPlatformSettingsPostConfigure : IPostConfigureOptions<PlatformEventsOptions>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlatformEventsOptionsPlatformSettingsPostConfigure> _logger;

    public PlatformEventsOptionsPlatformSettingsPostConfigure(
        IServiceScopeFactory scopeFactory,
        ILogger<PlatformEventsOptionsPlatformSettingsPostConfigure> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void PostConfigure(string? name, PlatformEventsOptions options) =>
        PlatformSettingsOptionsOverlay.ApplyBool(
            _scopeFactory,
            _logger,
            PlatformSettingKeys.PlatformEventsEnabled,
            value => options.Enabled = value);
}

public sealed class AttentionIncidentObservationOptionsPlatformSettingsPostConfigure : IPostConfigureOptions<AttentionIncidentObservationOptions>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AttentionIncidentObservationOptionsPlatformSettingsPostConfigure> _logger;

    public AttentionIncidentObservationOptionsPlatformSettingsPostConfigure(
        IServiceScopeFactory scopeFactory,
        ILogger<AttentionIncidentObservationOptionsPlatformSettingsPostConfigure> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void PostConfigure(string? name, AttentionIncidentObservationOptions options)
    {
        PlatformSettingsOptionsOverlay.ApplyBool(
            _scopeFactory,
            _logger,
            PlatformSettingKeys.AttentionIncidentsObservationEnabled,
            value => options.Enabled = value);
        PlatformSettingsOptionsOverlay.ApplyDuration(
            _scopeFactory,
            _logger,
            PlatformSettingKeys.AttentionIncidentsObservationInterval,
            value => options.Interval = value);
    }
}
