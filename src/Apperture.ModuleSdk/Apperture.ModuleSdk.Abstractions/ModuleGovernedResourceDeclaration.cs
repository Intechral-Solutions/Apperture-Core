namespace Apperture.ModuleSdk.Abstractions;

/// <summary>
/// Module-declared governed resource type contribution.
/// </summary>
/// <param name="ResourceType">PascalCase resource type name, e.g. AcmeConnector.</param>
/// <param name="ScopeKey">Default scope key for this resource type.</param>
public sealed record ModuleGovernedResourceDeclaration(string ResourceType, string ScopeKey);

