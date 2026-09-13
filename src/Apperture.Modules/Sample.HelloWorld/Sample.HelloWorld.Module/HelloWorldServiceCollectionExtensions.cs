using Microsoft.Extensions.DependencyInjection;

namespace Sample.HelloWorld;

public static class HelloWorldServiceCollectionExtensions
{
    /// <summary>
    /// Registers Sample.HelloWorld control-plane services. Set <paramref name="registerCapabilitySlotBootstrap"/> to
    /// <c>true</c> only where <see cref="Apperture.Core.Application.Services.IModuleSlotRegistry"/> is registered
    /// (Management host); runtime workers must pass <c>false</c>.
    /// </summary>
    public static IServiceCollection AddSampleHelloWorldIntegration(
        this IServiceCollection services,
        bool registerCapabilitySlotBootstrap = true)
    {
        if (registerCapabilitySlotBootstrap)
            services.AddHostedService<HelloWorldSlotBootstrapHostedService>();

        return services;
    }
}
