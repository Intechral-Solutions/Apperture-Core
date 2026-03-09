using Apperture.Shared.Kernel; // PermissionDescriptor, ResourceDescriptor

namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Action on a resource (e.g. "execute", "read", "admin"). Value object.
/// </summary>
public sealed record Permission(
    string Action,
    ResourceDescriptor? Resource,
    string? ModuleId);
