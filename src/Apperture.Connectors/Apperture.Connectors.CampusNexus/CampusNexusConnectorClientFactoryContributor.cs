using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.Connectors.CampusNexus;

internal sealed class CampusNexusConnectorClientFactoryContributor : IConnectorClientFactoryContributor
{
    public void Register(IConnectorClientFactoryRegistry registry, IServiceProvider services)
    {
        var factory = services.GetRequiredService<IConnectorClientFactory<ICampusNexusApiClient>>();
        registry.Register(WellKnownConnectorDefinitionIds.PlatformCampusNexus, factory);
    }
}
