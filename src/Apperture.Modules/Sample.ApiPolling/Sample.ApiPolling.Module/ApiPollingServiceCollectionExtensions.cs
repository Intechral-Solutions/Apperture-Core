using Microsoft.Extensions.DependencyInjection;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling;

public static class ApiPollingServiceCollectionExtensions
{
    /// <summary>
    /// Registers embedded <see cref="ISampleDirectoryApiClient"/>, <see cref="IApiPollingStateStore"/>,
    /// and <see cref="ApiPollingModule"/>.
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
        return services;
    }
}
