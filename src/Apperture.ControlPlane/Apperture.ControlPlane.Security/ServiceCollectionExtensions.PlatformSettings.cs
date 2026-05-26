using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

public static class PlatformSettingsServiceCollectionExtensions
{
    public static IServiceCollection AddApperturePlatformSettingsCore(this IServiceCollection services)
    {
        services.AddSingleton<IPlatformSettingDefinitionRegistry, PlatformSettingDefinitionRegistry>();
        services.AddScoped<IPlatformSettingValueRepository, SqlPlatformSettingValueRepository>();
        services.AddScoped<IPlatformSettingsResolver, PlatformSettingsResolver>();
        services.AddScoped<IPlatformSettingOptionProviderRegistry, PlatformSettingOptionProviderRegistry>();
        services.AddScoped<IPlatformSettingOptionProvider, PlatformConnectorDefinitionsOptionProvider>();
        services.AddScoped<PlatformSettingOptionProviderAuthorization>();
        services.AddScoped<PlatformSettingOptionProviderCoordinator>();
        return services;
    }

    public static IServiceCollection AddApperturePlatformSettingsManagementHost(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddApperturePlatformSettingsCore();
        services.AddScoped<IPlatformSettingsService, PlatformSettingsService>();

        services.AddSingleton<IPostConfigureOptions<NotificationEvaluationOptions>, NotificationEvaluationOptionsPlatformSettingsPostConfigure>();
        services.AddSingleton<IPostConfigureOptions<NotificationInstanceOptions>, NotificationInstanceOptionsPlatformSettingsPostConfigure>();
        services.AddSingleton<IPostConfigureOptions<NotificationDeliveryOptions>, NotificationDeliveryOptionsPlatformSettingsPostConfigure>();
        services.AddSingleton<IPostConfigureOptions<NotificationPipelineOptions>, NotificationPipelineOptionsPlatformSettingsPostConfigure>();
        services.AddSingleton<IPostConfigureOptions<PlatformEventsOptions>, PlatformEventsOptionsPlatformSettingsPostConfigure>();
        services.AddSingleton<IPostConfigureOptions<AttentionIncidentObservationOptions>, AttentionIncidentObservationOptionsPlatformSettingsPostConfigure>();

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
