using Apperture.ModuleSdk.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Apperture.Core.Application.Services;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Provides manifests from a set of DI-registered IModule instances.
/// </summary>
public sealed class KnownModuleManifestProvider : IModuleManifestProvider
{
    private readonly IReadOnlyList<IModule> _modules;
    private readonly IServiceScopeFactory? _scopeFactory;

    public KnownModuleManifestProvider(IEnumerable<IModule> modules, IServiceScopeFactory? scopeFactory = null)
    {
        _modules = modules?.ToList() ?? [];
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public IReadOnlyList<(string ModuleId, ModuleManifest Manifest)> GetManifests()
    {
        HashSet<string>? enabled = null;
        if (_scopeFactory is not null)
        {
            using var scope = _scopeFactory.CreateScope();
            var registry = scope.ServiceProvider.GetService<IModuleRegistryService>();
            if (registry is not null)
            {
                var descriptors = registry.GetEnabledDescriptorsAsync().AsTask().GetAwaiter().GetResult();
                enabled = descriptors.Select(x => x.ModuleId).ToHashSet(StringComparer.Ordinal);
            }
        }

        var list = new List<(string ModuleId, ModuleManifest Manifest)>(_modules.Count);
        foreach (var module in _modules)
        {
            var manifest = module.GetManifest();
            if (enabled is not null && !enabled.Contains(manifest.Id))
                continue;
            list.Add((manifest.Id, manifest));
        }
        return list;
    }
}
