using Apperture.Core.Application.Notifications.Delivery;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications.Delivery;

public sealed class NotificationEmailMessageComposerTests
{
    [Fact]
    public void Expected_run_missing_email_contains_absolute_schedule_diagnostics_link()
    {
        var message = NotificationEmailMessageComposer.Compose(
            new NotificationEmailComposeInput(
                Subject: "[Apperture] Attention: expected_run_missing (int-1)",
                Summary: "Expected run missing for Campus Nexus",
                BodyText: "Integration: Campus Nexus (int-1)\nCondition: expected_run_missing\n",
                BodyHtml: null,
                ActionText: "Review schedule diagnostics",
                ActionUrl: "/integrations/int-1/schedule-diagnostics",
                InstanceBaseUrl: "https://mgmt.example.com",
                IntegrationDisplayName: "Campus Nexus",
                IntegrationId: "int-1",
                OccurredAtUtc: "2026-07-02 12:00:00"));

        message.AbsoluteActionUrl
            .Should().Be("https://mgmt.example.com/integrations/int-1/schedule-diagnostics");
        message.PlainTextBody.Should().Contain("https://mgmt.example.com/integrations/int-1/schedule-diagnostics");
        message.HtmlBody.Should().Contain("https://mgmt.example.com/integrations/int-1/schedule-diagnostics");
        message.HtmlBody.Should().Contain("Review schedule diagnostics");
        message.HtmlBody.Should().Contain("Campus Nexus");
    }

    [Fact]
    public void Missing_base_url_keeps_relative_link_in_plain_text()
    {
        var message = NotificationEmailMessageComposer.Compose(
            new NotificationEmailComposeInput(
                Subject: "Subject",
                Summary: "Summary",
                BodyText: "Body",
                BodyHtml: null,
                ActionText: "Open",
                ActionUrl: "/integrations/int-1/schedule-diagnostics",
                InstanceBaseUrl: null));

        message.AbsoluteActionUrl.Should().BeNull();
        message.PlainTextBody.Should().Contain("/integrations/int-1/schedule-diagnostics");
        message.HtmlBody.Should().Contain("/integrations/int-1/schedule-diagnostics");
    }

    [Fact]
    public void Plain_text_fallback_is_present_when_html_is_generated()
    {
        var message = NotificationEmailMessageComposer.Compose(
            new NotificationEmailComposeInput(
                Subject: "Subject",
                Summary: "Summary",
                BodyText: "Operator details",
                BodyHtml: null,
                ActionText: null,
                ActionUrl: null,
                InstanceBaseUrl: "https://mgmt.example.com"));

        message.PlainTextBody.Should().Be("Operator details");
        message.HtmlBody.Should().NotBeNullOrWhiteSpace();
        message.HtmlBody.Should().Contain("Operator details");
    }
}
