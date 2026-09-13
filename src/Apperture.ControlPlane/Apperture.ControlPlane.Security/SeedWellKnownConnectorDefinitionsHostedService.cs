using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Microsoft.Extensions.Hosting;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Ensures well-known platform connector definitions exist in <see cref="IConnectorDefinitionRegistry"/> (control plane stub registry).
/// Campus Nexus and SFTP definitions are contributed by connector packages via IConnectorDefinitionContributor.
/// </summary>
public sealed class SeedWellKnownConnectorDefinitionsHostedService : IHostedService
{
    private readonly IConnectorDefinitionRegistry _registry;

    public SeedWellKnownConnectorDefinitionsHostedService(IConnectorDefinitionRegistry registry) =>
        _registry = registry;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _registry.Register(WellKnownConnectorDefinitions.CreatePlatformSql());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
