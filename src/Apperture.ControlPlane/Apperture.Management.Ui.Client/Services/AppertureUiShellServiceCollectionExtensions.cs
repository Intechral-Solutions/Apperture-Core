using Microsoft.Extensions.DependencyInjection;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Shared DI for shell UX. <see cref="ThemeService"/> must stay scoped: it depends on <see cref="Microsoft.JSInterop.IJSRuntime"/>,
/// which is scoped on the WASM server host (prerender + circuits).
/// </summary>
public static class AppertureUiShellServiceCollectionExtensions
{
    public static IServiceCollection AddAppertureShellServices(this IServiceCollection services)
    {
        services.AddScoped<ThemeService>();
        services.AddSingleton<IToastService, ToastService>();
        // AddScoped, not AddSingleton: this watcher depends on the AddScoped SessionStateService, and a
        // singleton here would be a captive dependency if a host ever created a real DI scope around it
        // (neither the WASM client nor the server host's prerendering path does that today -- see
        // SessionStateService's own doc comment -- but AddScoped costs nothing and removes the hazard).
        services.AddScoped<INotificationToastWatcher, NotificationToastWatcher>();
        services.AddScoped<SessionStateService>();
        services.AddScoped<AuthSignInOptionsService>();
        services.AddScoped<ILoginAutoRedirectMarker, SessionStorageLoginAutoRedirectMarker>();
        services.AddScoped<ShellBootstrapState>();
        services.AddScoped<ShellHostNavigation>();
        services.AddScoped<ShellOrientationService>();
        services.AddScoped<TopBarMenuCoordinator>();
        services.AddScoped<IOperatorDisplayTimeZone, OperatorDisplayTimeZoneService>();
        services.AddScoped<AccountAvatarService>();
        services.AddScoped<AppearancePreferenceCoordinator>();
        services.AddScoped<ResourceContextViewState>();
        return services;
    }
}
