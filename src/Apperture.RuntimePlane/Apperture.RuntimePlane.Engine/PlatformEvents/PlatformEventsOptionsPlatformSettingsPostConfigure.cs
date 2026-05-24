using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.PlatformSettings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.RuntimePlane.Engine.PlatformEvents;

public sealed class PlatformEventsOptionsPlatformSettingsPostConfigure : IPostConfigureOptions<PlatformEventsOptions>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlatformEventsOptionsPlatformSettingsPostConfigure> _logger;

    public PlatformEventsOptionsPlatformSettingsPostConfigure(
        IServiceScopeFactory scopeFactory,
        ILogger<PlatformEventsOptionsPlatformSettingsPostConfigure> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void PostConfigure(string? name, PlatformEventsOptions options)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var resolver = scope.ServiceProvider.GetService<IPlatformSettingsResolver>();
            if (resolver is null)
                return;

            var value = resolver.ResolveRawValueAsync(PlatformSettingKeys.PlatformEventsEnabled).GetAwaiter().GetResult();
            if (value is bool enabled)
                options.Enabled = enabled;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not apply platform settings overlay for PlatformEvents.Enabled; using configuration.");
        }
    }
}
