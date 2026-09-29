using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apperture.Management.Host;

internal static class BrowserRequestTrustExtensions
{
    public static IServiceCollection AddBrowserRequestTrust(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var mode = ResolveMode(configuration[BrowserRequestTrustOptions.ModeKey]);
        var reportOnly = BrowserRequestTrustOptions.HonorReportOnly(mode, environment.IsDevelopment());
        var snapshot = new BrowserRequestTrustOptions
        {
            TrustedOrigins = TrustedOrigins(configuration),
            ReportOnlyActive = reportOnly,
            ReportOnlyConfiguredButIgnored = mode == BrowserRequestTrustMode.ReportOnly && !reportOnly,
        };
        services.AddSingleton(snapshot);
        services.AddSingleton<IStartupFilter, BrowserRequestTrustStartupFilter>();
        return services;
    }

    public static IApplicationBuilder UseBrowserRequestTrust(this IApplicationBuilder app) =>
        app.UseMiddleware<BrowserRequestTrustMiddleware>();

    /// <summary>
    /// The origin <see cref="ManagementUiRedirects"/> would redirect to. Read from the live configuration so a test host
    /// that sets <c>ShellUi:ManagementUiUrl</c> after the service snapshot is still the authority Rule U compares.
    /// </summary>
    internal static IReadOnlyList<BrowserOrigin> TrustedOrigins(IConfiguration configuration)
    {
        if (!BrowserOrigin.TryParse(ManagementUiRedirects.ResolveBaseUrl(configuration), out var origin))
            return [];

        return [origin];
    }

    internal static BrowserRequestTrustMode ResolveMode(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return BrowserRequestTrustMode.Enforce;

        return Enum.TryParse<BrowserRequestTrustMode>(configured.Trim(), ignoreCase: true, out var mode)
            ? mode
            : BrowserRequestTrustMode.Enforce;
    }

    private sealed class BrowserRequestTrustStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            var options = app.ApplicationServices.GetRequiredService<BrowserRequestTrustOptions>();
            var configuration = app.ApplicationServices.GetRequiredService<IConfiguration>();
            var trusted = TrustedOrigins(configuration);
            if (trusted.Count > 0)
                options = new BrowserRequestTrustOptions
                {
                    TrustedOrigins = trusted,
                    ReportOnlyActive = options.ReportOnlyActive,
                    ReportOnlyConfiguredButIgnored = options.ReportOnlyConfiguredButIgnored,
                };
            var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("Apperture.Management.Host.BrowserRequestTrust");
            if (options.TrustedOrigins.Count == 0)
            {
                logger.LogError(
                    "ShellUi:ManagementUiUrl is not a usable origin. Unsafe browser API requests will be rejected until it is an exact scheme, host and port.");
            }
            else
            {
                logger.LogInformation("Browser request trust will require Origin {TrustedOrigin}.", options.TrustedOrigins[0]);
            }

            if (options.ReportOnlyConfiguredButIgnored)
            {
                logger.LogWarning(
                    "BrowserRequestTrust:Mode ReportOnly is ignored outside Development. Unsafe browser API requests are enforced.");
            }

            var environment = app.ApplicationServices.GetRequiredService<IHostEnvironment>();
            ManagementUiRedirects.EnsurePublicOrigin(configuration, environment);

            // Do not resolve OpenIdConnectOptions here. The browser harness points the authority at an http stub and
            // sets RequireHttpsMetadata only in its own Configure callback; creating the options during host start
            // runs the framework's HTTPS check before that callback is visible on the test-server twin.
            next(app);
        };
    }
}
