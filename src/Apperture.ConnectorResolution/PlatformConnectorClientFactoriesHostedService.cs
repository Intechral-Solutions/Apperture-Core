using System.Data;
using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.Sftp;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Apperture.ConnectorResolution;

/// <summary>
/// Registers platform connector client factories into <see cref="IConnectorClientFactoryRegistry"/> at host startup.
/// </summary>
internal sealed class PlatformConnectorClientFactoriesHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public PlatformConnectorClientFactoriesHostedService(IServiceProvider serviceProvider) =>
        _serviceProvider = serviceProvider;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var registry = _serviceProvider.GetRequiredService<IConnectorClientFactoryRegistry>();

        var sqlFactory = _serviceProvider.GetRequiredService<IConnectorClientFactory<IDbConnection>>();
        registry.Register(WellKnownConnectorDefinitionIds.PlatformSql, sqlFactory);

        var cnsFactory = _serviceProvider.GetRequiredService<IConnectorClientFactory<ICampusNexusApiClient>>();
        registry.Register(WellKnownConnectorDefinitionIds.PlatformCampusNexus, cnsFactory);

        var sftpFactory = _serviceProvider.GetRequiredService<IConnectorClientFactory<ISftpFileClient>>();
        registry.Register(WellKnownConnectorDefinitionIds.PlatformSftp, sftpFactory);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
