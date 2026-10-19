using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Persistence for platform roles and user-role assignments.
/// </summary>
public interface IRoleRepository
{
    /// <summary>
    /// Gets a role by id.
    /// </summary>
    ValueTask<Role?> GetAsync(RoleId roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all roles.
    /// </summary>
    ValueTask<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets roles assigned to a user.
    /// </summary>
    ValueTask<IReadOnlyList<Role>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts how many users have the given role assigned.
    /// </summary>
    ValueTask<int> CountUserAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists user ids that currently have the given role. Default is empty so test doubles need not implement it.
    /// </summary>
    ValueTask<IReadOnlyList<UserId>> ListUserIdsWithRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<UserId>>(Array.Empty<UserId>());

    /// <summary>
    /// Assigns a role to a user.
    /// </summary>
    ValueTask AssignToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a role assignment from a user.
    /// </summary>
    ValueTask RemoveFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the permissions for a role. Replaces PermissionsJson.
    /// </summary>
    ValueTask UpdatePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the permissions of a role and returns what that replacement added and removed (AUD-1.2 permission diffs).
    /// The SQL implementation computes the change from the very row it updates, under that row's concurrency token, so the
    /// diff is the one that committed, never an earlier read. The default composes the other members for test doubles.
    /// Throws <see cref="InvalidOperationException"/> when the role is missing.
    /// </summary>
    async ValueTask<PermissionSetChange> ReplacePermissionsAsync(
        RoleId roleId,
        IReadOnlyList<PermissionDescriptor> permissions,
        CancellationToken cancellationToken = default)
    {
        var role = await GetAsync(roleId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Role {roleId.Value} not found.");
        await UpdatePermissionsAsync(roleId, permissions, cancellationToken).ConfigureAwait(false);
        return PermissionSetChange.Between(role.Permissions, permissions);
    }

    /// <summary>
    /// Inserts a user-created role row, including its initial permission JSON, in one save.
    /// Throws <see cref="InvalidOperationException"/> when the name collides with <c>IX_Roles_Name</c>.
    /// </summary>
    ValueTask CreateAsync(Role role, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>
    /// Replaces name and description. Does not change permissions or <c>IsSystemRole</c>.
    /// Throws <see cref="InvalidOperationException"/> when the role is missing or the name collides.
    /// </summary>
    ValueTask UpdateMetadataAsync(RoleId roleId, string name, string? description, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>
    /// Deletes the role row only when a locked assignment count is zero. Never removes assignments.
    /// </summary>
    ValueTask<RoleUnassignedDeleteResult> TryDeleteUnassignedAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>
    /// Locks and returns the rows an authorization decision about a role change is made from. The SQL implementation must run
    /// inside an open transaction (it refuses otherwise) and takes, with <c>UPDLOCK, HOLDLOCK</c> and in this order:
    /// <list type="number">
    /// <item>the actor's <c>UserRoles</c> key range;</item>
    /// <item>the <c>Roles</c> rows of the actor's roles and of <paramref name="targetRoleId"/>, once each, in ascending ordinal
    /// role-id order;</item>
    /// <item>when both <paramref name="targetUserId"/> and <paramref name="targetRoleId"/> are given, the <c>(user, role)</c>
    /// assignment key, present or absent.</item>
    /// </list>
    /// The locks are held until the surrounding transaction ends. The default reads the same rows without locking, for test
    /// doubles that have no transaction.
    /// </summary>
    async ValueTask<AuthoritySnapshot> LockAuthoritySnapshotAsync(
        UserId? actorUserId,
        RoleId? targetRoleId,
        UserId? targetUserId,
        CancellationToken cancellationToken = default)
    {
        var actorRoles = actorUserId is null
            ? Array.Empty<Role>()
            : await GetForUserAsync(actorUserId, cancellationToken).ConfigureAwait(false);
        var targetRole = targetRoleId is null ? null : await GetAsync(targetRoleId, cancellationToken).ConfigureAwait(false);
        var assigned = targetUserId is not null
            && targetRoleId is not null
            && (await GetForUserAsync(targetUserId, cancellationToken).ConfigureAwait(false)).Any(r => r.Id.Value == targetRoleId.Value);
        return new AuthoritySnapshot(actorRoles, targetRole, assigned);
    }
}
