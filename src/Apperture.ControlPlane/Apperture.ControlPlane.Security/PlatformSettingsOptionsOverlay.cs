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

    /// <summary>
    /// As <see cref="ApplyString"/>, for a secret reference, and it says where the value came from: <paramref name="apply"/> is told
    /// whether the reference is the one stored in the setting's row (the only source the ownership rule applies to) or one deployment
    /// configuration supplied.
    /// </summary>
    public static void ApplySecretReference(
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        string key,
        Action<string, bool> apply)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var resolver = scope.ServiceProvider.GetService<IPlatformSettingsResolver>();
            if (resolver is null)
                return;

            var resolved = resolver.GetResolvedAsync(key).GetAwaiter().GetResult();
            if (resolved?.CurrentValue is string s && !string.IsNullOrWhiteSpace(s))
                apply(s, resolved.Source == PlatformSettingValueSource.Database);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not apply platform setting overlay for {Key}; using configuration default.", key);
        }
    }

    public static void ApplyString(
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        string key,
        Action<string> apply)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var resolver = scope.ServiceProvider.GetService<IPlatformSettingsResolver>();
            if (resolver is null)
                return;

            var value = resolver.ResolveRawValueAsync(key).GetAwaiter().GetResult();
            if (value is string s && !string.IsNullOrWhiteSpace(s))
                apply(s);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not apply platform setting overlay for {Key}; using configuration default.", key);
        }
    }
}
