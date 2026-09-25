using Apperture.Bootstrap;
using Apperture.ControlPlane.Security;
using Apperture.Management.Host;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{LocalAuthOptions.SectionName}:Enabled"] = "true",
                [$"{LocalAuthOptions.SectionName}:MinPasswordLength"] = "8",
                [ManagementUiRedirectKey] = TrustedUiOrigin,
            });
        });
        builder.ConfigureServices(services =>
        {
            var toRemove = services.Where(d => d.ServiceType == typeof(IBootstrapConnectionProfile)).ToList();
            foreach (var d in toRemove)
                services.Remove(d);

            services.AddSingleton<IBootstrapConnectionProfile>(new FixedBootstrapConnectionProfile(_connectionString));

            services.AddAuthentication()
                .AddPolicyScheme(
                    ManagementTestAuthenticationPostConfigure.DefaultScheme,
                    "Test header or auth cookie",
                    options =>
                    {
                        options.ForwardDefaultSelector = context =>
                            context.Request.Headers.ContainsKey(ManagementTestAuthHandler.HeaderName)
                                ? ManagementTestAuthHandler.SchemeName
                                : Apperture.Management.Host.AuthenticationServiceCollectionExtensions.CookieScheme;
                    })
                .AddScheme<AuthenticationSchemeOptions, ManagementTestAuthHandler>(
                    ManagementTestAuthHandler.SchemeName,
                    _ => { });

            services.AddSingleton<IPostConfigureOptions<AuthenticationOptions>, ManagementTestAuthenticationPostConfigure>();
        });
    }

    /// <summary>
    /// The origin Rule U compares. Matches <see cref="ManagementUiRedirectKey"/> so existing tests satisfy the browser contract.
    /// Rejection tests remove these headers.
    /// </summary>
    public const string TrustedUiOrigin = "http://localhost:9001";

    public const string ManagementUiRedirectKey = "ShellUi:ManagementUiUrl";

    protected override void ConfigureClient(HttpClient client)
    {
        client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", TrustedUiOrigin);
        client.DefaultRequestHeaders.TryAddWithoutValidation(BrowserRequest.HeaderName, BrowserRequest.HeaderValue);
    }
}
