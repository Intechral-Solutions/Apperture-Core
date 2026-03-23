using System.Data;
using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.Sftp;
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
    /// Registers platform connector definitions (SQL, Campus Nexus API, SFTP) and their client factories for runtime resolution.
    /// If <see cref="IConnectorDefinitionRegistry"/> is not yet registered, uses <see cref="InMemoryConnectorDefinitionRegistry"/>.
    /// Idempotent if called multiple times.
    /// </summary>
    public static IServiceCollection AddPlatformSqlConnector(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(PlatformConnectorClientFactoriesHostedService)))
            return services;

        services.TryRegisterConnectorDefinitionRegistry();
        services.AddSingleton<IConnectorClientFactory<IDbConnection>, PlatformSqlConnectorClientFactory>();
        services.AddSingleton<IConnectorClientFactory<ICampusNexusApiClient>, CampusNexusApiConnectorClientFactory>();
        services.AddSingleton<IConnectorClientFactory<ISftpFileClient>, SftpConnectorClientFactory>();
        services.AddHostedService<PlatformConnectorClientFactoriesHostedService>();
        return services;
    }

    /// <inheritdoc cref="AddPlatformSqlConnector(IServiceCollection)"/>
    public static IServiceCollection AddPlatformCampusNexusApiConnector(this IServiceCollection services) =>
        AddPlatformSqlConnector(services);

    /// <inheritdoc cref="AddPlatformSqlConnector(IServiceCollection)"/>
    public static IServiceCollection AddPlatformSftpConnector(this IServiceCollection services) =>
        AddPlatformSqlConnector(services);

    private static void TryRegisterConnectorDefinitionRegistry(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(IConnectorDefinitionRegistry)))
            return;
        services.AddSingleton<IConnectorDefinitionRegistry>(_ =>
        {
            var registry = new InMemoryConnectorDefinitionRegistry();
            registry.Register(new ConnectorDefinition(
                WellKnownConnectorDefinitionIds.PlatformSql,
                "Platform SQL",
                null,
                new Dictionary<string, string>()));
            registry.Register(new ConnectorDefinition(
                WellKnownConnectorDefinitionIds.PlatformCampusNexus,
                "Campus Nexus API",
                null,
                new Dictionary<string, string>()));
            registry.Register(new ConnectorDefinition(
                WellKnownConnectorDefinitionIds.PlatformSftp,
                "SFTP",
                null,
                new Dictionary<string, string>()));
            return registry;
        });
    }
}
