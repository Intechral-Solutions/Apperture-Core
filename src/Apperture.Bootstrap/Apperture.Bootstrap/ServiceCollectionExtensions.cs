using Apperture.Bootstrap.Stubs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.Bootstrap;

/// <summary>
/// Extension methods for configuring bootstrap connection and credential management.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds bootstrap SQL configuration and connection profile. Validates connection on startup when validateOnStartup is true.
    /// Also registers scaffolding interfaces (IBootstrapConfigurationStore, IBootstrapCredentialResolver, etc.) with stub implementations.
    /// </summary>
    public static IServiceCollection AddAppertureBootstrap(
        this IServiceCollection services,
        IConfiguration configuration,
        bool validateOnStartup = true)
    {
        services.Configure<BootstrapSqlConfig>(configuration.GetSection(BootstrapSqlConfig.SectionName));
        services.AddSingleton<IBootstrapConnectionProfile, BootstrapConnectionProfile>();
        AddBootstrapScaffolding(services);

        if (validateOnStartup)
        {
            services.AddHostedService<BootstrapValidationHostedService>();
        }

        return services;
    }

    /// <summary>
    /// Registers bootstrap scaffolding interfaces with stub implementations. Additive; call from AddAppertureBootstrap or for advanced wiring.
    /// </summary>
    public static IServiceCollection AddBootstrapScaffolding(this IServiceCollection services)
    {
        services.AddSingleton<IBootstrapConfigurationStore, StubBootstrapConfigurationStore>();
        services.AddSingleton<IBootstrapCredentialResolver, StubBootstrapCredentialResolver>();
        services.AddSingleton<IBootstrapCredentialManager, StubBootstrapCredentialManager>();
        services.AddSingleton<ISqlConnectionBootstrapProfileManager, StubSqlConnectionBootstrapProfileManager>();
        services.AddSingleton<IBootstrapConfigurationValidator, StubBootstrapConfigurationValidator>();
        services.AddSingleton<IBootstrapRecoveryAuthorizer, StubBootstrapRecoveryAuthorizer>();
        services.AddSingleton<IBootstrapAuditWriter, StubBootstrapAuditWriter>();
        services.AddSingleton<IBootstrapEnvironmentStrategy, DefaultBootstrapEnvironmentStrategy>();
        return services;
    }
}
