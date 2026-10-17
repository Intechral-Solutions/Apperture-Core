using Apperture.Core.Domain.Governance;

namespace Apperture.ModuleSdk.Abstractions;

/// <summary>
/// Module-declared governed action contribution. It registers a permission at scope module:{moduleId}, so it carries the same
/// operator-facing metadata as <see cref="ModulePermissionDeclaration"/>. A declaration without a display name or description
/// fails registration, naming the module.
/// </summary>
/// <param name="Action">Lowercase dot-separated action key.</param>
/// <param name="ResourceType">Target resource type.</param>
/// <param name="DisplayName">What an operator sees in the role editor, at most 80 characters.</param>
/// <param name="Description">One sentence saying what holding the permission allows, at most 300 characters.</param>
/// <param name="Sensitivity">How much harm holding the permission can do. A declared classification for operators, not a security boundary against the module itself.</param>
/// <param name="RequiresStepUp">Whether exercising the permission needs recent strong authentication.</param>
public sealed record ModuleGovernedActionDeclaration(
    string Action,
    string ResourceType,
    string DisplayName,
    string Description,
    PermissionSensitivity Sensitivity = PermissionSensitivity.Standard,
    bool RequiresStepUp = false);
