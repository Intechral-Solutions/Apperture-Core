namespace Apperture.ModuleSdk.Abstractions;

/// <summary>
/// Module metadata contract.
/// Configuration schemas should model credential inputs as secret references, not raw secret strings.
/// </summary>
public sealed record ModuleManifest(string Id, string Name, string Version, string Description);
