using Apperture.Core.Application.PlatformSettings;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Integration.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.Core.Application.Integration;

/// <summary>DI helpers for SDK-6C integration configuration contributors.</summary>
public static class IntegrationConfigurationServiceCollectionExtensions
{
    /// <summary>
    /// Registers SDK-6C configuration bindings for a module resolved from DI (WorkerHost pattern).
    /// </summary>
    public static IServiceCollection AddIntegrationConfigurationBinding<TModule>(this IServiceCollection services)
        where TModule : class, IModule, IIntegrationConfigurationContributor
    {
        services.AddSingleton<ModuleIntegrationConfigurationBinding>(sp =>
            new ModuleIntegrationConfigurationBinding(
                sp.GetRequiredService<TModule>().GetManifest().Id,
                sp.GetRequiredService<TModule>()));
        services.AddSingleton<IModuleIntegrationSettingDefinitionContributor>(sp =>
            new IntegrationConfigurationSettingContributorAdapter(
                sp.GetRequiredService<TModule>().GetManifest().Id,
                sp.GetRequiredService<TModule>()));
        return services;
    }

    /// <summary>Registers configuration bindings for a known module instance (Management host pattern).</summary>
    public static void RegisterModuleIntegrationConfiguration(IServiceCollection services, IModule module)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(module);

        if (module is IIntegrationConfigurationContributor configContributor)
        {
            var moduleId = module.GetManifest().Id;
            services.AddSingleton(new ModuleIntegrationConfigurationBinding(moduleId, configContributor));
            services.AddSingleton<IModuleIntegrationSettingDefinitionContributor>(
                new IntegrationConfigurationSettingContributorAdapter(moduleId, configContributor));
        }
        else if (module is IModuleIntegrationSettingDefinitionContributor contributor)
        {
            services.AddSingleton(contributor);
        }
    }
}
