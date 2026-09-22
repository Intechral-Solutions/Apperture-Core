using System.Security.Cryptography;
using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Self-service account operations for the signed-in operator.
/// </summary>
/// <remarks>
/// Every method takes a <see cref="UserId"/> that callers must have resolved from the authentication
/// context. Nothing here accepts a user id from a request body or route, so there is no path by which
/// one operator can mutate another's profile through this service.
/// </remarks>
public sealed class AccountProfileService : IAccountProfileService
{
    /// <summary>
    /// Explains the in-app notification channel, which the delivery pipeline materializes from the
    /// resolved recipient set rather than from a per-user switch.
    /// </summary>
    internal const string PlatformChannelNotEditableReason =
        "In-app notifications are always delivered for notifications routed to you.";

    private readonly IUserRepository _users;
    private readonly IUserPreferencesRepository _preferences;
    private readonly IUserProfileImageStore _images;
    private readonly IProfileImageNormalizer _normalizer;
    private readonly IOperatorNotificationSubscriptionService _notificationPreferences;
    private readonly IOperatorNotificationToastPreferenceService _toastPreferences;
    private readonly IAuditService _audit;
    private readonly ILogger<AccountProfileService> _logger;

    public AccountProfileService(
        IUserRepository users,
        IUserPreferencesRepository preferences,
        IUserProfileImageStore images,
        IProfileImageNormalizer normalizer,
        IOperatorNotificationSubscriptionService notificationPreferences,
        IOperatorNotificationToastPreferenceService toastPreferences,
        IAuditService audit,
        ILogger<AccountProfileService> logger)
    {
        _users = users;
        _preferences = preferences;
        _images = images;
        _normalizer = normalizer;
        _notificationPreferences = notificationPreferences;
        _toastPreferences = toastPreferences;
        _audit = audit;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<AccountProfileSnapshot?> GetProfileAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        return user is null ? null : BuildSnapshot(user);
    }

    /// <inheritdoc />
    public async ValueTask<AccountProfileSnapshot> UpdateDisplayNameAsync(
        UserId userId,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        if (!DisplayNameRules.TryNormalize(displayName, out var normalized, out var error))
            throw new ArgumentException(error, nameof(displayName));

        var user = await RequireUserAsync(userId, cancellationToken).ConfigureAwait(false);
        EnsureMutable(user);
        if (string.Equals(user.DisplayName, normalized, StringComparison.Ordinal))
            return BuildSnapshot(user);

        var previous = user.DisplayName;
        var updated = user with { DisplayName = normalized };
        await _users.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        await AppendAuditAsync(
            userId,
            AccountAuditActionNames.DisplayNameUpdated,
            $"Display name changed from '{previous}' to '{normalized}'.",
            cancellationToken).ConfigureAwait(false);

        return BuildSnapshot(updated);
    }

    /// <inheritdoc />
    public async ValueTask<AccountPreferencesSnapshot> GetPreferencesAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId, cancellationToken).ConfigureAwait(false);
        var stored = await _preferences.GetAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? UserPreferences.Default;
        return new AccountPreferencesSnapshot(stored.Appearance, stored.TimeFormat, user.DisplayTimeZoneId);
    }

    /// <inheritdoc />
    public async ValueTask<AccountPreferencesSnapshot> UpdatePreferencesAsync(
        UserId userId,
        AccountPreferencesUpdate update,
        CancellationToken cancellationToken = default)
    {
        if (!UserPreferences.TryParseAppearance(update.Appearance, out var appearance))
            throw new ArgumentException(
                $"'{update.Appearance}' is not a supported appearance. Use System, Light, or Dark.",
                nameof(update));

        if (!UserPreferences.TryParseTimeFormat(update.TimeFormat, out var timeFormat))
            throw new ArgumentException(
                $"'{update.TimeFormat}' is not a supported time format. Use System, Hour12, or Hour24.",
                nameof(update));

        var timeZoneId = NormalizeTimeZoneOrThrow(update.DisplayTimeZoneId);

        var user = await RequireUserAsync(userId, cancellationToken).ConfigureAwait(false);
        await _preferences
            .UpsertAsync(userId, new UserPreferences(appearance, timeFormat), cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(user.DisplayTimeZoneId, timeZoneId, StringComparison.Ordinal))
        {
            await _users
                .UpdateAsync(user with { DisplayTimeZoneId = timeZoneId }, cancellationToken)
                .ConfigureAwait(false);
        }

        // Appearance, clock format, and display timezone are presentation-only and change nothing
        // about execution, so they are deliberately not written to the operational audit trail.
        return new AccountPreferencesSnapshot(appearance, timeFormat, timeZoneId);
    }

    /// <inheritdoc />
    public async ValueTask<UserAvatar> SelectBuiltInAvatarAsync(
        UserId userId,
        string? optionId,
        CancellationToken cancellationToken = default)
    {
        if (!BuiltInAvatarCatalog.IsKnownOptionId(optionId))
            throw new ArgumentException($"'{optionId}' is not a built-in avatar option.", nameof(optionId));

        var user = await RequireUserAsync(userId, cancellationToken).ConfigureAwait(false);
        EnsureMutable(user);
        var avatar = UserAvatar.ForBuiltIn(optionId!);
        await _users.UpdateAsync(user with { Avatar = avatar }, cancellationToken).ConfigureAwait(false);

        // Selecting artwork retires any photo the operator had uploaded, so the image does not linger.
        var retired = await _images.DeleteForUserAsync(userId.Value, keepImageKey: null, cancellationToken)
            .ConfigureAwait(false);
        if (retired > 0)
        {
            await AppendAuditAsync(
                userId,
                AccountAuditActionNames.AvatarReset,
                $"Selected built-in avatar '{avatar.BuiltInOptionId}'; {retired} stored profile image(s) deleted.",
                cancellationToken).ConfigureAwait(false);
        }

        return avatar;
    }

    /// <inheritdoc />
    public async ValueTask<AccountAvatarUploadResult> UploadAvatarAsync(
        UserId userId,
        AccountAvatarUpload upload,
        CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId, cancellationToken).ConfigureAwait(false);
        EnsureMutable(user);
        var current = user.EffectiveAvatar;

        // Cheap pre-filter on what the client claims. The decoder below is what actually decides.
        if (!ProfileImageLimits.IsAcceptedUploadContentType(upload.DeclaredContentType)
            || !ProfileImageLimits.IsAcceptedUploadExtension(upload.FileName))
        {
            await AppendAuditAsync(
                userId,
                AccountAuditActionNames.AvatarUploadRejected,
                "Upload rejected: unsupported file type.",
                cancellationToken).ConfigureAwait(false);
            return AccountAvatarUploadResult.Rejected(
                ProfileImageRejectionReason.UnsupportedDeclaredType,
                "Only JPEG, PNG, and WebP images are accepted.",
                current);
        }

        var normalized = _normalizer.Normalize(upload.Content);
        if (!normalized.Succeeded)
        {
            await AppendAuditAsync(
                userId,
                AccountAuditActionNames.AvatarUploadRejected,
                $"Upload rejected: {normalized.Reason}.",
                cancellationToken).ConfigureAwait(false);
            return AccountAvatarUploadResult.Rejected(
                normalized.Reason,
                normalized.Message ?? "That image could not be used.",
                current);
        }

        var imageKey = GenerateImageKey();
        await _images
            .SaveAsync(userId.Value, imageKey, normalized.Renditions, cancellationToken)
            .ConfigureAwait(false);

        var avatar = UserAvatar.ForCustomImage(imageKey);
        await _users.UpdateAsync(user with { Avatar = avatar }, cancellationToken).ConfigureAwait(false);

        // Point the user row at the new key first, then retire everything else. If cleanup fails the
        // account still renders correctly and only orphaned rows remain.
        var retired = await _images
            .DeleteForUserAsync(userId.Value, keepImageKey: imageKey, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Stored normalized profile image for user {UserId} ({Width}x{Height} {SourceFormat} source); retired {RetiredCount} previous image(s).",
            userId.Value,
            normalized.SourceWidth,
            normalized.SourceHeight,
            normalized.SourceFormat,
            retired);

        await AppendAuditAsync(
            userId,
            AccountAuditActionNames.AvatarUpdated,
            $"Custom profile image stored (key {imageKey}); {retired} previous image(s) deleted.",
            cancellationToken).ConfigureAwait(false);

        return new AccountAvatarUploadResult(true, ProfileImageRejectionReason.None, null, avatar);
    }

    /// <inheritdoc />
    public async ValueTask<UserAvatar> ResetAvatarAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId, cancellationToken).ConfigureAwait(false);
        EnsureMutable(user);
        await _users.UpdateAsync(user with { Avatar = UserAvatar.Default }, cancellationToken).ConfigureAwait(false);
        var retired = await _images
            .DeleteForUserAsync(userId.Value, keepImageKey: null, cancellationToken)
            .ConfigureAwait(false);

        await AppendAuditAsync(
            userId,
            AccountAuditActionNames.AvatarReset,
            $"Avatar reset to the platform default; {retired} stored profile image(s) deleted.",
            cancellationToken).ConfigureAwait(false);

        return UserAvatar.Default;
    }

    /// <inheritdoc />
    public async ValueTask<ProfileImageContent?> GetOwnAvatarImageAsync(
        UserId userId,
        int size,
        CancellationToken cancellationToken = default)
    {
        if (!ProfileImageLimits.IsSupportedRenditionSize(size))
            return null;

        var user = await _users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        var avatar = user?.EffectiveAvatar;
        if (avatar is not { Kind: UserAvatarKind.Custom, ImageKey: { Length: > 0 } key })
            return null;

        // Scoped by user id, so the key alone is never sufficient to read another account's image.
        return await _images.GetAsync(userId.Value, key, size, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AccountNotificationPreference>> GetNotificationPreferencesAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        var emailRows = await _notificationPreferences
            .ListEmailPreferencesAsync(userId.Value, cancellationToken)
            .ConfigureAwait(false);
        var toastRows = await _toastPreferences
            .ListToastPreferencesAsync(userId.Value, cancellationToken)
            .ConfigureAwait(false);

        // Both lists iterate the same self-subscribable definition set (Design Decision 5), so they
        // should always line up 1:1 by DefinitionId -- but look up by id defensively rather than zip
        // positionally, in case of any future drift between the two services.
        var toastByDefinitionId = toastRows.ToDictionary(t => t.DefinitionId, StringComparer.Ordinal);

        return emailRows
            .Select(email => ToPreference(
                email,
                toastByDefinitionId.TryGetValue(email.DefinitionId, out var toast) ? toast : null))
            .ToList();
    }

    /// <inheritdoc />
    public async ValueTask<AccountNotificationPreference> SetNotificationEmailPreferenceAsync(
        UserId userId,
        string definitionId,
        bool emailEnabled,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            throw new ArgumentException("A notification definition id is required.", nameof(definitionId));

        var trimmedDefinitionId = definitionId.Trim();
        var updated = await _notificationPreferences
            .SetEmailEnabledAsync(userId.Value, trimmedDefinitionId, emailEnabled, cancellationToken)
            .ConfigureAwait(false);

        // Opting out of a channel changes who gets told about operational problems, so unlike the
        // presentation preferences above this is worth an audit record.
        await AppendAuditAsync(
            userId,
            emailEnabled
                ? AccountAuditActionNames.NotificationEmailOptIn
                : AccountAuditActionNames.NotificationEmailOptOut,
            $"Email delivery for '{updated.DisplayName}' ({updated.DefinitionId}) set to {(emailEnabled ? "on" : "off")}.",
            cancellationToken).ConfigureAwait(false);

        var toast = await _toastPreferences
            .ListToastPreferencesAsync(userId.Value, cancellationToken)
            .ConfigureAwait(false);
        var toastPreference = toast.FirstOrDefault(t => string.Equals(t.DefinitionId, trimmedDefinitionId, StringComparison.Ordinal));

        return ToPreference(updated, toastPreference);
    }

    /// <inheritdoc />
    public async ValueTask<AccountNotificationPreference> SetNotificationToastPreferenceAsync(
        UserId userId,
        string definitionId,
        bool toastEnabled,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            throw new ArgumentException("A notification definition id is required.", nameof(definitionId));

        var trimmedDefinitionId = definitionId.Trim();
        var updated = await _toastPreferences
            .SetToastEnabledAsync(userId.Value, trimmedDefinitionId, toastEnabled, cancellationToken)
            .ConfigureAwait(false);

        // Opting out of a channel changes who gets told about operational problems, so unlike the
        // presentation preferences above this is worth an audit record.
        await AppendAuditAsync(
            userId,
            toastEnabled
                ? AccountAuditActionNames.NotificationToastOptIn
                : AccountAuditActionNames.NotificationToastOptOut,
            $"Toast delivery for '{updated.DisplayName}' ({updated.DefinitionId}) set to {(toastEnabled ? "on" : "off")}.",
            cancellationToken).ConfigureAwait(false);

        var email = await _notificationPreferences
            .ListEmailPreferencesAsync(userId.Value, cancellationToken)
            .ConfigureAwait(false);
        var emailPreference = email.FirstOrDefault(e => string.Equals(e.DefinitionId, trimmedDefinitionId, StringComparison.Ordinal));

        // If the toast service accepted the definition, the email service (which iterates the exact
        // same self-subscribable definition set) must have a matching row too.
        if (emailPreference is null)
            throw new KeyNotFoundException($"Notification definition '{trimmedDefinitionId}' is not available for operator email preferences.");

        return ToPreference(emailPreference, updated);
    }

    private static AccountNotificationPreference ToPreference(
        OperatorEmailSubscriptionPreference email,
        OperatorToastPreference? toast) =>
        new(
            email.DefinitionId,
            email.DisplayName,
            email.Description,
            // The in-app inbox is materialized from the resolved recipient set, so there is no user
            // switch behind it. Reporting it as not editable keeps the UI honest instead of showing
            // a control that would silently do nothing.
            new AccountNotificationChannelState(
                Enabled: true,
                Editable: false,
                NotEditableReason: PlatformChannelNotEditableReason),
            new AccountNotificationChannelState(
                Enabled: email.EmailEnabled,
                Editable: true,
                NotEditableReason: null),
            // Toast editability never depends on email's state -- it is a wholly independent
            // preference-store channel with no subscription/route concept behind it.
            new AccountNotificationChannelState(
                Enabled: toast?.ToastEnabled ?? false,
                Editable: true,
                NotEditableReason: null));

    private static AccountProfileSnapshot BuildSnapshot(User user) =>
        new(
            user.Id.Value,
            user.DisplayName,
            DisplayNameEditable: !EmergencyAccessIdentity.IsSystemManaged(user),
            user.EffectiveAvatar,
            user.DisplayTimeZoneId,
            user.IdentityLinks
                .Select(l => new AccountIdentityLinkSnapshot(
                    l.Source.ToString(),
                    AccountMeSnapshotBuilder.FormatIdentityLinkLabel(l.Source, l.ExternalId),
                    l.ExternalId,
                    l.LastAuthenticatedAt))
                .ToList());

    private static string? NormalizeTimeZoneOrThrow(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return null;

        var trimmed = timeZoneId.Trim();
        if (trimmed.Length > 128 || !TryFindTimeZone(trimmed))
            throw new ArgumentException($"'{trimmed}' is not a recognized timezone identifier.", nameof(timeZoneId));

        return trimmed;
    }

    private static bool TryFindTimeZone(string timeZoneId)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <summary>
    /// Produces the storage key. It is random rather than derived from the user or file name, so keys
    /// are unguessable and a replacement image can never collide with the one it supersedes.
    /// </summary>
    private static string GenerateImageKey() =>
        "avt_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private async ValueTask<User> RequireUserAsync(UserId userId, CancellationToken cancellationToken) =>
        await _users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false)
        ?? throw new KeyNotFoundException($"User '{userId.Value}' was not found.");

    private static void EnsureMutable(User user)
    {
        if (!EmergencyAccessIdentity.IsSystemManaged(user))
            return;
        throw new EmergencyAccessException(
            EmergencyAccessErrorCodes.UnsafeOperation,
            "Recovery Access is system-managed. Display name and avatar cannot be changed here.");
    }

    private async ValueTask AppendAuditAsync(
        UserId userId,
        string action,
        string? details,
        CancellationToken cancellationToken)
    {
        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId(userId.Value),
            action,
            Resource: null,
            Outcome: AccountAuditActionNames.Outcome,
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            details);
        await _audit.AppendAsync(record, cancellationToken).ConfigureAwait(false);
    }
}
