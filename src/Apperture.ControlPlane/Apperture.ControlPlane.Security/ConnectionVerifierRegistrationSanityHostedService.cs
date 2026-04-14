using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// After connector definitions are registered, logs a warning if any definition declares
/// <see cref="ConnectorCapabilities.SupportsConnectionVerification"/> but no <see cref="IConnectionVerifier"/> was registered for that id.
/// </summary>
public sealed class ConnectionVerifierRegistrationSanityHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectorDefinitionRegistry _registry;
    private readonly ILogger<ConnectionVerifierRegistrationSanityHostedService> _logger;

    public ConnectionVerifierRegistrationSanityHostedService(
        IServiceScopeFactory scopeFactory,
        IConnectorDefinitionRegistry registry,
        ILogger<ConnectionVerifierRegistrationSanityHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var verifiers = scope.ServiceProvider.GetRequiredService<IEnumerable<IConnectionVerifier>>().ToList();
        var registeredIds = verifiers.Select(v => v.ConnectorDefinitionId.Value).ToHashSet(StringComparer.Ordinal);

        foreach (var def in _registry.GetAll())
        {
            if (!def.Capabilities.HasFlag(ConnectorCapabilities.SupportsConnectionVerification))
                continue;
            if (!registeredIds.Contains(def.Id.Value))
            {
                _logger.LogWarning(
                    "Connector '{ConnectorName}' ({ConnectorId}) declares SupportsConnectionVerification but no IConnectionVerifier is registered.",
                    def.Name,
                    def.Id.Value);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
