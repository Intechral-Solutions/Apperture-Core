namespace Apperture.Management.Api.Models;

/// <summary>
/// Permission entry in the governance catalog with metadata for admin display.
/// </summary>
public sealed record PermissionCatalogEntryDto(
    string Action,
    string? ResourceType,
    string? ScopeKey,
    string? Family,
    string Source,
    string? ModuleId);

/// <summary>
/// Resource entry in the governance catalog with metadata for admin display.
/// </summary>
public sealed record ResourceCatalogEntryDto(
    string ResourceType,
    string ScopeKey,
    string? Family);

/// <summary>
/// Role detail including metadata and granted permissions.
/// </summary>
public sealed record RoleDetailDto(
    string Id,
    string Name,
    string? Description,
    bool IsSystemRole,
    IReadOnlyList<RolePermissionEntryDto> Permissions,
    int AssignmentCount = 0);

/// <summary>
/// Current holders of a role, for delete confirmation. Not a user directory.
/// </summary>
public sealed record RoleAssignmentsDto(
    int Count,
    IReadOnlyList<RoleAssignmentHolderDto> Holders);

/// <summary>
/// Identity shown when confirming whether a role can be deleted.
/// </summary>
public sealed record RoleAssignmentHolderDto(string UserId, string DisplayName);

/// <summary>
/// A single permission granted to a role.
/// </summary>
public sealed record RolePermissionEntryDto(
    string Action,
    string? ResourceType,
    string? ScopeKey);

/// <summary>
/// Effective access for a principal (user), computed from role membership.
/// </summary>
public sealed record PrincipalEffectiveAccessDto(
    string UserId,
    IReadOnlyList<string> RoleIds,
    IReadOnlyList<RolePermissionEntryDto> Permissions);
