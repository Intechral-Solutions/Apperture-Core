using Apperture.ModuleSdk.Abstractions.Signals;

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

    /// <summary>
    /// Optional governed resource type declarations contributed by this module.
    /// </summary>
    public IReadOnlyList<ModuleGovernedResourceDeclaration>? GovernedResources { get; init; } = null;

    /// <summary>
    /// Optional governed action declarations contributed by this module.
    /// </summary>
    public IReadOnlyList<ModuleGovernedActionDeclaration>? GovernedActions { get; init; } = null;

    /// <summary>
    /// Optional documentation of connector definitions associated with this module (not a runtime registry).
    /// </summary>
    public IReadOnlyList<ConnectorDefinitionDeclaration>? ConnectorDefinitions { get; init; }

    /// <summary>
    /// Optional module-declared operator procedures (integration-scoped launch surface). See ADR-0029.
    /// </summary>
    public IReadOnlyList<ModuleProcedureDeclaration>? Procedures { get; init; }

    /// <summary>
    /// Optional module-authored platform signal declarations (Epic 77 Phase 2).
    /// </summary>
    public IReadOnlyList<ModuleSignalDeclaration>? Signals { get; init; }
}
