using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Apperture.Core.Application.PlatformSettings;

public static class SettingSecretServiceCollectionExtensions
{
    /// <summary>
    /// Registers the setting-secret boundary (<see cref="ISettingSecretResolver"/>). The host registers <c>ISecretResolver</c> and
    /// <c>IAuditEventWriter</c>; the integration repository and principal context are used when the host has them (the refusal row's
    /// related context and the management actor) and the boundary works without them.
    /// </summary>
    public static IServiceCollection AddSettingSecretResolution(this IServiceCollection services)
    {
        services.TryAddScoped<ISettingSecretResolver, SettingSecretResolver>();
        return services;
    }
}
