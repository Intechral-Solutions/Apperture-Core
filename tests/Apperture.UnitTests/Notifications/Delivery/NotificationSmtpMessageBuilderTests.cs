using System.Net.Mail;
using Apperture.ControlPlane.Security;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications.Delivery;

public sealed class NotificationSmtpMessageBuilderTests
{
    [Fact]
    public void ApplyBodies_adds_multipart_alternate_views_when_html_present()
    {
        using var message = new MailMessage();
        NotificationSmtpMessageBuilder.ApplyBodies(message, "Plain body", "<html><body>Html body</body></html>");

        message.Body.Should().Be("Plain body");
        message.IsBodyHtml.Should().BeFalse();
        message.AlternateViews.Should().HaveCount(2);
        message.AlternateViews[0].ContentType.MediaType.Should().Be("text/plain");
        message.AlternateViews[1].ContentType.MediaType.Should().Be("text/html");
    }

    [Fact]
    public void ApplyBodies_uses_plain_text_only_when_html_missing()
    {
        using var message = new MailMessage();
        NotificationSmtpMessageBuilder.ApplyBodies(message, "Plain only", null);

        message.Body.Should().Be("Plain only");
        message.IsBodyHtml.Should().BeFalse();
        message.AlternateViews.Should().BeEmpty();
    }
}
