using Apperture.Bootstrap;
using Apperture.ControlPlane.Security;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Management.Host;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apperture.BrowserTests.Infrastructure;

/// <summary>
/// The real Management.Host composition (<c>Program.cs</c>, real cookie/OIDC/CORS/authorization wiring,
/// real SQL persistence) listening on a real Kestrel socket at <c>http://localhost:{api}</c>, so a browser
/// talks to it exactly as it does in the dev topology.
/// <para>
/// Everything this class adds is <b>test-only and lives in this assembly</b>: an observe-only request
/// recorder, a controllable clock on the application cookie handler, an ephemeral Data Protection key ring
/// (so the harness never touches the developer's key ring), a harness-owned <c>ISecretProtector</c>
/// (so SQL secrets can start on Linux CI without DPAPI), the stub-OIDC configuration, the harness SQL
/// database, and a test-only CORS policy for this run's UI origin (product CORS is Development-only).
/// None of it can be activated by a production composition.
/// </para>
/// </summary>
public sealed class LiveManagementHost : WebApplicationFactory<ManagementHostWebApplicationFactoryEntryPoint>
{
    private readonly HarnessOrigins _origins;
    private readonly string _connectionString;
    private readonly StubOidcSettings _oidc;
    private readonly BrowserHarnessSecretProtector _secretProtector = new();
    private IHost? _kestrelHost;
    private bool _buildingTestServerTwin;

    public LiveManagementHost(HarnessOrigins origins, string connectionString, StubOidcSettings oidc)
    {
        _origins = origins;
        _connectionString = connectionString;
        _oidc = oidc;
    }

    public RequestRecorder Recorder { get; } = new();

    public ControllableTimeProvider Clock { get; } = new();

    public HostLogBuffer Logs { get; } = new();

    /// <summary>The service provider of the host that is actually listening (not the un-started test-server twin).</summary>
    public IServiceProvider LiveServices =>
        _kestrelHost?.Services ?? throw new InvalidOperationException("The live host has not started.");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{LocalAuthOptions.SectionName}:Enabled"] = "true",
                [$"{LocalAuthOptions.SectionName}:MinPasswordLength"] = "8",
                // The UI origin the host builds post-sign-in redirects against.
                ["ShellUi:ManagementUiUrl"] = _origins.Ui,
                // Entra is "enabled" and pointed at the harness's stub provider on 127.0.0.1.
                [$"{EntraAuthOptions.SectionName}:Enabled"] = "true",
                [$"{EntraAuthOptions.SectionName}:Authority"] = _oidc.Authority,
                [$"{EntraAuthOptions.SectionName}:AzureTenantId"] = _oidc.TenantId,
                [$"{EntraAuthOptions.SectionName}:ClientId"] = _oidc.ClientId,
                [$"{EntraAuthOptions.SectionName}:ClientSecret"] = _oidc.ClientSecret,
                [$"{EntraAuthOptions.SectionName}:AuthenticationContextId"] = _oidc.AuthenticationContextId,
                // Keep the harness quiet and self-contained: no SMTP, no outbound notification delivery.
                ["Notifications:Email:Enabled"] = "false",
                ["Logging:LogLevel:Default"] = "Warning",
                ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning",
                ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Error",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Program.cs calls AddAppertureSqlSecrets() with the production default (DPAPI on Windows;
            // fail-closed when resolved on Linux). Replace it on both the TestServer twin and the
            // Kestrel host so neither composition selects DPAPI. Always applied — the suite must
            // not depend on Windows DPAPI.
            services.RemoveAll<ISecretProtector>();
            services.AddSingleton<ISecretProtector>(_secretProtector);

            if (_buildingTestServerTwin)
            {
                // WebApplicationFactory demands a TestServer host, and the deferred-host machinery runs the entry
                // point for it as well. That twin must not run any hosted service: two copies of the real host
                // start-up work (permission repair, migrations, workers) would race each other on one database.
                services.RemoveAll<IHostedService>();
                return;
            }

            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IBootstrapConnectionProfile)).ToList())
                services.Remove(descriptor);
            services.AddSingleton<IBootstrapConnectionProfile>(new HarnessBootstrapConnectionProfile(_connectionString));

            // Ephemeral key ring: never read or write the developer's persisted Data Protection keys.
            services.AddDataProtection()
                .SetApplicationName("Apperture.BrowserTests")
                .UseEphemeralDataProtectionProvider();

            services.AddSingleton(Recorder);
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(new RequestRecorderStartupFilter(Recorder));

            // Deterministic session expiry / sliding renewal, on the application session cookie only.
            services.PostConfigure<CookieAuthenticationOptions>(
                CookieAuthenticationDefaults.AuthenticationScheme,
                options => options.TimeProvider = Clock);

            // The stub provider is served over plain http on loopback. (Configure, not PostConfigure: the
            // framework's own post-configure validates the scheme of the authority and would throw first.)
            services.Configure<OpenIdConnectOptions>(
                OpenIdConnectDefaults.AuthenticationScheme,
                options => options.RequireHttpsMetadata = false);

            // Testing has no product CORS. The harness is still a split-host browser, so it registers
            // the same Development policy against this run's UI origin and applies it here only.
            services.AddCors(options =>
            {
                options.AddPolicy(UiDevCors.PolicyName, policy => UiDevCors.Configure(policy, _origins.Ui));
            });
            services.AddSingleton<IStartupFilter>(new HarnessCorsStartupFilter());
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // WebApplicationFactory insists on a TestServer host. Build that twin first (with no hosted services, see
        // ConfigureWebHost), then switch the builder to Kestrel for the one host that really listens.
        _buildingTestServerTwin = true;
        var testServerTwin = builder.Build();
        _buildingTestServerTwin = false;

        builder.ConfigureWebHost(web => web
            .UseKestrel()
            .UseUrls(_origins.Api));

        _kestrelHost = builder.Build();
        _kestrelHost.Start();
        return testServerTwin;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _kestrelHost is not null)
        {
            try
            {
                _kestrelHost.StopAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            }
            catch
            {
                // Teardown must never mask a test failure.
            }

            _kestrelHost.Dispose();
            _kestrelHost = null;
        }

        base.Dispose(disposing);
    }

    private sealed class HarnessCorsStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseCors(UiDevCors.PolicyName);
            next(app);
        };
    }

    private sealed class HarnessBootstrapConnectionProfile(string connectionString) : IBootstrapConnectionProfile
    {
        public string ConnectionString { get; } = connectionString;

        public async Task ValidateAsync(CancellationToken cancellationToken = default)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
