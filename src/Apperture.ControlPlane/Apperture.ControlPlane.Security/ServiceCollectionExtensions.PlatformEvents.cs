using Apperture.Core.Application.PlatformEvents;
using Apperture.Persistence.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.ControlPlane.Security;

/// <summary>Epic 77: platform event store and emitter (Management Host / control plane).</summary>
public static class PlatformEventsServiceCollectionExtensions
{
    public static IServiceCollection AddApperturePlatformEvents(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PlatformEventsOptions>(configuration.GetSection(PlatformEventsOptions.SectionName));
        services.AddSingleton<IPlatformEventCatalog, PlatformEventCatalog>();
        services.AddScoped<IPlatformEventStore, SqlPlatformEventStore>();
        services.AddScoped<IPlatformEventEmitter, PlatformEventEmitter>();
        services.AddScoped<ScheduleExpectationPlatformEventDualEmitter>();
        return services;
    }
}
