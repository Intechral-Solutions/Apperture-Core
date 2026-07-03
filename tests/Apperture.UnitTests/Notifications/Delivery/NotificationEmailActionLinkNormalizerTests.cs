using Apperture.Core.Application.Notifications.Delivery;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications.Delivery;

public sealed class NotificationEmailActionLinkNormalizerTests
{
    [Fact]
    public void Relative_action_url_with_base_url_becomes_absolute()
    {
        NotificationEmailActionLinkNormalizer
            .Normalize("https://mgmt.example.com", "/integrations/int-1/schedule-diagnostics")
            .Should().Be("https://mgmt.example.com/integrations/int-1/schedule-diagnostics");
    }

    [Fact]
    public void Relative_action_url_with_trailing_slash_base_avoids_double_slash()
    {
        NotificationEmailActionLinkNormalizer
            .Normalize("https://mgmt.example.com/", "/integrations/int-1/schedule-diagnostics")
            .Should().Be("https://mgmt.example.com/integrations/int-1/schedule-diagnostics");
    }

    [Fact]
    public void Absolute_action_url_is_preserved()
    {
        const string url = "https://other.example.com/path";
        NotificationEmailActionLinkNormalizer.Normalize("https://mgmt.example.com", url)
            .Should().Be(url);
    }

    [Fact]
    public void Missing_base_url_returns_relative_action_url()
    {
        NotificationEmailActionLinkNormalizer
            .Normalize(null, "/integrations/int-1/schedule-diagnostics")
            .Should().Be("/integrations/int-1/schedule-diagnostics");
    }

    [Fact]
    public void Invalid_base_url_returns_relative_action_url()
    {
        NotificationEmailActionLinkNormalizer
            .Normalize("not-a-url", "/integrations/int-1/schedule-diagnostics")
            .Should().Be("/integrations/int-1/schedule-diagnostics");
    }
}
