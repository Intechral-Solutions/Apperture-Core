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
    private readonly ConcurrentDictionary<string, (PermissionDescriptor Descriptor, ResourceFamily? Family)> _permissions = new(StringComparer.Ordinal);

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
    public void RegisterPermission(PermissionDescriptor descriptor, ResourceFamily? family = null)
    {
        ValidatePermission(descriptor);
        var key = ToPermissionKey(descriptor);
        var entry = (descriptor, family);
        if (!_permissions.TryAdd(key, entry))
        {
            var existing = _permissions[key];
            if (!existing.Descriptor.Equals(descriptor) || !Equals(existing.Family, family))
                throw new InvalidOperationException(
                    $"Conflicting permission registration for key '{key}'. " +
                    $"Existing: Action={existing.Descriptor.Action}, Resource={FormatResource(existing.Descriptor.Resource)}, Family={existing.Family}. " +
                    $"Incoming: Action={descriptor.Action}, Resource={FormatResource(descriptor.Resource)}, Family={family}.");
        }
    }

    private static string FormatResource(ResourceDescriptor? r) =>
        r is null ? "null" : $"{r.ResourceType}:{r.ScopeKey}";

    /// <inheritdoc />
    public IReadOnlyList<ResourceDescriptor> GetResourceDescriptors(ResourceFamily? family = null) =>
        family is null
            ? _resources.Values.Select(r => r.Descriptor).ToList()
            : _resources.Values.Where(r => r.Family == family).Select(r => r.Descriptor).ToList();

    /// <inheritdoc />
    public IReadOnlyList<PermissionDescriptor> GetPermissionDescriptors(ResourceFamily? family = null) =>
        family is null
            ? _permissions.Values.Select(p => p.Descriptor).ToList()
            : _permissions.Values.Where(p => p.Family == family).Select(p => p.Descriptor).ToList();

    /// <inheritdoc />
    public bool IsPermissionRegistered(PermissionDescriptor descriptor)
    {
        ValidatePermission(descriptor);
        var key = ToPermissionKey(descriptor);
        return _permissions.ContainsKey(key);
    }

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
