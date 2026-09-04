namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Human operator identity. Aggregate root.
/// </summary>
/// <param name="DisplayTimeZoneId">
/// Optional IANA/Windows timezone id for operator timestamp display. Null means no preference
/// (display falls back to browser → UTC). This is a display concern only; scheduling and storage
/// remain UTC.
/// </param>
/// <param name="Avatar">
/// Presentation-only avatar selection. Null on records loaded before AUP-1 and treated as the
/// neutral built-in default by <see cref="EffectiveAvatar"/>.
/// </param>
public sealed record User(
    UserId Id,
    string DisplayName,
    IReadOnlyList<IdentityLink> IdentityLinks,
    IReadOnlyList<RoleId> AssignedRoleIds,
    bool IsBreakGlassEligible,
    string? DisplayTimeZoneId = null,
    UserAvatar? Avatar = null,
    string? EmailAddress = null,
    DateTimeOffset? EmailVerifiedAtUtc = null,
    string? PendingEmailAddress = null)
{
    public UserAvatar EffectiveAvatar => Avatar ?? UserAvatar.Default;

    /// <summary>Verified Apperture contact destination. Never an authentication key.</summary>
    public bool HasVerifiedContactEmail =>
        !string.IsNullOrWhiteSpace(EmailAddress) && EmailVerifiedAtUtc is not null;

    public string ContactEmailStatus =>
        HasVerifiedContactEmail ? "verified"
        : !string.IsNullOrWhiteSpace(PendingEmailAddress) ? "pending"
        : "none";
}
