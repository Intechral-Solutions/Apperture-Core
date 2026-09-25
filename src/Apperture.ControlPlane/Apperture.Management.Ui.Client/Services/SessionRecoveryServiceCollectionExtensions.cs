using Microsoft.Extensions.DependencyInjection;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Registers session recovery for the WASM client only. The Ui.Server host registers no <see cref="HttpClient"/>
/// pipeline that reports to it (its registrations are never resolved: there is no prerender), so this lives
/// beside <c>Program.cs</c> rather than in the shared shell services.
/// </summary>
public static class SessionRecoveryServiceCollectionExtensions
{
    /// <param name="apiBaseUri">Absolute API base (trailing slash) shared with the application <see cref="HttpClient"/>.</param>
    public static IServiceCollection AddSessionRecovery(this IServiceCollection services, Uri apiBaseUri)
    {
        // Credentials-only client for the authoritative re-check: no recovery handler, so it cannot recurse.
        services.AddScoped<ISessionProbe>(_ => new HttpSessionProbe(
            new HttpClient(new IncludeCredentialsHandler { InnerHandler = new HttpClientHandler() }) { BaseAddress = apiBaseUri }));
        services.AddScoped<IRecoveryHistoryStore, SessionStorageRecoveryHistory>();
        services.AddScoped<SessionRecoveryCoordinator>();
        services.AddScoped<ISessionRecoveryCoordinator>(sp => sp.GetRequiredService<SessionRecoveryCoordinator>());
        services.AddScoped(sp => new AuthRecoveryHandler(() => sp.GetRequiredService<ISessionRecoveryCoordinator>()));
        return services;
    }
}
