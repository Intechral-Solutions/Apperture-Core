using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Accounts;

public sealed record AccountContactSnapshot(
    string? EmailAddress,
    string Status,
    DateTimeOffset? VerifiedAtUtc,
    string? PendingEmailAddress,
    string? SuggestedEntraEmail,
    bool CanManage,
    bool EmergencyAccessAccount,
    bool HasEnabledLocalPassword = false);

public sealed record AccountInvitationStatus(
    bool Pending,
    bool Expired,
    DateTimeOffset? ExpiresAtUtc,
    string? DestinationEmail,
    bool LastDeliveryFailed,
    string? LastDeliveryFailure);

public sealed record AccountContactMutationResult(
    AccountContactSnapshot Contact,
    bool DeliverySucceeded,
    string? DeliveryFailure);

public sealed record AccountInvitationResult(
    UserId UserId,
    bool DeliverySucceeded,
    string? DeliveryFailure,
    DateTimeOffset ExpiresAtUtc);

public interface IAccountContactService
{
    ValueTask<AccountContactSnapshot> GetAsync(UserId userId, CancellationToken cancellationToken = default);

    ValueTask<AccountContactMutationResult> RequestAsync(
        UserId userId,
        string email,
        string? currentPassword,
        CancellationToken cancellationToken = default);

    ValueTask<AccountContactMutationResult> ResendAsync(UserId userId, CancellationToken cancellationToken = default);

    ValueTask VerifyAsync(string token, CancellationToken cancellationToken = default);
}

/// <summary>
/// A setup invitation staged in an audited transaction, held in memory until the transaction commits and its link is sent.
/// Never recorded, logged or reconstructed: it carries the link's plaintext.
/// </summary>
public sealed record PreparedSetupInvitation(UserId UserId, string Destination, IssuedAccountActionToken Token)
{
    /// <summary>Never prints the link or the address.</summary>
    public override string ToString() => $"PreparedSetupInvitation {{ UserId = {UserId.Value}, RequestId = {Token.Id} }}";
}

public interface IAccountInvitationService
{
    /// <summary>
    /// Issues (or reissues) a setup invitation as one audited operation of the current principal, then sends its link once.
    /// <paramref name="contactEmail"/> falls back to the user's pending, then verified, contact address.
    /// </summary>
    ValueTask<AccountInvitationResult> SendSetupInvitationAsync(
        UserId userId,
        string? contactEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inside <paramref name="transaction"/>: checks the user can be invited, records the pending address, issues
    /// <paramref name="token"/> (revoking earlier invitations) and stages the sent or resent row. Sends nothing.
    /// </summary>
    ValueTask<PreparedSetupInvitation> StageSetupInvitationAsync(
        Apperture.Core.Application.Audit.IAuditedTransaction transaction,
        UserId userId,
        string? contactEmail,
        PreparedAccountActionToken token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// After the invitation's transaction committed: sends its link once and records the send. A failed or unknown send is
    /// reported in the result; it never undoes or contradicts the committed invitation.
    /// </summary>
    ValueTask<AccountInvitationResult> DeliverSetupInvitationAsync(
        PreparedSetupInvitation invitation,
        Apperture.Core.Application.Audit.AuditActor actor,
        CancellationToken cancellationToken = default);

    ValueTask CompletePasswordSetupAsync(string token, string newPassword, CancellationToken cancellationToken = default);

    ValueTask<AccountInvitationStatus?> GetStatusAsync(UserId userId, CancellationToken cancellationToken = default);
}

public interface IForgottenPasswordService
{
    /// <summary>Always returns a generic acknowledgement. Never reveals whether an account exists.</summary>
    ValueTask RequestAsync(string identifier, CancellationToken cancellationToken = default);

    ValueTask CompleteAsync(string token, string newPassword, CancellationToken cancellationToken = default);
}

public interface IAccountSecurityMailSender
{
    ValueTask<AccountSecurityMailResult> SendAsync(
        string recipient,
        string subject,
        string plainTextBody,
        CancellationToken cancellationToken = default);
}

public sealed record AccountSecurityMailResult(bool Succeeded, string? FailureSummary);

public static class AccountSecurityAuditActionNames
{
    public const string EmailVerificationRequested = "account.email.verification_requested";
    public const string EmailVerified = "account.email.verified";
    public const string EmailChangeRequested = "account.email.change_requested";
    public const string EmailChanged = "account.email.changed";
    public const string PasswordSetupRequested = "credential.password.setup_requested";
    public const string PasswordSetupCompleted = "credential.password.setup_completed";
    public const string PasswordChanged = "credential.password.changed";
    public const string PasswordResetRequested = "credential.password.reset_requested";
    public const string PasswordResetCompleted = "credential.password.reset_completed";
    public const string InvitationSent = "account.invitation.sent";
    public const string InvitationResent = "account.invitation.resent";
    public const string InvitationAccepted = "account.invitation.accepted";
    public const string DeliveryFailed = "account.security_mail.delivery_failed";
    public const string Outcome = "account";
}

public static class AccountSecurityErrorCodes
{
    public const string InvalidEmail = "account.email.invalid";
    public const string SystemManaged = "account.email.system_managed";
    public const string CurrentPasswordRequired = "account.email.current_password_required";
    public const string CurrentPasswordInvalid = "account.email.current_password_invalid";
    public const string StrongAuthenticationRequired = "account.email.strong_authentication_required";
    public const string TokenInvalid = "account.action_token.invalid";
    public const string EmailInUse = "account.email.in_use";
    public const string InvitationRequired = "account.invitation.required";
    public const string PasswordRequired = "account.password.required";
    public const string SmtpNotConfigured = "account.security_mail.not_configured";
}

public sealed class AccountSecurityException : Exception
{
    public AccountSecurityException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}
