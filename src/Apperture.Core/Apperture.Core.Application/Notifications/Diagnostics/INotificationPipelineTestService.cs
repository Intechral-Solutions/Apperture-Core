namespace Apperture.Core.Application.Notifications.Diagnostics;

/// <summary>
/// Admin-only diagnostics for SMTP transport and platform notification pipeline verification (S12).
/// Does not replace natural expected-run-missing observation; exercises the canonical pipeline on demand.
/// </summary>
public interface INotificationPipelineTestService
{
    ValueTask<NotificationSmtpTestResult> SendSmtpTestEmailAsync(
        NotificationSmtpTestRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<PlatformNotificationPipelineTestResult> RunPlatformPipelineTestAsync(
        PlatformNotificationPipelineTestRequest request,
        CancellationToken cancellationToken = default);
}
