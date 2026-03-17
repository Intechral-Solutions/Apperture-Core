namespace Apperture.ModuleSdk.Abstractions;

/// <summary>
/// Module metadata contract.
/// Configuration schemas should model credential inputs as secret references, not raw secret strings.
/// </summary>
/// <param name="Id">Unique module identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Version">Semantic version.</param>
/// <param name="Description">Human-readable description.</param>
public sealed record ModuleManifest(string Id, string Name, string Version, string Description)
{
    /// <summary>
    /// Optional list of permissions this module declares. Registered at host startup into IResourceDescriptorRegistry with scope module:{Id}.
    /// </summary>
    public IReadOnlyList<ModulePermissionDeclaration>? Permissions { get; init; } = null;
}
