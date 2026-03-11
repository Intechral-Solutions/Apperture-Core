using System.Text.Json;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// SQL persistence for platform roles and user-role assignments.
/// </summary>
public sealed class SqlRoleRepository : IRoleRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppertureDbContext _db;

    public SqlRoleRepository(AppertureDbContext db) => _db = db;

    /// <inheritdoc />
    public async ValueTask<Role?> GetAsync(RoleId roleId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Roles.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == roleId.Value, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _db.Roles.AsNoTracking().ToListAsync(cancellationToken);
        return entities.Select(ToDomain).ToList();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Role>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var roleIds = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId.Value)
            .Select(ur => ur.RoleId)
            .ToListAsync(cancellationToken);
        if (roleIds.Count == 0)
            return Array.Empty<Role>();

        var entities = await _db.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .ToListAsync(cancellationToken);
        return entities.Select(ToDomain).ToList();
    }

    /// <inheritdoc />
    public async ValueTask AssignToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default)
    {
        var exists = await _db.UserRoles.AnyAsync(ur => ur.UserId == userId.Value && ur.RoleId == roleId.Value, cancellationToken);
        if (exists)
            return;

        _db.UserRoles.Add(new UserRoleEntity
        {
            UserId = userId.Value,
            RoleId = roleId.Value
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask RemoveFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.UserRoles.FirstOrDefaultAsync(ur => ur.UserId == userId.Value && ur.RoleId == roleId.Value, cancellationToken);
        if (entity is null)
            return;

        _db.UserRoles.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static Role ToDomain(RoleEntity e)
    {
        var permissions = DeserializePermissions(e.PermissionsJson);
        return new Role(
            new RoleId(e.Id),
            e.Name,
            e.Description,
            permissions,
            e.IsSystemRole);
    }

    private static IReadOnlyList<PermissionDescriptor> DeserializePermissions(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return Array.Empty<PermissionDescriptor>();

        var list = JsonSerializer.Deserialize<List<PermissionDescriptorDto>>(json, JsonOptions);
        if (list is null)
            return Array.Empty<PermissionDescriptor>();

        return list.Select(dto => new PermissionDescriptor(
            dto.Action,
            dto.Resource is null ? null : new ResourceDescriptor(dto.Resource.ResourceType, dto.Resource.ScopeKey))).ToList();
    }

    private sealed class PermissionDescriptorDto
    {
        public string Action { get; set; } = "";
        public ResourceDescriptorDto? Resource { get; set; }
    }

    private sealed class ResourceDescriptorDto
    {
        public string ResourceType { get; set; } = "";
        public string ScopeKey { get; set; } = "";
    }
}
