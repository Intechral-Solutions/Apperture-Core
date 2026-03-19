namespace Apperture.ModuleSdk.Abstractions;

/// <summary>
/// Module-declared governed action contribution.
/// </summary>
/// <param name="Action">Lowercase dot-separated action key.</param>
/// <param name="ResourceType">Target resource type.</param>
/// <param name="IsPrivileged">Whether this action is privileged for audit/reporting semantics.</param>
public sealed record ModuleGovernedActionDeclaration(string Action, string ResourceType, bool IsPrivileged = false);

