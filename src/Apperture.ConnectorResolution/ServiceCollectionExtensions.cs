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
    /// Adds connector resolution: slot resolver, connection resolver, factory registry,
    /// and hosted registration of <see cref="IConnectorClientFactoryContributor"/> instances.
    /// Caller must register IConnectionRepository, IBindingRepository, IConnectorDefinitionRegistry, ISecretResolver.
    /// </summary>
    public static IServiceCollection AddConnectorResolution(this IServiceCollection services)
    {
        services.AddSingleton<IConnectorClientFactoryRegistry, ConnectorClientFactoryRegistry>();
        services.AddScoped<IConnectionResolver, DefaultConnectionResolver>();
        services.AddScoped<IRuntimeSlotResolver, SlotBoundResolver>();
        services.AddHostedService<ConnectorClientFactoryRegistrationHostedService>();
        return services;
    }

    /// <summary>
    /// Registers the platform SQL connector client factory and its runtime registration contributor.
    /// If <see cref="IConnectorDefinitionRegistry"/> is not yet registered, uses <see cref="InMemoryConnectorDefinitionRegistry"/>
    /// with well-known platform definitions from <see cref="WellKnownConnectorDefinitions"/> (Core.Domain only).
    /// Idempotent if called multiple times.
    /// </summary>
    public static IServiceCollection AddPlatformSqlConnector(this IServiceCollection services)
    {
        if (services.Any(d =>
                d.ServiceType == typeof(IConnectorClientFactoryContributor) &&
                d.ImplementationType == typeof(PlatformSqlConnectorClientFactoryContributor)))
            return services;

        services.TryRegisterConnectorDefinitionRegistry();
        services.AddSingleton<IConnectorClientFactory<IDbConnection>, PlatformSqlConnectorClientFactory>();
        services.AddSingleton<IConnectorClientFactoryContributor, PlatformSqlConnectorClientFactoryContributor>();
        return services;
    }

    private static void TryRegisterConnectorDefinitionRegistry(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(IConnectorDefinitionRegistry)))
            return;
        services.AddSingleton<IConnectorDefinitionRegistry>(_ =>
        {
            var registry = new InMemoryConnectorDefinitionRegistry();
            registry.Register(WellKnownConnectorDefinitions.CreatePlatformSql());
            registry.Register(WellKnownConnectorDefinitions.CreatePlatformCampusNexusMinimal());
            registry.Register(WellKnownConnectorDefinitions.CreatePlatformSftp());
            return registry;
        });
    }
}
