using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Cutover;
using Apperture.Core.Application.Notifications.Inbox;
using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Persistence.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.ControlPlane.Security;

/// <summary>Epic 77: notification definitions, validation, render preview, and evaluate-only rule matching.</summary>
public static class NotificationsServiceCollectionExtensions
{
    public static IServiceCollection AddAppertureNotifications(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<NotificationEvaluationOptions>(configuration.GetSection(NotificationEvaluationOptions.SectionName));
            services.Configure<NotificationInstanceOptions>(configuration.GetSection(NotificationInstanceOptions.SectionName));
            services.Configure<NotificationDeliveryOptions>(configuration.GetSection(NotificationDeliveryOptions.SectionName));
            services.Configure<NotificationPipelineOptions>(configuration.GetSection(NotificationPipelineOptions.SectionName));
            services.Configure<EmailNotificationOptions>(configuration.GetSection(EmailNotificationOptions.SectionName));
        }

        services.AddScoped<INotificationLiveRenderContextResolver, NotificationLiveRenderContextResolver>();
        services.AddScoped<IEmailSmtpCredentialResolver, EmailSmtpCredentialResolver>();
        services.AddSingleton<INotificationTemplateVariableCatalog, NotificationTemplateVariableCatalog>();
        services.AddSingleton<IRedactionPolicy, NoOpRedactionPolicy>();
        services.AddSingleton<INotificationThrottleStore, InMemoryNotificationThrottleStore>();
        services.AddScoped<INotificationRuleValidator, NotificationRuleValidator>();
        services.AddScoped<INotificationTemplateValidator, NotificationTemplateValidator>();
        services.AddScoped<INotificationTemplateRenderer, NotificationTemplateRenderer>();
        services.AddSingleton<INotificationRenderContextFactory, NotificationRenderContextFactory>();
        services.AddScoped<INotificationRenderContextBuilder, NotificationRenderContextBuilder>();
        services.AddScoped<INotificationRuleEvaluator, NotificationRuleEvaluator>();
        services.AddScoped<INotificationEvaluateOnlyCoordinator, NotificationEvaluateOnlyCoordinator>();
        services.AddScoped<INotificationDefinitionStore, SqlNotificationDefinitionStore>();
        services.AddScoped<INotificationSubscriptionStore, SqlNotificationSubscriptionStore>();
        services.AddScoped<OperationalTeamRecipientExpander>();
        services.AddScoped<INotificationRecipientResolver, SqlNotificationRecipientResolver>();
        services.AddSingleton<NotificationRecipientAddressParser>();
        services.AddScoped<ManagedContactRecipientProvisioner>();
        services.AddScoped<NotificationDefinitionAuthorization>();
        services.AddScoped<INotificationDefinitionService, NotificationDefinitionService>();
        services.AddScoped<INotificationSubscriptionService, NotificationSubscriptionService>();
        services.AddScoped<INotificationInstanceStore, SqlNotificationInstanceStore>();
        services.AddScoped<INotificationDeliveryOutboxStore, SqlNotificationDeliveryOutboxStore>();
        services.AddScoped<INotificationInstanceCoordinator, NotificationInstanceCoordinator>();
        services.AddScoped<INotificationUserInboxStore, SqlNotificationUserInboxStore>();
        services.AddScoped<INotificationUserInboxService, NotificationUserInboxService>();
        services.AddScoped<INotificationInstanceQueryService, NotificationInstanceQueryService>();
        services.AddScoped<INotificationDeliveryDiagnosticsStore, SqlNotificationDeliveryDiagnosticsStore>();
        services.AddScoped<INotificationDeliveryDiagnosticsService, NotificationDeliveryDiagnosticsService>();
        services.AddScoped<IExpectedRunMissingCutoverSignOffStore, SqlExpectedRunMissingCutoverSignOffStore>();
        services.AddScoped<IExpectedRunMissingCutoverSignOffService, ExpectedRunMissingCutoverSignOffService>();
        services.AddScoped<IExpectedRunMissingCutoverValidationService, ExpectedRunMissingCutoverValidationService>();
        services.AddScoped<IEmailNotificationChannel, SmtpEmailNotificationChannel>();
        services.AddScoped<NotificationDispatchProcessor>();
        services.AddHostedService<NotificationDispatchHostedService>();
        services.AddHostedService<NotificationConfigurationSafetyHostedService>();

        services.AddScoped<SqlPlatformEventStore>();
        services.AddScoped<IPlatformEventStore, EvaluatingPlatformEventStore>();
        return services;
    }
}
