using Apperture.Core.Application.PlatformSettings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Merges production platform settings with demo gallery definitions when Development or
/// <c>PlatformSettings:DemoGallery:Enabled</c> is true (Management host only).
/// </summary>
public sealed class GatedPlatformSettingDefinitionRegistry : IPlatformSettingDefinitionRegistry
{
    private readonly PlatformSettingDefinitionRegistry _production = new();
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IConfiguration _configuration;

    public GatedPlatformSettingDefinitionRegistry(
        IHostEnvironment hostEnvironment,
        IConfiguration configuration)
    {
        _hostEnvironment = hostEnvironment;
        _configuration = configuration;
    }

    public IReadOnlyList<PlatformSettingDefinition> GetAll()
    {
        var production = _production.GetAll();
        if (!IsDemoGalleryEnabled())
            return production;

        return production
            .Concat(PlatformSettingDemoDefinitionRegistry.GetAll())
            .ToList();
    }

    public PlatformSettingDefinition? TryGet(string key)
    {
        var production = _production.TryGet(key);
        if (production is not null)
            return production;

        if (!IsDemoGalleryEnabled())
            return null;

        return PlatformSettingDemoDefinitionRegistry.TryGet(key);
    }

    private bool IsDemoGalleryEnabled() =>
        _hostEnvironment.IsDevelopment()
        || _configuration.GetValue("PlatformSettings:DemoGallery:Enabled", false);
}
