using System.Collections.Concurrent;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Thread-safe in-memory implementation of IResourceDescriptorRegistry.
/// Used for Phase 1 scaffolding; seeded with platform permission catalog at startup.
/// </summary>
public sealed class InMemoryResourceDescriptorRegistry : IResourceDescriptorRegistry
{
    private readonly ConcurrentDictionary<string, (ResourceDescriptor Descriptor, ResourceFamily? Family)> _resources = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PermissionDefinition> _permissions = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void RegisterResource(ResourceDescriptor descriptor, ResourceFamily? family = null)
    {
        ValidateResource(descriptor);
        var key = ToResourceKey(descriptor);
        var entry = (descriptor, family);
        if (!_resources.TryAdd(key, entry))
        {
            var existing = _resources[key];
            if (!existing.Descriptor.Equals(descriptor) || !Equals(existing.Family, family))
                throw new InvalidOperationException(
                    $"Conflicting resource registration for key '{key}'. " +
                    $"Existing: ResourceType={existing.Descriptor.ResourceType}, ScopeKey={existing.Descriptor.ScopeKey}, Family={existing.Family}. " +
                    $"Incoming: ResourceType={descriptor.ResourceType}, ScopeKey={descriptor.ScopeKey}, Family={family}.");
        }
    }

    /// <inheritdoc />
    public void RegisterPermission(PermissionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateDefinition(definition);
        var key = ToPermissionKey(definition.Identity);
        if (!_permissions.TryAdd(key, definition))
        {
            var existing = _permissions[key];
            if (!existing.Equals(definition))
                throw new InvalidOperationException(
                    $"Conflicting permission registration for key '{key}'. " +
                    $"Existing: {Describe(existing)}. Incoming: {Describe(definition)}.");
        }
    }

    private static string FormatResource(ResourceDescriptor? r) =>
        r is null ? "null" : $"{r.ResourceType}:{r.ScopeKey}";

    private static string Describe(PermissionDefinition d) =>
        $"Action={d.Identity.Action}, Resource={FormatResource(d.Identity.Resource)}, Family={d.Family}, DisplayName='{d.DisplayName}', " +
        $"Category={d.Category}, Sensitivity={d.Sensitivity}, RequiresStepUp={d.RequiresStepUp}, " +
        $"GrantedToPlatformAdministratorByDefault={d.GrantedToPlatformAdministratorByDefault}, Origin={d.Origin}, ModuleId={d.ModuleId ?? "null"}";

    /// <inheritdoc />
    public IReadOnlyList<ResourceDescriptor> GetResourceDescriptors(ResourceFamily? family = null) =>
        family is null
            ? _resources.Values.Select(r => r.Descriptor).ToList()
            : _resources.Values.Where(r => r.Family == family).Select(r => r.Descriptor).ToList();

    /// <inheritdoc />
    public IReadOnlyList<PermissionDefinition> GetPermissionDefinitions(ResourceFamily? family = null) =>
        family is null
            ? _permissions.Values.ToList()
            : _permissions.Values.Where(p => p.Family == family).ToList();

    /// <inheritdoc />
    public IReadOnlyList<PermissionDescriptor> GetPermissionDescriptors(ResourceFamily? family = null) =>
        GetPermissionDefinitions(family).Select(p => p.Identity).ToList();

    /// <inheritdoc />
    public bool IsPermissionRegistered(PermissionDescriptor descriptor)
    {
        ValidatePermission(descriptor);
        var key = ToPermissionKey(descriptor);
        return _permissions.ContainsKey(key);
    }

    /// <inheritdoc />
    public IReadOnlyList<(ResourceDescriptor Descriptor, ResourceFamily? Family)> GetResourceDescriptorsWithFamily(ResourceFamily? family = null) =>
        family is null
            ? _resources.Values.Select(r => (r.Descriptor, r.Family)).ToList()
            : _resources.Values.Where(r => r.Family == family).Select(r => (r.Descriptor, r.Family)).ToList();

    /// <inheritdoc />
    public IReadOnlyList<(PermissionDescriptor Descriptor, ResourceFamily? Family)> GetPermissionDescriptorsWithFamily(ResourceFamily? family = null) =>
        GetPermissionDefinitions(family).Select(p => (p.Identity, p.Family)).ToList();

    private static string ToResourceKey(ResourceDescriptor descriptor) =>
        $"{descriptor.ResourceType}::{descriptor.ScopeKey}";

    private static string ToPermissionKey(PermissionDescriptor descriptor)
    {
        if (descriptor.Resource is null)
            return $"{descriptor.Action}::";
        return $"{descriptor.Action}::{descriptor.Resource.ResourceType}::{descriptor.Resource.ScopeKey}";
    }

    private static void ValidateResource(ResourceDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.ResourceType))
            throw new InvalidOperationException("ResourceType is required.");
        if (string.IsNullOrWhiteSpace(descriptor.ScopeKey))
            throw new InvalidOperationException("ScopeKey is required.");
        if (descriptor.ResourceType != descriptor.ResourceType.Trim())
            throw new InvalidOperationException("ResourceType must not have leading or trailing whitespace.");
        if (descriptor.ScopeKey != descriptor.ScopeKey.Trim())
            throw new InvalidOperationException("ScopeKey must not have leading or trailing whitespace.");
        if (descriptor.ResourceType.Length > 0 && !char.IsLetter(descriptor.ResourceType[0]))
            throw new InvalidOperationException($"ResourceType '{descriptor.ResourceType}' must start with a letter (PascalCase).");
    }

    private static void ValidateDefinition(PermissionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition.Identity);
        ValidatePermission(definition.Identity);
        var who = $"Permission '{definition.Identity.Action}' ({FormatResource(definition.Identity.Resource)})";

        if (string.IsNullOrWhiteSpace(definition.DisplayName))
            throw new InvalidOperationException($"{who}: DisplayName is required.");
        if (definition.DisplayName.Length > PermissionDefinition.MaxDisplayNameLength)
            throw new InvalidOperationException($"{who}: DisplayName must be at most {PermissionDefinition.MaxDisplayNameLength} characters.");
        if (string.IsNullOrWhiteSpace(definition.Description))
            throw new InvalidOperationException($"{who}: Description is required.");
        if (definition.Description.Length > PermissionDefinition.MaxDescriptionLength)
            throw new InvalidOperationException($"{who}: Description must be at most {PermissionDefinition.MaxDescriptionLength} characters.");
        if (!Enum.IsDefined(definition.Category))
            throw new InvalidOperationException($"{who}: Category '{definition.Category}' is not a defined category.");
        if (!Enum.IsDefined(definition.Sensitivity))
            throw new InvalidOperationException($"{who}: Sensitivity '{definition.Sensitivity}' is not a defined sensitivity.");
        if (!Enum.IsDefined(definition.Origin))
            throw new InvalidOperationException($"{who}: Origin '{definition.Origin}' is not a defined origin.");

        if (definition.Origin == PermissionOrigin.Module && string.IsNullOrWhiteSpace(definition.ModuleId))
            throw new InvalidOperationException($"{who}: ModuleId is required for a module permission.");
        if (definition.Origin == PermissionOrigin.Platform && definition.ModuleId is not null)
            throw new InvalidOperationException($"{who}: ModuleId must be empty for a platform permission.");
    }

    private static void ValidatePermission(PermissionDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Action))
            throw new InvalidOperationException("Action is required.");
        if (descriptor.Action != descriptor.Action.Trim())
            throw new InvalidOperationException("Action must not have leading or trailing whitespace.");
        if (descriptor.Action != descriptor.Action.ToLowerInvariant())
            throw new InvalidOperationException($"Permission action '{descriptor.Action}' must be lowercase.");
        var segments = descriptor.Action.Split('.');
        if (segments.Length < 2 || segments.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException($"Permission action '{descriptor.Action}' must be dot-separated (e.g. workflow.start).");
        if (descriptor.Resource is not null)
            ValidateResource(descriptor.Resource);
    }
}
