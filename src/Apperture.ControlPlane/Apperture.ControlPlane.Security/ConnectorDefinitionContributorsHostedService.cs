using Apperture.Core.Application.Services;
using Microsoft.Extensions.Hosting;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Runs after well-known connector seeds; applies <see cref="IConnectorDefinitionContributor"/> instances to the registry.
/// </summary>
public sealed class ConnectorDefinitionContributorsHostedService : IHostedService
{
    private readonly IConnectorDefinitionRegistry _registry;
    private readonly IEnumerable<IConnectorDefinitionContributor> _contributors;

    public ConnectorDefinitionContributorsHostedService(
        IConnectorDefinitionRegistry registry,
        IEnumerable<IConnectorDefinitionContributor> contributors)
    {
        _registry = registry;
        _contributors = contributors ?? Array.Empty<IConnectorDefinitionContributor>();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var c in _contributors)
            c.Contribute(_registry);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
