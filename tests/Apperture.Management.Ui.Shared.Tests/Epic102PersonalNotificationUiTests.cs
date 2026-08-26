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

        var menuPath = Path.Combine(
            FindRepoRoot(),
            "src",
            "Apperture.ControlPlane",
            "Apperture.Management.Ui.Client",
            "Organisms",
            "PersonalNotificationMenu.razor");
        var menuContent = File.ReadAllText(menuPath);
        menuContent.Should().Contain("ui-personal-notification-menu__panel");

        var cssPath = Path.Combine(
            FindRepoRoot(),
            "src",
            "Apperture.ControlPlane",
            "Apperture.Management.Ui.Client",
            "wwwroot",
            "css",
            "management-ui-primitives.css");
        var cssContent = File.ReadAllText(cssPath);
        cssContent.Should().Contain(".ui-topbar");
        cssContent.Should().Contain("z-index: 60");
        cssContent.Should().Contain(".ui-personal-notification-menu__panel");
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
        var content = File.ReadAllText(path);
        content.Should().Contain("ui-notification-unread-badge");
    }

    [Fact]
    public void Top_bar_utility_menus_share_an_exclusive_open_coordinator()
    {
        var root = FindRepoRoot();
        var clientRoot = Path.Combine(root, "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client");

        File.Exists(Path.Combine(clientRoot, "Services", "TopBarMenuCoordinator.cs")).Should().BeTrue();

        var di = File.ReadAllText(Path.Combine(clientRoot, "Services", "AppertureUiShellServiceCollectionExtensions.cs"));
        di.Should().Contain("AddScoped<TopBarMenuCoordinator>");

        foreach (var relative in new[]
                 {
                     Path.Combine("Molecules", "ThemeMenu.razor"),
                     Path.Combine("Organisms", "PersonalNotificationMenu.razor"),
                     Path.Combine("Molecules", "UserAccountMenu.razor")
                 })
        {
            var content = File.ReadAllText(Path.Combine(clientRoot, relative));
            content.Should().Contain("TopBarMenuCoordinator", $"{relative} must coordinate exclusive open state");
        }

        var topBar = File.ReadAllText(Path.Combine(clientRoot, "Organisms", "TopBar.razor"));
        topBar.Should().Contain("Menus.CloseAll()");

        var layout = File.ReadAllText(Path.Combine(clientRoot, "Layout", "MainLayout.razor"));
        layout.Should().Contain("@onclick=\"CloseTopBarMenus\"");
        layout.Should().Contain("TopBarMenus.CloseAll()");

        var css = File.ReadAllText(Path.Combine(clientRoot, "wwwroot", "css", "management-ui-primitives.css"));
        css.Should().Contain(".ui-notification-unread-badge");
        css.Should().Contain("bottom:");
    }

    [Fact]
    public void Personal_notifications_page_uses_empty_state_without_invalid_class_parameter()
    {
        var path = Path.Combine(
            FindRepoRoot(),
            "src",
            "Apperture.ControlPlane",
            "Apperture.Management.Ui.Client",
            "Pages",
            "Me",
            "PersonalNotifications.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("<EmptyState");
        content.Should().NotContain("EmptyState class=");
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
