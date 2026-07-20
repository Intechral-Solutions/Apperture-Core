using Microsoft.Extensions.DependencyInjection;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling;

public static class ApiPollingServiceCollectionExtensions
{
    /// <summary>
    /// Registers embedded <see cref="ISampleDirectoryApiClient"/>, <see cref="IApiPollingStateStore"/>,
    /// <see cref="ApiPollingModule"/>, and ApiPolling retry handlers.
    /// When <see cref="IApiPollingDurableLastSeenStore"/> is registered (via
    /// <c>AddSampleApiPollingPersistence</c>), uses <see cref="HybridApiPollingStateStore"/>;
    /// otherwise uses process-local <see cref="InMemoryApiPollingStateStore"/>.
    /// </summary>
    public static IServiceCollection AddSampleApiPolling(this IServiceCollection services)
    {
        services.AddSingleton<ISampleDirectoryApiClient, EmbeddedSampleDirectoryApiClient>();
        services.AddSingleton<IApiPollingStateStore>(sp =>
        {
            var durable = sp.GetService<IApiPollingDurableLastSeenStore>();
            if (durable is not null)
                return new HybridApiPollingStateStore(durable);
            return new InMemoryApiPollingStateStore();
        });
        services.AddSingleton(sp => new ApiPollingModule(
            sp.GetRequiredService<IApiPollingStateStore>(),
            sp.GetRequiredService<ISampleDirectoryApiClient>()));
        services.AddSampleApiPollingRetryHandlers();
        return services;
    }

    /// <summary>
    /// Registers ApiPolling <see cref="Apperture.Core.Application.OperationItems.IOperationItemRetryHandler"/> (SDK-5C).
    /// Safe to call when the module instance is registered separately via AddKnownModules.
    /// </summary>
    public static IServiceCollection AddSampleApiPollingRetryHandlers(this IServiceCollection services)
    {
        services.AddSingleton<Apperture.Core.Application.OperationItems.IOperationItemRetryHandler>(sp =>
            new ApiPollingOperationItemRetryHandler(
                sp.GetService<ISampleDirectoryApiClient>() ?? new EmbeddedSampleDirectoryApiClient()));
        return services;
    }
}
