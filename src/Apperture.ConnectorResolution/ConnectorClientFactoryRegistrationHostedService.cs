using Apperture.Core.Application.Services;
using Microsoft.Extensions.Hosting;

namespace Apperture.ConnectorResolution;

/// <summary>
/// Applies all <see cref="IConnectorClientFactoryContributor"/> registrations once at startup.
/// </summary>
internal sealed class ConnectorClientFactoryRegistrationHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnectorClientFactoryRegistry _registry;
    private readonly IEnumerable<IConnectorClientFactoryContributor> _contributors;

    public ConnectorClientFactoryRegistrationHostedService(
        IServiceProvider serviceProvider,
        IConnectorClientFactoryRegistry registry,
        IEnumerable<IConnectorClientFactoryContributor> contributors)
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
        _contributors = contributors ?? Array.Empty<IConnectorClientFactoryContributor>();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var c in _contributors)
            c.Register(_registry, _serviceProvider);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
