using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Registers module-declared permissions into the resource descriptor registry.
/// Validates declarations per ADR-0013; uses module scope for all module-contributed permissions.
/// </summary>
public static class ModulePermissionRegistrar
{
    /// <summary>
    /// Registers all declared permissions from the given manifests into the registry.
    /// Validates each declaration; throws <see cref="InvalidOperationException"/> if any declaration is invalid.
    /// Deduplicates by (Action, ResourceType, ScopeKey) so duplicate entries in manifests do not grow the catalog.
    /// </summary>
    public static void Register(
        IResourceDescriptorRegistry registry,
        IReadOnlyList<(string ModuleId, ModuleManifest Manifest)> manifests)
    {
        var seen = new HashSet<(string Action, string ResourceType, string ScopeKey)>();
        var errors = new List<string>();

        foreach (var (moduleId, manifest) in manifests)
        {
            RegisterGovernedResources(registry, moduleId, manifest, errors);
            RegisterGovernedActions(registry, moduleId, manifest, errors);

            if (manifest.Permissions is null or { Count: 0 })
                continue;

            foreach (var decl in manifest.Permissions)
            {
                if (!TryValidate(decl, moduleId, out var error))
                {
                    errors.Add($"Module {moduleId}: {error}");
                    continue;
                }

                var resourceType = decl.ResourceType!.Trim();
                var scopeKey = ScopeKeyConstants.Module(moduleId);
                var key = (decl.Action.Trim(), resourceType, scopeKey);
                if (!seen.Add(key))
                    continue;

                registry.RegisterPermission(ToDefinition(
                    moduleId,
                    decl.Action.Trim(),
                    resourceType,
                    decl.DisplayName,
                    decl.Description,
                    decl.Sensitivity,
                    decl.RequiresStepUp));
            }
        }

        if (errors.Count > 0)
            throw new InvalidOperationException($"Invalid module permission declaration(s): {string.Join("; ", errors)}");
    }

    private static void RegisterGovernedResources(
        IResourceDescriptorRegistry registry,
        string moduleId,
        ModuleManifest manifest,
        ICollection<string> errors)
    {
        if (manifest.GovernedResources is null or { Count: 0 })
            return;

        foreach (var resource in manifest.GovernedResources)
        {
            if (string.IsNullOrWhiteSpace(resource.ResourceType))
            {
                errors.Add($"Module {moduleId}: governed resource ResourceType is required.");
                continue;
            }

            var scope = string.IsNullOrWhiteSpace(resource.ScopeKey)
                ? ScopeKeyConstants.Module(moduleId)
                : resource.ScopeKey.Trim();
            registry.RegisterResource(new ResourceDescriptor(resource.ResourceType.Trim(), scope), ResourceFamily.Core);
        }
    }

    private static void RegisterGovernedActions(
        IResourceDescriptorRegistry registry,
        string moduleId,
        ModuleManifest manifest,
        ICollection<string> errors)
    {
        if (manifest.GovernedActions is null or { Count: 0 })
            return;

        foreach (var action in manifest.GovernedActions)
        {
            if (string.IsNullOrWhiteSpace(action.Action))
            {
                errors.Add($"Module {moduleId}: governed action Action is required.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(action.ResourceType))
            {
                errors.Add($"Module {moduleId}: governed action ResourceType is required.");
                continue;
            }

            var actionKey = action.Action.Trim();
            if (actionKey != actionKey.ToLowerInvariant())
            {
                errors.Add($"Module {moduleId}: governed action '{actionKey}' must be lowercase.");
                continue;
            }

            if (!TryValidateMetadata(actionKey, action.DisplayName, action.Description, action.Sensitivity, out var metadataError))
            {
                errors.Add($"Module {moduleId}: governed action {metadataError}");
                continue;
            }

            registry.RegisterPermission(ToDefinition(
                moduleId,
                actionKey,
                action.ResourceType.Trim(),
                action.DisplayName,
                action.Description,
                action.Sensitivity,
                action.RequiresStepUp));
        }
    }

    /// <summary>
    /// A module permission is always module-origin, in the Modules category, and always held by the Platform Administrator
    /// by default: a module cannot decline the administrator default.
    /// </summary>
    private static PermissionDefinition ToDefinition(
        string moduleId,
        string action,
        string resourceType,
        string displayName,
        string description,
        PermissionSensitivity sensitivity,
        bool requiresStepUp) =>
        new(
            new PermissionDescriptor(action, new ResourceDescriptor(resourceType, ScopeKeyConstants.Module(moduleId))),
            displayName.Trim(),
            description.Trim(),
            PermissionCategory.Modules,
            sensitivity,
            requiresStepUp,
            GrantedToPlatformAdministratorByDefault: true,
            Origin: PermissionOrigin.Module,
            ModuleId: moduleId,
            Family: ResourceFamily.Core);

    private static bool TryValidateMetadata(
        string action,
        string? displayName,
        string? description,
        PermissionSensitivity sensitivity,
        out string? error)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(displayName))
            problems.Add("DisplayName is required.");
        else if (displayName.Trim().Length > PermissionDefinition.MaxDisplayNameLength)
            problems.Add($"DisplayName must be at most {PermissionDefinition.MaxDisplayNameLength} characters.");

        if (string.IsNullOrWhiteSpace(description))
            problems.Add("Description is required (one sentence saying what holding the permission allows).");
        else if (description.Trim().Length > PermissionDefinition.MaxDescriptionLength)
            problems.Add($"Description must be at most {PermissionDefinition.MaxDescriptionLength} characters.");

        if (!Enum.IsDefined(sensitivity))
            problems.Add($"Sensitivity '{sensitivity}' is not a defined sensitivity (Standard, Elevated or Critical).");

        if (problems.Count == 0)
        {
            error = null;
            return true;
        }

        error = $"'{action}': {string.Join(" ", problems)}";
        return false;
    }

    private static bool TryValidate(ModulePermissionDeclaration decl, string moduleId, out string? error)
    {
        if (string.IsNullOrWhiteSpace(decl.Action))
        {
            error = "Action is required and must not be empty.";
            return false;
        }

        var action = decl.Action.Trim();
        if (action != action.ToLowerInvariant())
        {
            error = "Action must be lowercase.";
            return false;
        }

        var segments = action.Split('.');
        if (segments.Length < 2 || segments.Any(string.IsNullOrWhiteSpace))
        {
            error = "Action must have at least two dot-separated segments (e.g. workflow.execute).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(decl.ResourceType))
        {
            error = "ResourceType is required. Declare explicitly (e.g. WorkflowInstance, OperationDefinition).";
            return false;
        }
        if (decl.ResourceType.Trim().Length == 0)
        {
            error = "ResourceType must not be whitespace.";
            return false;
        }

        if (!TryValidateMetadata(action, decl.DisplayName, decl.Description, decl.Sensitivity, out var metadataError))
        {
            error = $"permission {metadataError}";
            return false;
        }

        error = null;
        return true;
    }
}
