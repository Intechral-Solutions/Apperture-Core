using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Audit;

// AUD-1.2: the typed producers of the account-service actions. A one-time request is named by its row id (requestId); the
// link's plaintext, its hash and the contact address are never recorded.
public static partial class AuditEvents
{
    /// <summary>A setup invitation was issued to a user for the first time.</summary>
    public static AuditEvent InvitationSent(UserId user, string requestId) =>
        Event(AuditActionNames.InvitationSent, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value), ("requestId", requestId));

    /// <summary>A setup invitation was issued again; the earlier one was revoked in the same transaction.</summary>
    public static AuditEvent InvitationResent(UserId user, string requestId) =>
        Event(AuditActionNames.InvitationResent, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value), ("requestId", requestId));

    /// <summary>A setup invitation was used.</summary>
    public static AuditEvent InvitationAccepted(UserId user, string requestId) =>
        Event(AuditActionNames.InvitationAccepted, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value), ("requestId", requestId));

    /// <summary>A user chose a first local password through a setup invitation. The password is never recorded.</summary>
    public static AuditEvent PasswordSetupCompleted(UserId user) =>
        Event(AuditActionNames.PasswordSetupCompleted, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value));

    /// <summary>
    /// A contact email verification link was issued: <paramref name="changing"/> when the user already has a verified address
    /// it would replace.
    /// </summary>
    public static AuditEvent ContactVerificationRequested(UserId user, string requestId, bool changing, bool resent) =>
        Event(
            changing ? AuditActionNames.EmailChangeRequested : AuditActionNames.EmailVerificationRequested,
            Target(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Requested,
            ("userId", user.Value),
            ("requestId", requestId),
            ("resent", resent));

    /// <summary>A contact email address became verified for a user that had none.</summary>
    public static AuditEvent EmailVerified(UserId user, bool viaSetupInvitation) =>
        Event(AuditActionNames.EmailVerified, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value), ("via", viaSetupInvitation ? "setup_invitation" : "verification_link"));

    /// <summary>A user's verified contact email address was replaced by a newly verified one.</summary>
    public static AuditEvent EmailChanged(UserId user) =>
        Event(AuditActionNames.EmailChanged, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value));

    /// <summary>A password reset link was issued for an eligible account.</summary>
    public static AuditEvent PasswordResetRequested(UserId user, string requestId) =>
        Event(AuditActionNames.PasswordResetRequested, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Requested,
            ("userId", user.Value), ("requestId", requestId));

    /// <summary>A forgotten-password request matched no eligible account. Neither the identifier nor any account is recorded.</summary>
    public static AuditEvent PasswordResetRequestSuppressed() =>
        Event(AuditActionNames.PasswordResetRequestSuppressed, null, AuditOutcomeCodes.Rejected);

    /// <summary>A local password was replaced through a reset link. The password is never recorded.</summary>
    public static AuditEvent PasswordResetCompleted(UserId user) =>
        Event(AuditActionNames.PasswordResetCompleted, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value));

    /// <summary>
    /// A one-time account link was handed to the mail transport after its request committed. <paramref name="outcome"/> is the
    /// transport's answer, or <see cref="AuditOutcomeCodes.Unknown"/> when the send threw and may or may not have left.
    /// <paramref name="failure"/> is <c>not_configured</c>, <c>send_failed</c> or <c>error</c>; transport text is never recorded.
    /// </summary>
    public static AuditEvent SecurityMailDeliveryAttempted(UserId user, string requestId, string purpose, string outcome, string? failure) =>
        Event(AuditActionNames.SecurityMailDeliveryAttempted, Target(AuditTargetTypes.User, user.Value), outcome,
            ("userId", user.Value), ("requestId", requestId), ("purpose", purpose), ("failure", failure));

    /// <summary>A user changed their display name.</summary>
    public static AuditEvent DisplayNameUpdated(UserId user, string previousDisplayName, string newDisplayName) =>
        Event(AuditActionNames.DisplayNameUpdated, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value), ("previousDisplayName", previousDisplayName), ("newDisplayName", newDisplayName));

    /// <summary>A user's avatar returned to the default or to a built-in option, deleting stored images.</summary>
    public static AuditEvent AvatarReset(UserId user, bool builtIn, int imagesDeleted) =>
        Event(AuditActionNames.AvatarReset, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value), ("avatar", builtIn ? "built_in" : "default"), ("imagesDeleted", imagesDeleted));

    /// <summary>A user stored a new profile image.</summary>
    public static AuditEvent AvatarUpdated(UserId user, string imageKey, int imagesDeleted) =>
        Event(AuditActionNames.AvatarUpdated, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value), ("imageKey", imageKey), ("imagesDeleted", imagesDeleted));

    /// <summary>An uploaded profile image was refused. <paramref name="reason"/> is the snake_case rejection reason.</summary>
    public static AuditEvent AvatarUploadRejected(UserId user, string reason) =>
        Event(AuditActionNames.AvatarUploadRejected, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Rejected,
            ("userId", user.Value), ("reason", reason));

    /// <summary>
    /// A user changed their own preference for a notification channel. The target is the persisted preference row
    /// (<paramref name="preferenceId"/>): the one record every change of that user, definition and channel writes.
    /// </summary>
    public static AuditEvent NotificationPreferenceChanged(UserId user, string preferenceId, string definitionId, string channel, bool enabled) =>
        Event(
            (channel, enabled) switch
            {
                ("email", true) => AuditActionNames.NotificationEmailOptedIn,
                ("email", false) => AuditActionNames.NotificationEmailOptedOut,
                ("toast", true) => AuditActionNames.NotificationToastOptedIn,
                ("toast", false) => AuditActionNames.NotificationToastOptedOut,
                _ => throw new ArgumentException($"'{channel}' is not a notification preference channel.", nameof(channel)),
            },
            Target(AuditTargetTypes.NotificationSubscription, preferenceId),
            AuditOutcomeCodes.Succeeded,
            ("userId", user.Value),
            ("definitionId", definitionId),
            ("channel", channel));
}
