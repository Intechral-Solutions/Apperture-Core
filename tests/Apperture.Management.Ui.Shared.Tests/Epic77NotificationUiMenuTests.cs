using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic77NotificationUiMenuTests
{
    [Fact]
    public void ConfigMenu_HasNotificationsUnderAttention()
    {
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        menus.Should().ContainKey("Config");
        var attention = menus["Config"].Items.First(i => i.Text == "Attention");
        var notifications = attention.Children!.FirstOrDefault(i => i.Text == "Notifications");
        notifications.Should().NotBeNull();
        notifications!.Href.Should().Be("/config/notifications");
        notifications.Children.Should().NotBeNull();
        var hrefs = notifications.Children!.Select(c => c.Href).ToList();
        hrefs.Should().Contain("/config/notifications/definitions");
        hrefs.Should().Contain("/config/notifications/instances");
        hrefs.Should().Contain("/config/notifications/delivery");
        hrefs.Should().Contain(DeveloperTaskRoutes.NotificationDiagnostics);
    }
}
