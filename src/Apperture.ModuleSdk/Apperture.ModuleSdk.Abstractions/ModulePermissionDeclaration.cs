using Apperture.Core.Domain.Governance;

namespace Apperture.ModuleSdk.Abstractions;

/// <summary>
/// Declares a single permission that a module contributes to the platform permission catalog.
/// Registered at host startup with scope module:{moduleId}. Action must follow ADR-0013 (lowercase, dot-separated).
/// A declaration without a display name or description fails registration, naming the module.
/// </summary>
/// <param name="Action">Permission action string, e.g. workflow.execute, helloworld.run.</param>
/// <param name="ResourceType">Resource type for the permission descriptor. Required; registration fails without it.</param>
/// <param name="DisplayName">What an operator sees in the role editor, at most 80 characters. Name the module so two modules' permissions can be told apart.</param>
/// <param name="Description">One sentence saying what holding the permission allows, at most 300 characters.</param>
/// <param name="Sensitivity">How much harm holding the permission can do. A declared classification for operators, not a security boundary against the module itself.</param>
/// <param name="RequiresStepUp">Whether exercising the permission needs recent strong authentication.</param>
public sealed record ModulePermissionDeclaration(
    string Action,
    string? ResourceType,
    string DisplayName,
    string Description,
    PermissionSensitivity Sensitivity = PermissionSensitivity.Standard,
    bool RequiresStepUp = false);
