using Apperture.Bootstrap;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apperture.Cli.Bootstrap;

/// <summary>
/// Extension methods for registering bootstrap CLI services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers BootstrapCliService and Bootstrap scaffolding.
    /// Use IConfiguration for config-based bootstrap (e.g. appsettings) or pass empty configuration for scaffolding only.
    /// </summary>
    public static IServiceCollection AddBootstrapCli(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = Environments.Development });
        services.AddLogging();
        services.Configure<BootstrapSqlConfig>(configuration.GetSection(BootstrapSqlConfig.SectionName));
        services.AddSingleton<IBootstrapConnectionProfile, BootstrapConnectionProfile>();
        services.AddBootstrapScaffolding();
        services.AddSingleton<BootstrapCliService>();
        return services;
    }

    private sealed class HostingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Apperture.Cli";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    /// <summary>
    /// Registers BootstrapCliService with Bootstrap scaffolding only (no config binding).
    /// Uses empty configuration; IBootstrapConnectionProfile will use LocalDB fallback.
    /// </summary>
    public static IServiceCollection AddBootstrapCliScaffolding(this IServiceCollection services)
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        return services.AddBootstrapCli(config);
    }
}
