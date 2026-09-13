using Apperture.Core.Application.PlatformEvents;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Abstractions.Signals;
using Apperture.Persistence.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Apperture.RuntimePlane.Engine.PlatformEvents;

/// <summary>
/// Registers Phase 77 module signal catalog/store/emitter for Worker Host (requires prior <see cref="IModule"/> enumeration registration).
/// </summary>
public static class PlatformEventsRuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Validates module manifests (signals) eagerly when the singleton registry is resolved; configures composite catalog + SQL persistence hooks.
    /// </summary>
    public static IServiceCollection AddRuntimePlatformEvents(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PlatformEventsOptions>(configuration.GetSection(PlatformEventsOptions.SectionName));
        services.AddSingleton<IPostConfigureOptions<PlatformEventsOptions>, PlatformEventsOptionsPlatformSettingsPostConfigure>();

        services.AddSingleton<IModuleSignalRegistry>(sp =>
        {
            var modules = sp.GetRequiredService<IEnumerable<IModule>>().ToList();
            return ModuleSignalRegistry.FromModules(modules);
        });

        services.AddSingleton<IPlatformEventCatalog>(sp =>
            new CompositePlatformEventCatalog(sp.GetRequiredService<IModuleSignalRegistry>()));

        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IPlatformEventStore, SqlPlatformEventStore>();
        services.AddScoped<IPlatformEventEmitter, PlatformEventEmitter>();
        services.AddScoped<ExecutionLifecyclePlatformEventEmitter>();
        services.AddScoped<IModulePlatformEventEmitter, ModulePlatformEventEmitter>();
        services.AddSingleton<IModuleTelemetry>(NoOpModuleTelemetry.Instance);
        services.AddScoped<IModuleOperationalLogger, ModuleOperationalLogger>();

        return services;
    }
}
