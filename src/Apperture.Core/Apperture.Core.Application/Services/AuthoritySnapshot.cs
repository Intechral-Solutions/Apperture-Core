using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>
/// The rows an authorization decision about a role change is made from, read while they are locked. While the snapshot's
/// transaction is open no other transaction can change any row, or insert any key into any range, that fed it.
/// </summary>
/// <param name="ActorRoles">The roles assigned to the acting user, as locked. Empty when the actor has no user or no roles.</param>
/// <param name="TargetRole">The role being created from, replaced, assigned or removed, as locked; null when it does not exist (or none was named).</param>
/// <param name="TargetAssignmentExists">Whether the target user holds the target role. False when no assignment was asked about.</param>
public sealed record AuthoritySnapshot(
    IReadOnlyList<Role> ActorRoles,
    Role? TargetRole,
    bool TargetAssignmentExists);
