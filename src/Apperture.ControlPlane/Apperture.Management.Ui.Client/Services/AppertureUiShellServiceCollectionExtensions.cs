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
        services.AddScoped<SessionStateService>();
        services.AddScoped<AuthSignInOptionsService>();
        services.AddScoped<ShellBootstrapState>();
        services.AddScoped<ShellHostNavigation>();
        services.AddScoped<ShellOrientationService>();
        services.AddScoped<IOperatorDisplayTimeZone, OperatorDisplayTimeZoneService>();
        services.AddScoped<AccountAvatarService>();
        services.AddScoped<AppearancePreferenceCoordinator>();
        return services;
    }
}
