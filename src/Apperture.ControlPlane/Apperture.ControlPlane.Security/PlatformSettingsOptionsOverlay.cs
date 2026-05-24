using Apperture.Core.Application.PlatformSettings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

internal static class PlatformSettingsOptionsOverlay
{
    public static void ApplyBool(
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        string key,
        Action<bool> apply)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var resolver = scope.ServiceProvider.GetService<IPlatformSettingsResolver>();
            if (resolver is null)
                return;

            var value = resolver.ResolveRawValueAsync(key).GetAwaiter().GetResult();
            if (value is bool b)
                apply(b);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not apply platform setting overlay for {Key}; using configuration default.", key);
        }
    }

    public static void ApplyInt(
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        string key,
        Action<int> apply)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var resolver = scope.ServiceProvider.GetService<IPlatformSettingsResolver>();
            if (resolver is null)
                return;

            var value = resolver.ResolveRawValueAsync(key).GetAwaiter().GetResult();
            if (value is int i)
                apply(i);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not apply platform setting overlay for {Key}; using configuration default.", key);
        }
    }

    public static void ApplyDuration(
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        string key,
        Action<TimeSpan> apply)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var resolver = scope.ServiceProvider.GetService<IPlatformSettingsResolver>();
            if (resolver is null)
                return;

            var value = resolver.ResolveRawValueAsync(key).GetAwaiter().GetResult();
            if (value is TimeSpan ts)
                apply(ts);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not apply platform setting overlay for {Key}; using configuration default.", key);
        }
    }
}
