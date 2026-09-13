using Apperture.Core.Application.PlatformEvents;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Apperture.ControlPlane.Security;

/// <summary>Epic 77: platform event store and emitter (Management Host / control plane).</summary>
public static class PlatformEventsServiceCollectionExtensions
{
    /// <summary>
    /// Registers SQL-backed platform events and a merged catalog (<see cref="IPlatformEventCatalog"/>): static platform-owned types plus
    /// module-declared signals built from the registered <see cref="IModule"/> enumeration.
    /// Call after registering known modules (e.g. <c>AddKnownModules</c>) so the module list is populated.
    /// </summary>
    public static IServiceCollection AddApperturePlatformEvents(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PlatformEventsOptions>(configuration.GetSection(PlatformEventsOptions.SectionName));

        services.AddSingleton<IModuleSignalRegistry>(sp =>
        {
            var modules = sp.GetRequiredService<IEnumerable<IModule>>().ToList();
            ModuleSignalManifestValidator.ThrowIfInvalid(modules);
            return ModuleSignalRegistry.FromModules(modules);
        });

        services.AddSingleton<IPlatformEventCatalog>(sp =>
            new CompositePlatformEventCatalog(sp.GetRequiredService<IModuleSignalRegistry>()));

        services.AddHostedService<PlatformEventCatalogWarmupHostedService>();

        services.AddScoped<IPlatformEventStore, SqlPlatformEventStore>();
        services.AddScoped<IPlatformEventEmitter, PlatformEventEmitter>();
        services.AddScoped<IPlatformEventAuthoringCatalogService, PlatformEventAuthoringCatalogService>();
        services.AddScoped<ScheduleExpectationPlatformEventEmitter>();
        return services;
    }
}
