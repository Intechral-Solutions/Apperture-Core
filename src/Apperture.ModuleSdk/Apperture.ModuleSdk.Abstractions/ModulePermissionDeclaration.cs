namespace Apperture.ModuleSdk.Abstractions;

/// <summary>
/// Declares a single permission that a module contributes to the platform permission catalog.
/// Registered at host startup with scope module:{moduleId}. Action must follow ADR-0013 (lowercase, dot-separated).
/// </summary>
/// <param name="Action">Permission action string, e.g. workflow.execute, helloworld.run.</param>
/// <param name="ResourceType">Optional resource type for the permission descriptor; if omitted, host uses a default.</param>
public sealed record ModulePermissionDeclaration(string Action, string? ResourceType = null);
