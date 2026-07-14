namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Human operator identity. Aggregate root.
/// </summary>
/// <param name="DisplayTimeZoneId">
/// SWM-5: optional IANA/Windows timezone id for operator timestamp display.
/// Null means no preference (UI falls back to browser → UTC). Setting via profile overhaul later.
/// </param>
public sealed record User(
    UserId Id,
    string DisplayName,
    IReadOnlyList<IdentityLink> IdentityLinks,
    IReadOnlyList<RoleId> AssignedRoleIds,
    bool IsBreakGlassEligible,
    string? DisplayTimeZoneId = null);
