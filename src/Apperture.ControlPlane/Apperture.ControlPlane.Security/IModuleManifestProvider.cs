using Apperture.ModuleSdk.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Provides the set of known module manifests for permission registration at startup.
/// Implementations typically resolve DI-registered modules and return their manifests.
/// </summary>
public interface IModuleManifestProvider
{
    /// <summary>
    /// Returns the set of known (module id, manifest) pairs.
    /// </summary>
    IReadOnlyList<(string ModuleId, ModuleManifest Manifest)> GetManifests();
}
