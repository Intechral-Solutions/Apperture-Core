using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Current holders of a role. Used by role detail and delete confirmation.
/// </summary>
public sealed record RoleAssignmentHolder(UserId UserId, string Username, string DisplayName);

/// <summary>
/// Assignment list for a live role, including the count used to decide delete.
/// </summary>
public sealed record RoleAssignmentList(int Count, IReadOnlyList<RoleAssignmentHolder> Holders);

/// <summary>
/// Outcome of a locked unassigned-role delete. The repository never removes <c>UserRoles</c> rows.
/// </summary>
public sealed record RoleUnassignedDeleteResult(
    RoleUnassignedDeleteOutcome Outcome,
    int AssignmentCount = 0,
    string? Name = null);

public enum RoleUnassignedDeleteOutcome
{
    Deleted,
    NotFound,
    AssignmentsRemain
}
