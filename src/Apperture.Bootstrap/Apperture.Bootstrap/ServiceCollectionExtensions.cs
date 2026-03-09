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
    /// </summary>
    public static IServiceCollection AddAppertureBootstrap(
        this IServiceCollection services,
        IConfiguration configuration,
        bool validateOnStartup = true)
    {
        services.Configure<BootstrapSqlConfig>(configuration.GetSection(BootstrapSqlConfig.SectionName));
        services.AddSingleton<IBootstrapConnectionProfile, BootstrapConnectionProfile>();

        if (validateOnStartup)
        {
            services.AddHostedService<BootstrapValidationHostedService>();
        }

        return services;
    }
}
