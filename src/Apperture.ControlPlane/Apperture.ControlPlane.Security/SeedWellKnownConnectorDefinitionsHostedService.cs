using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Microsoft.Extensions.Hosting;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Ensures well-known platform connector definitions exist in <see cref="IConnectorDefinitionRegistry"/> (control plane stub registry).
/// Aligns with runtime worker platform connector registration ids (Apperture.ConnectorResolution).
/// </summary>
public sealed class SeedWellKnownConnectorDefinitionsHostedService : IHostedService
{
    private readonly IConnectorDefinitionRegistry _registry;

    public SeedWellKnownConnectorDefinitionsHostedService(IConnectorDefinitionRegistry registry) =>
        _registry = registry;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _registry.Register(new ConnectorDefinition(WellKnownConnectorDefinitionIds.PlatformSql, "Platform SQL", null, new Dictionary<string, string>()));
        _registry.Register(new ConnectorDefinition(WellKnownConnectorDefinitionIds.PlatformCampusNexus, "Campus Nexus API", null, new Dictionary<string, string>()));
        _registry.Register(new ConnectorDefinition(WellKnownConnectorDefinitionIds.PlatformSftp, "SFTP", null, new Dictionary<string, string>()));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
