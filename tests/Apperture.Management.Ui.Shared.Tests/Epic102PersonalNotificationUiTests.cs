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
    public void Personal_notification_menu_subscribes_to_watcher_changed_and_unsubscribes_on_dispose()
    {
        // No bUnit (or any other Razor-component render harness) exists anywhere in this repo -- see
        // tests/Apperture.Management.Ui.Client.Tests/ResourceContextViewStateTests.cs's doc comment --
        // and OnWatcherChanged's body (InvokeAsync/StateHasChanged) requires a live RenderHandle to
        // execute, which only a real or fake Blazor renderer can provide. Source-text assertion is the
        // established pattern for this exact component (see the other tests in this file and
        // Uix2ManagementBreadcrumbStabilizationTests.cs's "Session.Changed -=" assertion), so this test
        // follows suit rather than introducing new test infrastructure as a side effect of this task.
        var path = Path.Combine(
            FindRepoRoot(),
            "src",
            "Apperture.ControlPlane",
            "Apperture.Management.Ui.Client",
            "Organisms",
            "PersonalNotificationMenu.razor");
        var content = File.ReadAllText(path);

        content.Should().Contain("@inject INotificationToastWatcher Watcher");
        content.Should().Contain("Watcher.Changed += OnWatcherChanged;");
        content.Should().Contain("Watcher.Changed -= OnWatcherChanged;");
        content.Should().Contain("_unreadCount = Watcher.UnreadCount;");

        // The subscribe must sit alongside the existing Session.Changed/Menus.Changed subscriptions in
        // OnInitialized, and the unsubscribe alongside their Dispose() counterparts -- not off on its own.
        var onInitializedIdx = content.IndexOf("protected override void OnInitialized()", StringComparison.Ordinal);
        var disposeIdx = content.IndexOf("public void Dispose()", StringComparison.Ordinal);
        onInitializedIdx.Should().BeGreaterThan(-1);
        disposeIdx.Should().BeGreaterThan(-1);

        var onInitializedBody = content[onInitializedIdx..content.IndexOf('}', onInitializedIdx)];
        onInitializedBody.Should().Contain("Session.Changed += OnSessionChanged;");
        onInitializedBody.Should().Contain("Watcher.Changed += OnWatcherChanged;");

        var disposeBody = content[disposeIdx..content.IndexOf('}', disposeIdx)];
        disposeBody.Should().Contain("Session.Changed -= OnSessionChanged;");
        disposeBody.Should().Contain("Watcher.Changed -= OnWatcherChanged;");
    }

    [Fact]
    public void Personal_notification_menu_footer_links_to_notification_settings()
    {
        var path = Path.Combine(
            FindRepoRoot(),
            "src",
            "Apperture.ControlPlane",
            "Apperture.Management.Ui.Client",
            "Organisms",
            "PersonalNotificationMenu.razor");
        var content = File.ReadAllText(path);

        content.Should().Contain("<a href=\"@AccountUiRoutes.Notifications\" class=\"ui-menu-item\" @onclick=\"Close\">Notification settings</a>");

        // The settings link belongs in the same footer as "View all notifications", after it.
        var footerIdx = content.IndexOf("ui-personal-notification-menu__footer", StringComparison.Ordinal);
        var viewAllIdx = content.IndexOf("View all notifications", StringComparison.Ordinal);
        var settingsIdx = content.IndexOf("Notification settings", StringComparison.Ordinal);
        footerIdx.Should().BeGreaterThan(-1);
        viewAllIdx.Should().BeGreaterThan(footerIdx);
        settingsIdx.Should().BeGreaterThan(viewAllIdx);
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
