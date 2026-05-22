using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic102PersonalNotificationUiTests
{
    [Fact]
    public void Personal_inbox_route_is_me_notifications()
    {
        PersonalNotificationUiRoutes.Inbox().Should().Be("/me/notifications");
    }

    [Fact]
    public void TopBar_organism_includes_personal_notification_menu()
    {
        var path = Path.Combine(
            FindRepoRoot(),
            "src",
            "Apperture.ControlPlane",
            "Apperture.Management.Ui.Client",
            "Organisms",
            "TopBar.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("ThemeMenu");
        content.Should().Contain("PersonalNotificationMenu");
        content.Should().Contain("UserAccountMenu");
        var themeIdx = content.IndexOf("ThemeMenu", StringComparison.Ordinal);
        var notifIdx = content.IndexOf("PersonalNotificationMenu", StringComparison.Ordinal);
        var userIdx = content.IndexOf("UserAccountMenu", StringComparison.Ordinal);
        themeIdx.Should().BeLessThan(notifIdx);
        notifIdx.Should().BeLessThan(userIdx);
    }

    [Fact]
    public void Notification_unread_badge_atom_exists()
    {
        var path = Path.Combine(
            FindRepoRoot(),
            "src",
            "Apperture.ControlPlane",
            "Apperture.Management.Ui.Client",
            "Atoms",
            "NotificationUnreadBadge.razor");
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void Me_notifications_controller_does_not_require_definition_read_permission()
    {
        var path = Path.Combine(
            FindRepoRoot(),
            "src",
            "Apperture.ControlPlane",
            "Apperture.Management.Api",
            "Controllers",
            "MeNotificationsController.cs");
        var content = File.ReadAllText(path);
        content.Should().Contain("[Authorize]");
        content.Should().NotContain("RequirePlatformReadAsync");
        content.Should().Contain("CanViewDefinitionsAsync");
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "AGENTS.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find repo root.");
    }
}
