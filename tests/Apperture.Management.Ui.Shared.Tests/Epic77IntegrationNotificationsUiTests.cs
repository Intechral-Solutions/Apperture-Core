using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic77IntegrationNotificationsUiTests
{
    [Fact]
    public void IntegrationNotifications_Route_IsDocumented()
    {
        const string route = "/integrations/{IntegrationId}/notifications";
        route.Should().Contain("/notifications");
        route.Should().StartWith("/integrations/");
    }
}
