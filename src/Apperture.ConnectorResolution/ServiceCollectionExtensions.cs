using System.Data;
using Apperture.Core.Application.ConnectionTools;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    /// <param name="services">The service collection.</param>
    /// <param name="slotResolutionPath">
    /// What slot resolution is in this host, as recorded on a refused secret: the Worker Host resolves slots to run steps
    /// (the default); the Management Host resolves them only for integration settings options.
    /// </param>
    public static IServiceCollection AddConnectorResolution(
        this IServiceCollection services,
        ConnectionResolutionPath slotResolutionPath = ConnectionResolutionPath.Run)
    {
        services.AddSingleton<IConnectorClientFactoryRegistry, ConnectorClientFactoryRegistry>();
        services.AddSingleton(new ConnectionSlotResolution(slotResolutionPath));
        services.AddConnectionMaterialResolver();
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
        services.AddConnectionMaterialResolver();
        services.AddSingleton<IConnectorClientFactory<IDbConnection>, PlatformSqlConnectorClientFactory>();
        services.AddSingleton<IConnectorClientFactoryContributor, PlatformSqlConnectorClientFactoryContributor>();
        services.AddScoped<IConnectionVerifier, PlatformSqlConnectionVerifier>();
        return services;
    }

    /// <summary>
    /// Registers the SQL structure browser, the one real connection tool provider. It is a management-plane operator tool, so
    /// only the Management Host composes it; runtime workers do not. Scoped, because it uses the scoped secret resolver; its
    /// constructor does no work, and it creates a remote client only inside an operator-invoked request. Idempotent.
    /// </summary>
    public static IServiceCollection AddPlatformSqlStructureBrowser(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(IConnectionToolProvider) && d.ImplementationType == typeof(PlatformSqlStructureBrowser)))
            return services;

        services.AddPlatformSqlConnector();
        services.AddSingleton<SqlStructureBrowserOptions>();
        services.AddScoped<IConnectionToolProvider, PlatformSqlStructureBrowser>();
        return services;
    }

    /// <summary>
    /// The one connection-material boundary. Scoped, because it uses the scoped secret resolver and audit writer. Registered once
    /// however many of the composition methods run.
    /// </summary>
    private static void AddConnectionMaterialResolver(this IServiceCollection services) =>
        services.TryAddScoped<IConnectionMaterialResolver, ConnectionMaterialResolver>();

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
