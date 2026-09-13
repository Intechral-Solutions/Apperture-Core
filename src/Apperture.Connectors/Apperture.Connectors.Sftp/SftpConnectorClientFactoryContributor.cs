using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.Connectors.Sftp;

internal sealed class SftpConnectorClientFactoryContributor : IConnectorClientFactoryContributor
{
    public void Register(IConnectorClientFactoryRegistry registry, IServiceProvider services)
    {
        var factory = services.GetRequiredService<IConnectorClientFactory<ISftpFileClient>>();
        registry.Register(WellKnownConnectorDefinitionIds.PlatformSftp, factory);
    }
}
