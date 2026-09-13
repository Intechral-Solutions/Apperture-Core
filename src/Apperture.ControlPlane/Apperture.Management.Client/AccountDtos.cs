namespace Apperture.Management.Client;

/// <summary>Account profile as returned by <c>GET/PUT api/account/profile</c>.</summary>
public sealed class AccountProfileDto
{
    public string UserId { get; init; } = "";
    public string DisplayName { get; init; } = "";

    /// <summary>True when the platform, rather than the identity provider, owns the display name.</summary>
    public bool DisplayNameEditable { get; init; }

    /// <summary>From the identity provider's claims. Always read-only.</summary>
    public string? EmailAddress { get; init; }

    /// <summary>Provider principal (UPN or username). Always read-only.</summary>
    public string? UserPrincipal { get; init; }

    public string SignInSource { get; init; } = "";
    public AccountAvatarDto Avatar { get; init; } = new();
    public string? TimeZoneId { get; init; }
    public IReadOnlyList<AccountIdentityLinkDetailDto> IdentityLinks { get; init; } = [];
    public IReadOnlyList<string> RoleNames { get; init; } = [];

    /// <summary>Platform-owned verified contact destination. Not an authentication identity.</summary>
    public string? ContactEmail { get; init; }

    /// <summary><c>verified</c>, <c>pending</c>, or <c>none</c>.</summary>
    public string ContactEmailStatus { get; init; } = "none";

    public string? PendingContactEmail { get; init; }

    /// <summary>Untrusted Entra mail/UPN snapshot offered only as a suggestion.</summary>
    public string? SuggestedContactEmail { get; init; }

    public bool CanManageContactEmail { get; init; }

    /// <summary>True when the account has an enabled local password that can prove an email change.</summary>
    public bool HasEnabledLocalPassword { get; init; }
}

public sealed class AccountContactEmailDto
{
    public string? EmailAddress { get; init; }
    public string Status { get; init; } = "none";
    public DateTimeOffset? VerifiedAtUtc { get; init; }
    public string? PendingEmailAddress { get; init; }
    public string? SuggestedEntraEmail { get; init; }
    public bool CanManage { get; init; }
    public bool EmergencyAccessAccount { get; init; }
    public bool HasEnabledLocalPassword { get; init; }
}

public sealed class AccountContactMutationDto
{
    public AccountContactEmailDto Contact { get; init; } = new();
    public bool DeliverySucceeded { get; init; }
    public string? DeliveryFailure { get; init; }
}

public sealed class RequestContactEmailRequest
{
    public string? Email { get; init; }
    public string? CurrentPassword { get; init; }
}

public sealed class AccountIdentityLinkDetailDto
{
    public string Source { get; init; } = "";
    public string DisplayLabel { get; init; } = "";

    /// <summary>Provider object id. Support-facing detail, not primary content.</summary>
    public string ExternalId { get; init; } = "";

    public DateTimeOffset? LastAuthenticatedAtUtc { get; init; }
}

/// <summary>The user's current avatar selection.</summary>
public sealed class AccountAvatarDto
{
    /// <summary>Rendition requested by the shell and account surfaces.</summary>
    public const int DefaultImageSize = 128;

    public const string KindBuiltIn = "builtIn";
    public const string KindCustom = "custom";

    public string Kind { get; init; } = KindBuiltIn;

    /// <summary>Curated option id when <see cref="Kind"/> is <see cref="KindBuiltIn"/>.</summary>
    public string? BuiltInOptionId { get; init; }

    /// <summary>Changes whenever the rendered avatar changes; used for cache busting.</summary>
    public string Version { get; init; } = "";

    /// <summary>
    /// Relative API path for a custom photo, or null for built-in artwork which the client draws
    /// itself. Never a storage path.
    /// </summary>
    public string? ImageUrl { get; init; }

    public bool IsCustom => string.Equals(Kind, KindCustom, StringComparison.Ordinal);
}

/// <summary>Raw bytes of one avatar rendition, fetched through the authenticated API client.</summary>
public sealed record AccountAvatarImageDto(string ContentType, byte[] Content);

/// <summary>One curated avatar illustration offered to the operator.</summary>
public sealed class AvatarOptionDto
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public bool IsDefault { get; init; }
}

/// <summary>Typed personal preferences from <c>GET/PUT api/account/preferences</c>.</summary>
public sealed class UserPreferencesDto
{
    /// <summary><c>System</c>, <c>Light</c>, or <c>Dark</c>.</summary>
    public string Appearance { get; init; } = "System";

    /// <summary><c>System</c>, <c>Hour12</c>, or <c>Hour24</c>.</summary>
    public string TimeFormat { get; init; } = "System";

    /// <summary>Display timezone id, or null to follow the browser.</summary>
    public string? TimeZoneId { get; init; }
}

/// <summary>
/// One notification with the state of every channel that exists. A channel reported as not editable
/// has no user-facing switch behind it in the delivery pipeline, and the reason explains why.
/// </summary>
public sealed class NotificationPreferenceDto
{
    public string DefinitionId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? Description { get; init; }

    public bool PlatformEnabled { get; init; }
    public bool PlatformEditable { get; init; }
    public string? PlatformNotEditableReason { get; init; }

    public bool EmailEnabled { get; init; }
    public bool EmailEditable { get; init; }
    public string? EmailNotEditableReason { get; init; }
}

public sealed class UpdateAccountProfileRequestDto
{
    public string? DisplayName { get; init; }
}

public sealed class UpdateUserPreferencesRequestDto
{
    public string? Appearance { get; init; }
    public string? TimeFormat { get; init; }
    public string? TimeZoneId { get; init; }
}

public sealed class SelectAvatarRequestDto
{
    public string? OptionId { get; init; }
}

public sealed class SetNotificationPreferenceRequestDto
{
    public bool EmailEnabled { get; init; }
}
