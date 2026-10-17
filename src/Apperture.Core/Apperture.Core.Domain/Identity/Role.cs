using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Named set of permissions. Aggregate root.
/// </summary>
/// <param name="PermissionsReadable">
/// False when the role's stored permissions could not be read (malformed JSON, or an element that is not a readable grant).
/// <see cref="Permissions"/> is then empty: an unreadable list contributes no grants, and no readable part of it is salvaged.
/// Roles built in code are readable.
/// </param>
public sealed record Role(
    RoleId Id,
    string Name,
    string? Description,
    IReadOnlyList<PermissionDescriptor> Permissions,
    bool IsSystemRole,
    bool PermissionsReadable = true);
