using System.Text.Json;
using Apperture.Shared.Kernel;

namespace Apperture.Persistence.SqlServer;

/// <summary>What the permissions stored for a role read as.</summary>
/// <param name="Permissions">The grants, or none when the stored value is unreadable.</param>
/// <param name="IsReadable">False when the stored value could not be read as a list of grants.</param>
public sealed record StoredPermissionsReading(IReadOnlyList<PermissionDescriptor> Permissions, bool IsReadable);

/// <summary>
/// Reads the permission JSON stored on a role (<c>Roles.PermissionsJson</c>).
/// </summary>
/// <remarks>
/// A blank value or an empty array is a readable role with no grants. Anything else that is not a list of readable grants is
/// <b>unreadable</b>, and an unreadable value contributes no grants at all: the readable part of a damaged list is not
/// salvaged, because a partly trusted list would silently grant less (or, once repaired, more) than the role appears to
/// hold. Unreadable means: not valid JSON, not an array, or an array with an element that is null, has no action, or names a
/// resource with a null type or scope. A grant with no resource is readable (it is a broad grant).
/// </remarks>
public static class StoredRolePermissions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly StoredPermissionsReading Empty = new(Array.Empty<PermissionDescriptor>(), IsReadable: true);
    private static readonly StoredPermissionsReading Unreadable = new(Array.Empty<PermissionDescriptor>(), IsReadable: false);

    public static StoredPermissionsReading Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return Empty;

        List<PermissionDescriptorDto?>? list;
        try
        {
            list = JsonSerializer.Deserialize<List<PermissionDescriptorDto?>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return Unreadable;
        }
        catch (NotSupportedException)
        {
            return Unreadable;
        }

        if (list is null)
            return Unreadable;

        var permissions = new List<PermissionDescriptor>(list.Count);
        foreach (var dto in list)
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Action))
                return Unreadable;
            if (dto.Resource is null)
            {
                permissions.Add(new PermissionDescriptor(dto.Action));
                continue;
            }

            if (dto.Resource.ResourceType is null || dto.Resource.ScopeKey is null)
                return Unreadable;
            permissions.Add(new PermissionDescriptor(dto.Action, new ResourceDescriptor(dto.Resource.ResourceType, dto.Resource.ScopeKey)));
        }

        return new StoredPermissionsReading(permissions, IsReadable: true);
    }

    public static string Serialize(IReadOnlyList<PermissionDescriptor> permissions)
    {
        if (permissions is null || permissions.Count == 0)
            return "[]";
        var dtos = permissions.Select(p => new PermissionDescriptorDto
        {
            Action = p.Action,
            Resource = p.Resource is null ? null : new ResourceDescriptorDto
            {
                ResourceType = p.Resource.ResourceType,
                ScopeKey = p.Resource.ScopeKey
            }
        }).ToList();
        return JsonSerializer.Serialize(dtos, JsonOptions);
    }

    private sealed class PermissionDescriptorDto
    {
        public string? Action { get; set; } = "";
        public ResourceDescriptorDto? Resource { get; set; }
    }

    private sealed class ResourceDescriptorDto
    {
        public string? ResourceType { get; set; } = "";
        public string? ScopeKey { get; set; } = "";
    }
}
