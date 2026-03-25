using Apperture.Bootstrap;
using Apperture.Management.Host;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// Runs the real <see cref="Apperture.Management.Host"/> pipeline with test SQL bootstrap and test authentication.
/// </summary>
public sealed class ManagementHostWebApplicationFactory : WebApplicationFactory<ManagementHostWebApplicationFactoryEntryPoint>
{
    private readonly string _connectionString;

    public ManagementHostWebApplicationFactory(string connectionString) =>
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            var toRemove = services.Where(d => d.ServiceType == typeof(IBootstrapConnectionProfile)).ToList();
            foreach (var d in toRemove)
                services.Remove(d);

            services.AddSingleton<IBootstrapConnectionProfile>(new FixedBootstrapConnectionProfile(_connectionString));

            services.AddSingleton<IPostConfigureOptions<AuthenticationOptions>, ManagementTestAuthenticationPostConfigure>();
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, ManagementTestAuthHandler>(
                    ManagementTestAuthHandler.SchemeName,
                    _ => { });
        });
    }
}
