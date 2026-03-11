using System.Data;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Apperture.ConnectorResolution;

/// <summary>
/// Registers connector resolution services for runtime slot-bound resolution.
/// Requires: AddApperturePersistence (DbContext), IConnectionRepository, IBindingRepository,
/// IConnectorDefinitionRegistry, and ISecretResolver to be registered by the host.
/// </summary>
public static class ConnectorResolutionServiceCollectionExtensions
{
    /// <summary>
    /// Adds connector resolution: slot resolver, connection resolver, and factory registry.
    /// Caller must register IConnectionRepository, IBindingRepository, IConnectorDefinitionRegistry, ISecretResolver.
    /// </summary>
    public static IServiceCollection AddConnectorResolution(this IServiceCollection services)
    {
        services.AddSingleton<IConnectorClientFactoryRegistry, ConnectorClientFactoryRegistry>();
        services.AddScoped<IConnectionResolver, DefaultConnectionResolver>();
        services.AddScoped<IRuntimeSlotResolver, SlotBoundResolver>();
        return services;
    }

    /// <summary>
    /// Registers the platform SQL connector definition and factory.
    /// If IConnectorDefinitionRegistry is not yet registered, registers InMemoryConnectorDefinitionRegistry and seeds the platform SQL definition.
    /// </summary>
    public static IServiceCollection AddPlatformSqlConnector(this IServiceCollection services)
    {
        services.TryRegisterConnectorDefinitionRegistry();
        services.AddSingleton<IConnectorClientFactory<IDbConnection>, PlatformSqlConnectorClientFactory>();
        services.AddHostedService<SeedPlatformSqlFactoryHostedService>();
        return services;
    }

    private static void TryRegisterConnectorDefinitionRegistry(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(IConnectorDefinitionRegistry)))
            return;
        services.AddSingleton<IConnectorDefinitionRegistry>(_ =>
        {
            var registry = new InMemoryConnectorDefinitionRegistry();
            registry.Register(new ConnectorDefinition(
                PlatformSqlConstants.ConnectorDefinitionId,
                "Platform SQL",
                null,
                new Dictionary<string, string>()));
            return registry;
        });
    }
}

internal sealed class SeedPlatformSqlFactoryHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public SeedPlatformSqlFactoryHostedService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var registry = _serviceProvider.GetRequiredService<IConnectorClientFactoryRegistry>();
        var factory = _serviceProvider.GetRequiredService<IConnectorClientFactory<IDbConnection>>();
        registry.Register(PlatformSqlConstants.ConnectorDefinitionId, factory);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
