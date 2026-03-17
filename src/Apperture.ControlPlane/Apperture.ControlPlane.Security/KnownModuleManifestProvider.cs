using Apperture.ModuleSdk.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Provides manifests from a set of DI-registered IModule instances.
/// </summary>
public sealed class KnownModuleManifestProvider : IModuleManifestProvider
{
    private readonly IReadOnlyList<IModule> _modules;

    public KnownModuleManifestProvider(IEnumerable<IModule> modules)
    {
        _modules = modules?.ToList() ?? [];
    }

    /// <inheritdoc />
    public IReadOnlyList<(string ModuleId, ModuleManifest Manifest)> GetManifests()
    {
        var list = new List<(string ModuleId, ModuleManifest Manifest)>(_modules.Count);
        foreach (var module in _modules)
        {
            var manifest = module.GetManifest();
            list.Add((manifest.Id, manifest));
        }
        return list;
    }
}
