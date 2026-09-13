using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Accounts;

/// <summary>
/// The persisted half of the account profile. Session-derived facts (sign-in source, principal,
/// role names) are composed by the API from the current request rather than read from storage.
/// </summary>
public sealed record AccountProfileSnapshot(
    string UserId,
    string DisplayName,
    bool DisplayNameEditable,
    UserAvatar Avatar,
    string? DisplayTimeZoneId,
    IReadOnlyList<AccountIdentityLinkSnapshot> IdentityLinks);

public sealed record AccountIdentityLinkSnapshot(
    string Source,
    string DisplayLabel,
    string ExternalId,
    DateTimeOffset? LastAuthenticatedAtUtc);

/// <summary>Combined personal preferences returned to and accepted from the account UI.</summary>
public sealed record AccountPreferencesSnapshot(
    AppearancePreference Appearance,
    TimeFormatPreference TimeFormat,
    string? DisplayTimeZoneId);

/// <summary>
/// A notification the signed-in operator can see, with the state of every channel that actually
/// exists. Channels the delivery pipeline does not let a user control are reported as not editable
/// with a reason, rather than as a disabled toggle.
/// </summary>
public sealed record AccountNotificationPreference(
    string DefinitionId,
    string DisplayName,
    string? Description,
    AccountNotificationChannelState Platform,
    AccountNotificationChannelState Email);

/// <summary>State of one delivery channel for one notification.</summary>
public sealed record AccountNotificationChannelState(
    bool Enabled,
    bool Editable,
    string? NotEditableReason);

/// <summary>
/// Self-service account operations. Every method is scoped to a user id resolved from the
/// authentication context — no caller may pass an arbitrary user id on behalf of an operator.
/// </summary>
public interface IAccountProfileService
{
    ValueTask<AccountProfileSnapshot?> GetProfileAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>Updates the platform-owned display name. Throws <see cref="ArgumentException"/> when invalid.</summary>
    ValueTask<AccountProfileSnapshot> UpdateDisplayNameAsync(
        UserId userId,
        string? displayName,
        CancellationToken cancellationToken = default);

    ValueTask<AccountPreferencesSnapshot> GetPreferencesAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces all personal preferences. Unknown appearance/time-format values and unrecognized
    /// timezone identifiers are rejected rather than coerced.
    /// </summary>
    ValueTask<AccountPreferencesSnapshot> UpdatePreferencesAsync(
        UserId userId,
        AccountPreferencesUpdate update,
        CancellationToken cancellationToken = default);

    ValueTask<UserAvatar> SelectBuiltInAvatarAsync(
        UserId userId,
        string? optionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Normalizes and stores an untrusted upload, retiring any previously derived images for the user.
    /// </summary>
    ValueTask<AccountAvatarUploadResult> UploadAvatarAsync(
        UserId userId,
        AccountAvatarUpload upload,
        CancellationToken cancellationToken = default);

    /// <summary>Restores the neutral built-in avatar and deletes every derived image for the user.</summary>
    ValueTask<UserAvatar> ResetAvatarAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>Reads one rendition of the caller's own avatar. Returns null when there is none.</summary>
    ValueTask<ProfileImageContent?> GetOwnAvatarImageAsync(
        UserId userId,
        int size,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AccountNotificationPreference>> GetNotificationPreferencesAsync(
        UserId userId,
        CancellationToken cancellationToken = default);

    ValueTask<AccountNotificationPreference> SetNotificationEmailPreferenceAsync(
        UserId userId,
        string definitionId,
        bool emailEnabled,
        CancellationToken cancellationToken = default);
}

/// <summary>Requested preference values. Raw strings so the service can reject unknown enum members.</summary>
public sealed record AccountPreferencesUpdate(
    string? Appearance,
    string? TimeFormat,
    string? DisplayTimeZoneId);

/// <summary>An untrusted upload as received by the API.</summary>
public sealed record AccountAvatarUpload(string? FileName, string? DeclaredContentType, byte[] Content);

/// <summary>Result of an avatar upload attempt.</summary>
public sealed record AccountAvatarUploadResult(
    bool Succeeded,
    ProfileImageRejectionReason Reason,
    string? Message,
    UserAvatar Avatar)
{
    public static AccountAvatarUploadResult Rejected(
        ProfileImageRejectionReason reason,
        string message,
        UserAvatar currentAvatar) =>
        new(false, reason, message, currentAvatar);
}

/// <summary>Audit action names for operator account changes.</summary>
public static class AccountAuditActionNames
{
    public const string DisplayNameUpdated = "account.profile.display_name.updated";
    public const string AvatarUpdated = "account.profile.avatar.updated";
    public const string AvatarReset = "account.profile.avatar.reset";
    public const string AvatarUploadRejected = "account.profile.avatar.upload_rejected";
    public const string NotificationEmailOptOut = "account.notification.email.opted_out";
    public const string NotificationEmailOptIn = "account.notification.email.opted_in";

    /// <summary>Audit outcome bucket shared by account self-service records.</summary>
    public const string Outcome = "account";
}
