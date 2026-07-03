using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Notifications.Delivery;

public sealed class SmtpAttentionNotificationSenderTests
{
    [Fact]
    public async Task SendEmailAsync_uses_email_notification_options_and_passes_html_body()
    {
        var options = Options.Create(new EmailNotificationOptions
        {
            SmtpHost = "",
            FromAddress = "",
        });
        var credentials = new Mock<IEmailSmtpCredentialResolver>();
        credentials.Setup(x => x.ResolvePasswordAsync(It.IsAny<EmailNotificationOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var sender = new SmtpAttentionNotificationSender(
            options,
            credentials.Object,
            NullLogger<SmtpAttentionNotificationSender>.Instance);

        var result = await sender.SendEmailAsync(
            new AttentionNotificationSendRequest("a@test.com", "Subject", "Plain", "<p>Html</p>"),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureSummary.Should().Be(Apperture.Core.Application.Integration.AttentionNotificationSuppressionReasons.SmtpNotConfigured);
        credentials.Verify(
            x => x.ResolvePasswordAsync(options.Value, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
