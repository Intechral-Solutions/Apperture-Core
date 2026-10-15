using Apperture.Core.Application.Execution;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformSettings;
using Apperture.ModuleSdk.Integration.Configuration;
using Apperture.RuntimePlane.Engine.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.RuntimePlane.Engine;

public static class IntegrationRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddAppertureIntegrationRuntimeAccessors(this IServiceCollection services)
    {
        services.AddSingleton<IIntegrationExecutionScopeAccessor, IntegrationExecutionScopeAccessor>();
        // The accessor reads a sensitive setting's secret only through the setting-secret boundary. The host registers
        // ISecretResolver and the audit event writer, which the boundary needs.
        services.AddSettingSecretResolution();
        services.AddScoped<IIntegrationSettingsAccessor, IntegrationSettingsAccessor>();
        services.AddScoped<IIntegrationConnectionAccessor, IntegrationConnectionAccessor>();
        return services;
    }

    public static IServiceCollection AddAppertureIntegrationRuntimeSettings(this IServiceCollection services)
    {
        services.AddSingleton<IntegrationSettingDefinitionRegistry>();
        services.AddSingleton<IIntegrationConfigurationRegistry, IntegrationConfigurationRegistry>();
        services.AddSingleton<IIntegrationSettingDefinitionRegistry>(sp =>
            new CompositeIntegrationSettingDefinitionRegistry(
                sp.GetRequiredService<IntegrationSettingDefinitionRegistry>(),
                sp.GetServices<IModuleIntegrationSettingDefinitionContributor>()));
        services.AddScoped<IIntegrationSettingsResolver, RuntimeIntegrationSettingsResolver>();
        return services;
    }
}
