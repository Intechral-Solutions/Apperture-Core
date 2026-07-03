using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Notifications.Delivery;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications.Delivery;

public sealed class AttentionIncidentNotificationEmailComposerTests
{
    [Fact]
    public void Compose_includes_html_and_absolute_action_link_when_base_url_configured()
    {
        var message = AttentionIncidentNotificationEmailComposer.Compose(
            "[Apperture] Attention: expected_run_missing (int-1)",
            "Integration: int-1\nCondition: expected_run_missing\n",
            "int-1",
            "inc-42",
            "https://mgmt.example.com",
            DateTimeOffset.Parse("2026-07-01T12:00:00Z"));

        message.HtmlBody.Should().NotBeNullOrWhiteSpace();
        message.HtmlBody.Should().Contain("https://mgmt.example.com/integrations/int-1/schedule-diagnostics?incidentId=inc-42");
        message.PlainTextBody.Should().Contain("https://mgmt.example.com/integrations/int-1/schedule-diagnostics?incidentId=inc-42");
        message.AbsoluteActionUrl.Should().Be("https://mgmt.example.com/integrations/int-1/schedule-diagnostics?incidentId=inc-42");
    }

    [Fact]
    public void Compose_preserves_relative_action_link_when_base_url_missing()
    {
        var message = AttentionIncidentNotificationEmailComposer.Compose(
            "Subject",
            "Body line",
            "int-1",
            "inc-42",
            managementHostBaseUrl: null,
            DateTimeOffset.UtcNow);

        message.HtmlBody.Should().NotBeNullOrWhiteSpace();
        message.PlainTextBody.Should().Contain("/integrations/int-1/schedule-diagnostics?incidentId=inc-42");
        message.AbsoluteActionUrl.Should().BeNull();
    }

    [Fact]
    public void BuildScheduleDiagnosticsActionUrl_escapes_path_segments()
    {
        AttentionIncidentNotificationEmailComposer.BuildScheduleDiagnosticsActionUrl("int/a", "inc b")
            .Should()
            .Be("/integrations/int%2Fa/schedule-diagnostics?incidentId=inc%20b");
    }
}
