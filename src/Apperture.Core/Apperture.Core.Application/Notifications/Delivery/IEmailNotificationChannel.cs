namespace Apperture.Core.Application.Notifications.Delivery;

public sealed record EmailNotificationSendRequest(
    string Recipient,
    string Subject,
    string PlainTextBody,
    string? HtmlBody = null);

/// <summary>What the platform observed about one send. Acceptance by the server is not delivery to a mailbox.</summary>
public enum EmailSendDisposition
{
    /// <summary>The channel did not say; <see cref="EmailNotificationSendResult.Observed"/> derives it from <see cref="EmailNotificationSendResult.Succeeded"/>.</summary>
    Unspecified = 0,

    /// <summary>No call was made: the channel is not configured or switched off.</summary>
    NotAttempted,

    /// <summary>The server accepted the message. It may still not reach the mailbox.</summary>
    Accepted,

    /// <summary>The server refused a recipient or the message.</summary>
    Rejected,

    /// <summary>Any other transport error after the attempt began.</summary>
    Failed,

    /// <summary>
    /// No answer arrived: the call was cancelled, timed out, or the connection broke after the session began. The message may or may
    /// not have left, so this is an unknown outcome and never a failure.
    /// </summary>
    NoAnswer,
}

public sealed record EmailNotificationSendResult(
    bool Succeeded,
    string? ProviderMessageId,
    string? FailureSummary,
    EmailSendDisposition Disposition = EmailSendDisposition.Unspecified)
{
    /// <summary>The disposition, derived from <see cref="Succeeded"/> when the channel did not state one.</summary>
    public EmailSendDisposition Observed =>
        Disposition != EmailSendDisposition.Unspecified
            ? Disposition
            : Succeeded ? EmailSendDisposition.Accepted : EmailSendDisposition.Failed;
}

public interface IEmailNotificationChannel
{
    ValueTask<EmailNotificationSendResult> SendAsync(
        EmailNotificationSendRequest request,
        CancellationToken cancellationToken = default);
}
