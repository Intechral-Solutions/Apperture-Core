using Apperture.Core.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.Connectors.CampusNexus;

public static class CampusNexusConnectorServiceCollectionExtensions
{
    /// <summary>
    /// Registers Campus Nexus API client factory, runtime factory registration, and connector definition contributor.
    /// </summary>
    public static IServiceCollection AddAppertureCampusNexusConnector(this IServiceCollection services)
    {
        services.AddSingleton<CampusNexusApiConnectorClientFactory>();
        services.AddSingleton<IConnectorClientFactory<ICampusNexusApiClient>>(sp => sp.GetRequiredService<CampusNexusApiConnectorClientFactory>());
        services.AddSingleton<IConnectorClientFactoryContributor, CampusNexusConnectorClientFactoryContributor>();
        services.AddSingleton<IConnectorDefinitionContributor, CampusNexusConnectorDefinitionContributor>();
        services.AddScoped<IConnectionVerifier, CampusNexusConnectionVerifier>();
        return services;
    }
}
