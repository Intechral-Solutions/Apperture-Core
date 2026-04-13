using System.Data;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.ConnectorResolution;

internal sealed class PlatformSqlConnectorClientFactoryContributor : IConnectorClientFactoryContributor
{
    public void Register(IConnectorClientFactoryRegistry registry, IServiceProvider services)
    {
        var factory = services.GetRequiredService<IConnectorClientFactory<IDbConnection>>();
        registry.Register(WellKnownConnectorDefinitionIds.PlatformSql, factory);
    }
}
