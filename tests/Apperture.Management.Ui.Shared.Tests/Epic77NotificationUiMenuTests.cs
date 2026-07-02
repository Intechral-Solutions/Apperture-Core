using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic77NotificationUiMenuTests
{
    [Fact]
    public void ConfigMenu_HasNotificationsWithChildren()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        menus.Should().ContainKey("Config");
        var config = menus["Config"];
        var notifications = config.Items.FirstOrDefault(i => i.Text == "Notifications");
        notifications.Should().NotBeNull();
        notifications!.Href.Should().Be("/config/notifications");
        notifications.Children.Should().NotBeNull();
        var hrefs = notifications.Children!.Select(c => c.Href).ToList();
        hrefs.Should().Contain("/config/notifications/definitions");
        hrefs.Should().Contain("/config/notifications/instances");
        hrefs.Should().Contain("/config/notifications/delivery");
        hrefs.Should().NotContain(DeveloperTaskRoutes.NotificationDiagnostics);
    }
}
