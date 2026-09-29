using Apperture.Bootstrap;
using Apperture.ControlPlane.Security;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Management.Host;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// Runs the real <see cref="Apperture.Management.Host"/> pipeline with test SQL bootstrap and test authentication.
/// Replaces <see cref="ISecretProtector"/> so startup validation can succeed on Linux, where the production
/// default is fail-closed DPAPI. The replacement lives in this assembly only.
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
            // Program.cs calls AddAppertureSqlSecrets() with the production default (DPAPI on Windows;
            // resolving it throws on Linux). SecretProtectorStartupValidationHostedService then probes the
            // final registration. Replace it here so this test host can start on Ubuntu CI. There is no
            // production, Testing, or plaintext fallback.
            services.RemoveAll<ISecretProtector>();
            services.AddSingleton<ISecretProtector>(new IntegrationTestSecretProtector());

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

            // Permission registration reads enabled modules once, at this service's slot. The fixture
            // inserts the HelloWorld install row only after the first request, which is too late for a
            // fresh catalog. Keep this service in the original registration index so it still runs after
            // migrations and before platform-admin role repair.
            var permissionRegistration = services
                .Select((descriptor, index) => (descriptor, index))
                .Where(x => x.descriptor.ServiceType == typeof(IHostedService)
                            && x.descriptor.ImplementationType == typeof(ModulePermissionRegistrationHostedService))
                .ToList();
            for (var i = permissionRegistration.Count - 1; i >= 0; i--)
            {
                var (descriptor, index) = permissionRegistration[i];
                services.Remove(descriptor);
                services.Insert(index, ServiceDescriptor.Singleton<IHostedService, GovernanceHarnessModuleEnablementHostedService>());
            }
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
