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
    /// <summary>
    /// The user's one canonical, human-facing username (trimmed, lower-case). Unique across every name
    /// any user has ever held. Not the immutable identity: that is <see cref="Id"/>. A Local identity
    /// link's external id always equals this value. Names that predate the current username policy are
    /// kept verbatim, so a stored value is not guaranteed to be URL-safe or policy-conforming.
    /// </summary>
    public required string Username { get; init; }

    public UserAvatar EffectiveAvatar => Avatar ?? UserAvatar.Default;

    /// <summary>Verified Apperture contact destination. Never an authentication key.</summary>
    public bool HasVerifiedContactEmail =>
        !string.IsNullOrWhiteSpace(EmailAddress) && EmailVerifiedAtUtc is not null;

    public string ContactEmailStatus =>
        HasVerifiedContactEmail ? "verified"
        : !string.IsNullOrWhiteSpace(PendingEmailAddress) ? "pending"
        : "none";
}
