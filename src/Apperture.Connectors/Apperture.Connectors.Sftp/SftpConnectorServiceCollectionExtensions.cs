using Apperture.Core.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.Connectors.Sftp;

public static class SftpConnectorServiceCollectionExtensions
{
    /// <summary>
    /// Registers SFTP client factory, runtime factory registration, and connector definition contributor.
    /// </summary>
    public static IServiceCollection AddAppertureSftpConnector(this IServiceCollection services)
    {
        services.AddSingleton<IConnectorClientFactory<ISftpFileClient>, SftpConnectorClientFactory>();
        services.AddSingleton<IConnectorClientFactoryContributor, SftpConnectorClientFactoryContributor>();
        services.AddSingleton<IConnectorDefinitionContributor, SftpConnectorDefinitionContributor>();
        new SftpConnectionVerifierContributor().Register(services);
        return services;
    }
}
