using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

public static class PlatformSettingsServiceCollectionExtensions
{
    public static IServiceCollection AddApperturePlatformSettingsCore(this IServiceCollection services)
    {
        // Production registry; Management host replaces with GatedPlatformSettingDefinitionRegistry.
        services.TryAddSingleton<IPlatformSettingDefinitionRegistry, PlatformSettingDefinitionRegistry>();
        services.AddScoped<IPlatformSettingValueRepository, SqlPlatformSettingValueRepository>();
        services.AddScoped<IPlatformSettingsResolver, PlatformSettingsResolver>();
        services.AddScoped<IPlatformSettingOptionProviderRegistry, PlatformSettingOptionProviderRegistry>();
        services.AddScoped<IPlatformSettingOptionProvider, PlatformConnectorDefinitionsOptionProvider>();
        services.AddScoped<IPlatformSettingOptionProvider, CampusNexusCampusesOptionProvider>();
        services.AddScoped<PlatformSettingOptionProviderAuthorization>();
        services.AddScoped<IntegrationSettingOptionProviderAuthorization>();
        services.AddScoped<PlatformSettingOptionProviderCoordinator>();
        return services;
    }

    public static IServiceCollection AddApperturePlatformSettingsManagementHost(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddApperturePlatformSettingsCore();
        services.AddSingleton<IPlatformSettingDefinitionRegistry, GatedPlatformSettingDefinitionRegistry>();
        services.AddSingleton<IntegrationSettingDefinitionRegistry>();
        services.AddSingleton<IIntegrationConfigurationRegistry, IntegrationConfigurationRegistry>();
        services.AddSingleton<IIntegrationSettingDefinitionRegistry>(sp =>
            new CompositeIntegrationSettingDefinitionRegistry(
                sp.GetRequiredService<IntegrationSettingDefinitionRegistry>(),
                sp.GetServices<IModuleIntegrationSettingDefinitionContributor>()));
        services.AddScoped<IIntegrationSettingsResolver, IntegrationSettingsResolver>();
        services.AddScoped<IntegrationSettingsService>();
        services.AddScoped<IIntegrationSettingsService>(sp => sp.GetRequiredService<IntegrationSettingsService>());
        // Internal and registered only here: the in-transaction reset. The integration delete does not use it (owner ruling H1).
        services.AddScoped<IIntegrationSettingResetInTransaction>(sp => sp.GetRequiredService<IntegrationSettingsService>());
        services.AddScoped<IntegrationSettingsAuthorization>();
        services.AddScoped<PlatformSettingSecretMaterialProvisioner>();
        services.AddScoped<IPlatformSettingsService, PlatformSettingsService>();

        // Integration delete's settings inventory, secret classifier and transactional retirement (slice 3, work package 4a).
        // Scoped, so each takes the request's one context, the one the audited unit and the secret store use.
        services.AddScoped<IntegrationSettingSecretInventory>();
        services.AddScoped<IntegrationSettingSecretClassifier>();
        services.AddScoped<IntegrationSettingSecretRetirement>();

        services.AddSingleton<IPostConfigureOptions<NotificationEvaluationOptions>, NotificationEvaluationOptionsPlatformSettingsPostConfigure>();
        services.AddSingleton<IPostConfigureOptions<NotificationInstanceOptions>, NotificationInstanceOptionsPlatformSettingsPostConfigure>();
        services.AddSingleton<IPostConfigureOptions<NotificationDeliveryOptions>, NotificationDeliveryOptionsPlatformSettingsPostConfigure>();
        services.AddSingleton<IPostConfigureOptions<PlatformEventsOptions>, PlatformEventsOptionsPlatformSettingsPostConfigure>();
        services.AddSingleton<IPostConfigureOptions<AttentionIncidentObservationOptions>, AttentionIncidentObservationOptionsPlatformSettingsPostConfigure>();
        services.AddSingleton<IPostConfigureOptions<EmailNotificationOptions>, EmailNotificationOptionsPlatformSettingsPostConfigure>();

        _ = configuration;
        return services;
    }

    public static IServiceCollection AddApperturePlatformSettingsWorkerHost(this IServiceCollection services)
    {
        services.AddApperturePlatformSettingsCore();
        services.AddSingleton<IPostConfigureOptions<PlatformEventsOptions>, PlatformEventsOptionsPlatformSettingsPostConfigure>();
        return services;
    }
}
