using System.Security.Cryptography;
using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Self-service account operations for the signed-in operator.
/// </summary>
/// <remarks>
/// <para>Every method takes a <see cref="UserId"/> that callers must have resolved from the authentication
/// context. Nothing here accepts a user id from a request body or route, so there is no path by which
/// one operator can mutate another's profile through this service.</para>
/// <para>AUD-1.2: each audited change is one audited unit of work of the signed-in user: the checks, the change and its row
/// commit together, and a request that changes nothing writes no row while returning the same response. A refused upload
/// changes nothing and writes a W3 row.</para>
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
    private readonly ILogger<AccountProfileService> _logger;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;
    private readonly IAuditEventWriter? _auditEvents;

    public AccountProfileService(
        IUserRepository users,
        IUserPreferencesRepository preferences,
        IUserProfileImageStore images,
        IProfileImageNormalizer normalizer,
        IOperatorNotificationSubscriptionService notificationPreferences,
        IOperatorNotificationToastPreferenceService toastPreferences,
        ILogger<AccountProfileService> logger,
        IAuditedUnitOfWork? auditedUnitOfWork = null,
        IAuditEventWriter? auditEvents = null)
    {
        _users = users;
        _preferences = preferences;
        _images = images;
        _normalizer = normalizer;
        _notificationPreferences = notificationPreferences;
        _toastPreferences = toastPreferences;
        _logger = logger;
        _auditedUnitOfWork = auditedUnitOfWork;
        _auditEvents = auditEvents;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("Profile changes require the audited unit of work.");

    private IAuditEventWriter Events =>
        _auditEvents ?? throw new InvalidOperationException("Profile changes require the audit event writer.");

    private static AuditedOperation Operation(string primaryAction, params string[] alternates) =>
        new(primaryAction, AuditActor.CurrentPrincipal, alternates.Length == 0 ? null : alternates);

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

        var updated = await Unit.ExecuteAsync(
            Operation(AuditActionNames.DisplayNameUpdated),
            async (transaction, ct) =>
            {
                var user = await RequireUserAsync(userId, ct).ConfigureAwait(false);
                EnsureMutable(user);
                if (string.Equals(user.DisplayName, normalized, StringComparison.Ordinal))
                {
                    transaction.DeclareNoOp();
                    return user;
                }

                var changed = user with { DisplayName = normalized };
                await _users.UpdateAsync(changed, ct).ConfigureAwait(false);
                await transaction.StageAsync(AuditEvents.DisplayNameUpdated(userId, user.DisplayName, normalized), ct).ConfigureAwait(false);
                return changed;
            },
            async (_, ct) => await RequireUserAsync(userId, ct).ConfigureAwait(false),
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

        var avatar = UserAvatar.ForBuiltIn(optionId!);

        // AUD-1.2: the row is written whenever the selection changed something (the avatar, or stored images deleted), since
        // a change and its row commit together; the earlier code recorded only image deletion. Re-selecting the current
        // artwork with no stored image changes nothing and writes no row.
        return await Unit.ExecuteAsync(
            Operation(AuditActionNames.AvatarReset),
            async (transaction, ct) =>
            {
                var user = await RequireUserAsync(userId, ct).ConfigureAwait(false);
                EnsureMutable(user);
                var avatarChanged = user.Avatar != avatar;
                if (avatarChanged)
                    await _users.UpdateAsync(user with { Avatar = avatar }, ct).ConfigureAwait(false);

                // Selecting artwork retires any photo the operator had uploaded, so the image does not linger.
                var retired = await _images.DeleteForUserAsync(userId.Value, keepImageKey: null, ct).ConfigureAwait(false);
                if (avatarChanged || retired > 0)
                    await transaction.StageAsync(AuditEvents.AvatarReset(userId, builtIn: true, retired), ct).ConfigureAwait(false);
                else
                    transaction.DeclareNoOp();
                return avatar;
            },
            (_, _) => Task.FromResult(avatar),
            cancellationToken).ConfigureAwait(false);
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

        // Cheap pre-filter on what the client claims. The decoder below is what actually decides. A refusal changes nothing,
        // so its row is a W3 attempt and its failure never changes the response.
        if (!ProfileImageLimits.IsAcceptedUploadContentType(upload.DeclaredContentType)
            || !ProfileImageLimits.IsAcceptedUploadExtension(upload.FileName))
        {
            await RecordUploadRejectedAsync(userId, ProfileImageRejectionReason.UnsupportedDeclaredType).ConfigureAwait(false);
            return AccountAvatarUploadResult.Rejected(
                ProfileImageRejectionReason.UnsupportedDeclaredType,
                "Only JPEG, PNG, and WebP images are accepted.",
                current);
        }

        var normalized = _normalizer.Normalize(upload.Content);
        if (!normalized.Succeeded)
        {
            await RecordUploadRejectedAsync(userId, normalized.Reason).ConfigureAwait(false);
            return AccountAvatarUploadResult.Rejected(
                normalized.Reason,
                normalized.Message ?? "That image could not be used.",
                current);
        }

        // Chosen once, so a rerun stores the same key.
        var imageKey = GenerateImageKey();
        var avatar = UserAvatar.ForCustomImage(imageKey);
        var retired = await Unit.ExecuteAsync(
            Operation(AuditActionNames.AvatarUpdated),
            async (transaction, ct) =>
            {
                var target = await RequireUserAsync(userId, ct).ConfigureAwait(false);
                EnsureMutable(target);
                await _images.SaveAsync(userId.Value, imageKey, normalized.Renditions, ct).ConfigureAwait(false);
                await _users.UpdateAsync(target with { Avatar = avatar }, ct).ConfigureAwait(false);

                // The new image, the user row pointing at it and the retirement of every other image commit together.
                var deleted = await _images.DeleteForUserAsync(userId.Value, keepImageKey: imageKey, ct).ConfigureAwait(false);
                await transaction.StageAsync(AuditEvents.AvatarUpdated(userId, imageKey, deleted), ct).ConfigureAwait(false);
                return deleted;
            },
            (committed, _) => Task.FromResult(ImagesDeleted(committed)),
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Stored normalized profile image for user {UserId} ({Width}x{Height} {SourceFormat} source); retired {RetiredCount} previous image(s).",
            userId.Value,
            normalized.SourceWidth,
            normalized.SourceHeight,
            normalized.SourceFormat,
            retired);

        return new AccountAvatarUploadResult(true, ProfileImageRejectionReason.None, null, avatar);
    }

    /// <inheritdoc />
    public async ValueTask<UserAvatar> ResetAvatarAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        return await Unit.ExecuteAsync(
            Operation(AuditActionNames.AvatarReset),
            async (transaction, ct) =>
            {
                var user = await RequireUserAsync(userId, ct).ConfigureAwait(false);
                EnsureMutable(user);
                var avatarChanged = user.EffectiveAvatar != UserAvatar.Default;
                if (avatarChanged)
                    await _users.UpdateAsync(user with { Avatar = UserAvatar.Default }, ct).ConfigureAwait(false);
                var retired = await _images.DeleteForUserAsync(userId.Value, keepImageKey: null, ct).ConfigureAwait(false);

                // Already the default with nothing stored: the same response, and no row (D7).
                if (avatarChanged || retired > 0)
                    await transaction.StageAsync(AuditEvents.AvatarReset(userId, builtIn: false, retired), ct).ConfigureAwait(false);
                else
                    transaction.DeclareNoOp();
                return UserAvatar.Default;
            },
            (_, _) => Task.FromResult(UserAvatar.Default),
            cancellationToken).ConfigureAwait(false);
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

        // Opting out of a channel changes who gets told about operational problems, so unlike the
        // presentation preferences above this is worth an audit record. Setting the current value writes nothing and no row.
        var updated = await Unit.ExecuteAsync(
            Operation(emailEnabled ? AuditActionNames.NotificationEmailOptedIn : AuditActionNames.NotificationEmailOptedOut),
            async (transaction, ct) =>
            {
                var update = await _notificationPreferences
                    .UpdateEmailEnabledAsync(userId.Value, trimmedDefinitionId, emailEnabled, ct)
                    .ConfigureAwait(false);
                await StageOrDeclareNoOpAsync(transaction, update.Changed, userId, update.PreferenceId, trimmedDefinitionId, "email", emailEnabled, ct)
                    .ConfigureAwait(false);
                return update.Preference;
            },
            async (_, ct) => await EmailPreferenceAsync(userId, trimmedDefinitionId, ct).ConfigureAwait(false),
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

        // Opting out of a channel changes who gets told about operational problems, so unlike the
        // presentation preferences above this is worth an audit record. Setting the current value writes nothing and no row.
        var updated = await Unit.ExecuteAsync(
            Operation(toastEnabled ? AuditActionNames.NotificationToastOptedIn : AuditActionNames.NotificationToastOptedOut),
            async (transaction, ct) =>
            {
                var update = await _toastPreferences
                    .UpdateToastEnabledAsync(userId.Value, trimmedDefinitionId, toastEnabled, ct)
                    .ConfigureAwait(false);
                await StageOrDeclareNoOpAsync(transaction, update.Changed, userId, update.PreferenceId, trimmedDefinitionId, "toast", toastEnabled, ct)
                    .ConfigureAwait(false);
                return update.Preference;
            },
            async (_, ct) => (await _toastPreferences.ListToastPreferencesAsync(userId.Value, ct).ConfigureAwait(false))
                .First(t => string.Equals(t.DefinitionId, trimmedDefinitionId, StringComparison.Ordinal)),
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

    private static async ValueTask StageOrDeclareNoOpAsync(
        IAuditedTransaction transaction,
        bool changed,
        UserId userId,
        string preferenceId,
        string definitionId,
        string channel,
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (!changed)
        {
            transaction.DeclareNoOp();
            return;
        }

        await transaction.StageAsync(
            AuditEvents.NotificationPreferenceChanged(userId, preferenceId, definitionId, channel, enabled),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<OperatorEmailSubscriptionPreference> EmailPreferenceAsync(
        UserId userId,
        string definitionId,
        CancellationToken cancellationToken) =>
        (await _notificationPreferences.ListEmailPreferencesAsync(userId.Value, cancellationToken).ConfigureAwait(false))
            .First(e => string.Equals(e.DefinitionId, definitionId, StringComparison.Ordinal));

    /// <summary>The committed upload row's count of retired images (it is logged only).</summary>
    private static int ImagesDeleted(AuditCommittedOperation committed)
    {
        if (string.IsNullOrEmpty(committed.Details))
            return 0;
        using var document = System.Text.Json.JsonDocument.Parse(committed.Details);
        return document.RootElement.TryGetProperty("imagesDeleted", out var value) && value.TryGetInt32(out var count) ? count : 0;
    }

    private ValueTask<bool> RecordUploadRejectedAsync(UserId userId, ProfileImageRejectionReason reason) =>
        new(Events.TryWriteAsync(
            AuditEvents.AvatarUploadRejected(userId, ReasonCode(reason)),
            AuditActor.CurrentPrincipal,
            CancellationToken.None));

    /// <summary>The recorded spelling of a rejection reason (snake_case of the enum name).</summary>
    internal static string ReasonCode(ProfileImageRejectionReason reason) =>
        string.Concat(reason.ToString().Select((c, i) => char.IsUpper(c) ? (i > 0 ? "_" : "") + char.ToLowerInvariant(c) : c.ToString()));
}
