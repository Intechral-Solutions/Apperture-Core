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
    public async ValueTask<int> CountUserAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
        await _db.UserRoles.CountAsync(ur => ur.RoleId == roleId.Value, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<UserId>> ListUserIdsWithRoleAsync(RoleId roleId, CancellationToken cancellationToken = default)
    {
        var ids = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.RoleId == roleId.Value)
            .Select(ur => ur.UserId)
            .ToListAsync(cancellationToken);
        return ids.ConvertAll(id => new UserId(id));
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

    /// <inheritdoc />
    public async ValueTask UpdatePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Roles.FirstOrDefaultAsync(e => e.Id == roleId.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Role {roleId.Value} not found.");
        entity.PermissionsJson = SerializePermissions(permissions);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<PermissionSetChange> ReplacePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default)
    {
        // The change is computed from the row this save replaces. Its RowVersion concurrency token makes the save fail if
        // anyone changed the row after this read, so the diff is exactly what committed.
        var entity = await _db.Roles.FirstOrDefaultAsync(e => e.Id == roleId.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Role {roleId.Value} not found.");
        // Against an unreadable stored list the previous set is empty, so every requested permission is an addition.
        var change = PermissionSetChange.Between(StoredRolePermissions.Read(entity.PermissionsJson).Permissions, permissions);
        entity.PermissionsJson = SerializePermissions(permissions);
        await _db.SaveChangesAsync(cancellationToken);
        return change;
    }

    /// <inheritdoc />
    public async ValueTask CreateAsync(Role role, CancellationToken cancellationToken = default)
    {
        _db.Roles.Add(new RoleEntity
        {
            Id = role.Id.Value,
            Name = role.Name,
            Description = role.Description,
            PermissionsJson = SerializePermissions(role.Permissions),
            IsSystemRole = false
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new InvalidOperationException("A role with this name already exists.", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask UpdateMetadataAsync(RoleId roleId, string name, string? description, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Roles.FirstOrDefaultAsync(e => e.Id == roleId.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Role {roleId.Value} not found.");
        entity.Name = name;
        entity.Description = description;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new InvalidOperationException("A role with this name already exists.", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask<RoleUnassignedDeleteResult> TryDeleteUnassignedAsync(RoleId roleId, CancellationToken cancellationToken = default)
    {
        if (_db.Database.IsRelational() && _db.Database.CurrentTransaction is null)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
                var result = await DeleteUnassignedCoreAsync(roleId, cancellationToken);
                if (result.Outcome == RoleUnassignedDeleteOutcome.Deleted)
                    await tx.CommitAsync(cancellationToken);
                return result;
            });
        }

        return await DeleteUnassignedCoreAsync(roleId, cancellationToken);
    }

    private async ValueTask<RoleUnassignedDeleteResult> DeleteUnassignedCoreAsync(RoleId roleId, CancellationToken cancellationToken)
    {
        RoleEntity? entity;
        if (_db.Database.IsRelational())
        {
            var locked = await _db.Roles
                .FromSqlInterpolated($"SELECT * FROM [Roles] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {roleId.Value}")
                .ToListAsync(cancellationToken);
            entity = locked.SingleOrDefault();
        }
        else
        {
            entity = await _db.Roles.FirstOrDefaultAsync(e => e.Id == roleId.Value, cancellationToken);
        }

        if (entity is null)
            return new RoleUnassignedDeleteResult(RoleUnassignedDeleteOutcome.NotFound);

        int assignmentCount;
        if (_db.Database.IsRelational())
        {
            var assignments = await _db.UserRoles
                .FromSqlInterpolated($"SELECT * FROM [UserRoles] WITH (UPDLOCK, HOLDLOCK) WHERE [RoleId] = {roleId.Value}")
                .ToListAsync(cancellationToken);
            assignmentCount = assignments.Count;
        }
        else
        {
            assignmentCount = await _db.UserRoles.CountAsync(ur => ur.RoleId == roleId.Value, cancellationToken);
        }

        if (assignmentCount > 0)
            return new RoleUnassignedDeleteResult(RoleUnassignedDeleteOutcome.AssignmentsRemain, assignmentCount, entity.Name);

        _db.Roles.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return new RoleUnassignedDeleteResult(RoleUnassignedDeleteOutcome.Deleted, 0, entity.Name);
    }

    /// <inheritdoc />
    public async ValueTask<AuthoritySnapshot> LockAuthoritySnapshotAsync(
        UserId? actorUserId,
        RoleId? targetRoleId,
        UserId? targetUserId,
        CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsRelational())
        {
            // No locks exist to take (the in-memory provider): the same rows, read.
            return await ReadAuthoritySnapshotAsync(actorUserId, targetRoleId, targetUserId, cancellationToken);
        }

        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The authority snapshot can only be locked inside an open transaction.");

        // Order matters for deadlock avoidance and is part of the contract: L1 the actor's assignments, L2 the role rows in
        // ascending id order, L3 the target assignment key. Every statement uses UPDLOCK, HOLDLOCK, which take locks under
        // READ_COMMITTED_SNAPSHOT as well, where an unhinted read would only see a version.
        var actorRoleIds = new List<string>();
        if (actorUserId is not null)
        {
            var assignments = await _db.UserRoles
                .FromSqlInterpolated($"SELECT * FROM [UserRoles] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] = {actorUserId.Value}")
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            actorRoleIds.AddRange(assignments.Select(a => a.RoleId));
        }

        var lockedRoles = new Dictionary<string, Role?>(StringComparer.Ordinal);
        var roleIds = actorRoleIds.Concat(targetRoleId is null ? [] : [targetRoleId.Value])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        foreach (var id in roleIds)
        {
            var rows = await _db.Roles
                .FromSqlInterpolated($"SELECT * FROM [Roles] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            lockedRoles[id] = rows.Count == 0 ? null : ToDomain(rows[0]);
        }

        var assigned = false;
        if (targetUserId is not null && targetRoleId is not null)
        {
            var rows = await _db.UserRoles
                .FromSqlInterpolated($"SELECT * FROM [UserRoles] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] = {targetUserId.Value} AND [RoleId] = {targetRoleId.Value}")
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            assigned = rows.Count > 0;
        }

        var actorRoles = actorRoleIds
            .Select(id => lockedRoles.GetValueOrDefault(id))
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();
        return new AuthoritySnapshot(actorRoles, targetRoleId is null ? null : lockedRoles.GetValueOrDefault(targetRoleId.Value), assigned);
    }

    private async ValueTask<AuthoritySnapshot> ReadAuthoritySnapshotAsync(
        UserId? actorUserId, RoleId? targetRoleId, UserId? targetUserId, CancellationToken cancellationToken)
    {
        var actorRoles = actorUserId is null ? Array.Empty<Role>() : await GetForUserAsync(actorUserId, cancellationToken);
        var targetRole = targetRoleId is null ? null : await GetAsync(targetRoleId, cancellationToken);
        var assigned = targetUserId is not null && targetRoleId is not null
            && await _db.UserRoles.AnyAsync(ur => ur.UserId == targetUserId.Value && ur.RoleId == targetRoleId.Value, cancellationToken);
        return new AuthoritySnapshot(actorRoles, targetRole, assigned);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 2601 or 2627)
                return true;
        }

        return false;
    }

    private static Role ToDomain(RoleEntity e)
    {
        // An unreadable stored list is a role with no grants that says so, so that nobody assigns it believing it is empty.
        var reading = StoredRolePermissions.Read(e.PermissionsJson);
        return new Role(
            new RoleId(e.Id),
            e.Name,
            e.Description,
            reading.Permissions,
            e.IsSystemRole,
            reading.IsReadable);
    }

    private static string SerializePermissions(IReadOnlyList<PermissionDescriptor> permissions) =>
        StoredRolePermissions.Serialize(permissions);
}
