using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Audit;
using Apperture.Core.Domain.Identity;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Sends a one-time account link after the request it belongs to committed, once, and records the send as its own W3
/// <c>external_attempt</c> row (AUD-1.2 D8). Never inside a transaction and never repeated: a transaction rerun cannot reach
/// it, and a failed send is reported to the caller, not retried.
/// </summary>
/// <remarks>
/// A send that throws is reported as not delivered with <see cref="DeliveryUnknown"/>, never as a failure of the committed
/// change, so the caller's response still says the change happened. The transport's own failure text is returned to the
/// caller as before but never recorded.
/// </remarks>
internal static class AccountSecurityMailDelivery
{
    /// <summary>The send threw: the message may or may not have left.</summary>
    public const string DeliveryUnknown = "account.security_mail.delivery_unknown";

    /// <summary>
    /// The request committed but this request no longer holds its link (it cannot be shown to belong to the committed row), so
    /// nothing was sent. Sending the request again issues a new link and revokes the unsent one.
    /// </summary>
    public const string LinkUnavailable = "account.security_mail.link_unavailable";

    public const string SetupInvitation = "setup_invitation";
    public const string ContactVerification = "contact_verification";
    public const string PasswordReset = "password_reset";

    public static async ValueTask<AccountSecurityMailResult> SendAsync(
        IAccountSecurityMailSender mail,
        IAuditEventWriter events,
        AuditActor actor,
        UserId user,
        string requestId,
        string purpose,
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        AccountSecurityMailResult result;
        string outcome;
        string? failure;
        try
        {
            result = await mail.SendAsync(recipient, subject, body, cancellationToken).ConfigureAwait(false);
            outcome = result.Succeeded ? AuditOutcomeCodes.Succeeded : AuditOutcomeCodes.Failed;
            failure = result.Succeeded
                ? null
                : string.Equals(result.FailureSummary, AccountSecurityErrorCodes.SmtpNotConfigured, StringComparison.Ordinal)
                    ? "not_configured"
                    : "send_failed";
        }
        catch (Exception)
        {
            // Includes cancellation: the change already committed, so the caller still gets its result.
            result = new AccountSecurityMailResult(false, DeliveryUnknown);
            outcome = AuditOutcomeCodes.Unknown;
            failure = "error";
        }

        await events.TryWriteAsync(
            AuditEvents.SecurityMailDeliveryAttempted(user, requestId, purpose, outcome, failure),
            actor,
            CancellationToken.None).ConfigureAwait(false);
        return result;
    }
}
