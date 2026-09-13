namespace Apperture.Core.Application.Notifications.Delivery;

public sealed record EmailNotificationSendRequest(
    string Recipient,
    string Subject,
    string PlainTextBody,
    string? HtmlBody = null);

public sealed record EmailNotificationSendResult(
    bool Succeeded,
    string? ProviderMessageId,
    string? FailureSummary);

public interface IEmailNotificationChannel
{
    ValueTask<EmailNotificationSendResult> SendAsync(
        EmailNotificationSendRequest request,
        CancellationToken cancellationToken = default);
}
