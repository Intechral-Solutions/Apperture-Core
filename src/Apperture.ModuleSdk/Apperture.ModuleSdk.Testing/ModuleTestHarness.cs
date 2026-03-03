using Apperture.ModuleSdk.Abstractions;

namespace Apperture.ModuleSdk.Testing;

/// <summary>
/// Stub test harness for module authors. To be expanded during implementation.
/// </summary>
public sealed class ModuleTestHarness
{
    public static ModuleManifest GetManifest(IModule module) => module.GetManifest();
}
