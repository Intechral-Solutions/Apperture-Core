using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Named set of permissions. Aggregate root.
/// </summary>
public sealed record Role(
    RoleId Id,
    string Name,
    string? Description,
    IReadOnlyList<PermissionDescriptor> Permissions,
    bool IsSystemRole);
