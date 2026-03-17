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
            if (manifest.Permissions is null or { Count: 0 })
                continue;

            foreach (var decl in manifest.Permissions)
            {
                if (!TryValidate(decl, moduleId, out var error))
                {
                    errors.Add($"Module {moduleId}: {error}");
                    continue;
                }

                var resourceType = string.IsNullOrWhiteSpace(decl.ResourceType)
                    ? ResourceTypeConstants.OperationDefinition
                    : decl.ResourceType!.Trim();
                var scopeKey = ScopeKeyConstants.Module(moduleId);
                var key = (decl.Action.Trim(), resourceType, scopeKey);
                if (!seen.Add(key))
                    continue;

                var descriptor = new PermissionDescriptor(
                    decl.Action.Trim(),
                    new ResourceDescriptor(resourceType, scopeKey));
                registry.RegisterPermission(descriptor, ResourceFamily.Operation);
            }
        }

        if (errors.Count > 0)
            throw new InvalidOperationException($"Invalid module permission declaration(s): {string.Join("; ", errors)}");
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

        if (!string.IsNullOrWhiteSpace(decl.ResourceType) && decl.ResourceType.Trim().Length == 0)
        {
            error = "ResourceType, if provided, must not be whitespace.";
            return false;
        }

        error = null;
        return true;
    }
}
