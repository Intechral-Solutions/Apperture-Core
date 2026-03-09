namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Human operator identity. Aggregate root.
/// </summary>
public sealed record User(
    UserId Id,
    string DisplayName,
    IReadOnlyList<IdentityLink> IdentityLinks,
    IReadOnlyList<RoleId> AssignedRoleIds,
    bool IsBreakGlassEligible);
