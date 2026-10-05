using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Organisms;
using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared;
using Apperture.Management.Ui.Shared.Auth;
using Apperture.Management.Ui.Shared.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// Renders the REAL <see cref="PersonalNotificationMenu"/> (real Blazor renderer, via
/// <see cref="HtmlRenderer"/> -- the same approach <c>ToastHostRenderTests</c> uses; there is no bUnit
/// anywhere in this repo) to prove the Task 5 badge-liveness wiring behaviorally rather than by
/// source-text assertion: the component subscribes to <see cref="INotificationToastWatcher.Changed"/> in
/// <c>OnInitialized</c>, unsubscribes in <c>Dispose</c>, and the ambient (closed-dropdown) badge updates
/// from the watcher's already-fetched <see cref="INotificationToastWatcher.UnreadCount"/> alone -- with no
/// additional <c>GetMyUnreadNotificationCountAsync</c> HTTP call.
/// </summary>
public sealed class PersonalNotificationMenuBadgeLivenessTests
{
    /// <summary>
    /// Minimal hand-rolled fake (matches this project's established Moq-for-interfaces-with-many-members /
    /// hand-rolled-spy-for-few-members split -- see <c>NotificationToastWatcherTests.cs</c>'s doc comment):
    /// exposes the live subscriber count of its own <c>Changed</c> event so tests can prove
    /// subscribe-on-init / unsubscribe-on-dispose directly, without reflecting into the component.
    /// </summary>
    private sealed class FakeNotificationToastWatcher : INotificationToastWatcher
    {
        public int UnreadCount { get; set; }

        public event Action? Changed;

        public int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;

        public void Start()
        {
        }

        public void Dispose()
        {
        }

        public void RaiseChanged() => Changed?.Invoke();
    }

    private sealed record Harness(
        HtmlRenderer Renderer,
        ServiceProvider Services,
        FakeNotificationToastWatcher Watcher,
        Mock<IManagementClient> Client,
        TopBarMenuCoordinator Menus,
        HtmlRootComponent Root) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Renderer.DisposeAsync();
            await Services.DisposeAsync();
        }

        /// <summary>Reads the current rendered markup. Must run on the renderer's own Dispatcher.</summary>
        public Task<string> HtmlAsync() => Renderer.Dispatcher.InvokeAsync(() => Root.ToHtmlString());

        /// <summary>Fires the watcher's Changed event on the renderer's Dispatcher, exactly as the real
        /// SynchronizationContext-bound event would arrive in a browser tab.</summary>
        public Task RaiseWatcherChangedAsync() => Renderer.Dispatcher.InvokeAsync(Watcher.RaiseChanged);
    }

    private static async Task<Harness> RenderMenuAsync(bool openDropdown = false)
    {
        var client = new Mock<IManagementClient>();
        client.Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeResponse("user-1", "test.user", "Test User", [], null, "Local", [], "Set", [], false, null));
        client.Setup(c => c.GetAccessRequestSessionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntraAccessRequestSessionDto?)null);

        var session = new SessionStateService(client.Object);
        await session.RefreshAsync();

        var watcher = new FakeNotificationToastWatcher();
        var menus = new TopBarMenuCoordinator();
        if (openDropdown)
            menus.Open(TopBarMenuCoordinator.Ids.Notifications);

        // Never LoadMenusAsync()'d on purpose: DomainMenuService.WhenFetchedAsync() then never
        // completes, so the component's own OnAfterRender-kicked-off count refresh never reaches its
        // GetMyUnreadNotificationCountAsync call -- isolating these tests to the watcher-driven path.
        var domainMenuService = new DomainMenuService(
            new HttpClient(),
            Options.Create(new ControlPanelMenuOptions()),
            NullLogger<DomainMenuService>.Instance);

        var nav = new TestNavigationManager("https://localhost/", "https://localhost/");

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(client.Object)
            .AddSingleton(session)
            .AddSingleton(menus)
            .AddSingleton(domainMenuService)
            .AddSingleton<NavigationManager>(nav)
            .AddSingleton<INotificationToastWatcher>(watcher)
            .BuildServiceProvider();

        var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() =>
            renderer.RenderComponentAsync<PersonalNotificationMenu>(ParameterView.Empty));

        return new Harness(renderer, services, watcher, client, menus, root);
    }

    [Fact]
    public async Task Component_subscribes_to_watcher_on_init_and_unsubscribes_on_dispose()
    {
        var harness = await RenderMenuAsync();

        harness.Watcher.SubscriberCount.Should().Be(1, "OnInitialized must subscribe exactly once");

        await harness.Renderer.DisposeAsync();

        harness.Watcher.SubscriberCount.Should().Be(0, "Dispose must unsubscribe, matching the Session.Changed/Menus.Changed pattern");

        await harness.Services.DisposeAsync();
    }

    [Fact]
    public async Task Ambient_badge_updates_from_watcher_changed_without_a_new_http_call()
    {
        await using var harness = await RenderMenuAsync();

        (await harness.HtmlAsync()).Should().NotContain(
            "ui-notification-unread-badge", "no unread notifications have been reported yet");

        harness.Watcher.UnreadCount = 7;
        await harness.RaiseWatcherChangedAsync();

        var html = await harness.HtmlAsync();
        html.Should().Contain("ui-notification-unread-badge");
        html.Should().Contain("7 unread notifications");

        // The whole point of Design Decision 9: the ambient badge is cheap because OnWatcherChanged reads
        // Watcher.UnreadCount directly instead of re-fetching over HTTP.
        harness.Client.Verify(
            c => c.GetMyUnreadNotificationCountAsync(It.IsAny<CancellationToken>()),
            Times.Never,
            "the watcher-driven ambient badge update must not make a new HTTP call");
    }

    [Fact]
    public async Task Second_watcher_tick_updates_the_badge_again_without_any_http_call()
    {
        await using var harness = await RenderMenuAsync();

        harness.Watcher.UnreadCount = 2;
        await harness.RaiseWatcherChangedAsync();
        (await harness.HtmlAsync()).Should().Contain("2 unread notifications");

        harness.Watcher.UnreadCount = 0;
        await harness.RaiseWatcherChangedAsync();
        (await harness.HtmlAsync()).Should().NotContain("ui-notification-unread-badge");

        harness.Client.Verify(
            c => c.GetMyUnreadNotificationCountAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Opening_the_dropdown_still_shows_the_notification_settings_link_alongside_view_all()
    {
        await using var harness = await RenderMenuAsync(openDropdown: true);

        var html = await harness.HtmlAsync();

        html.Should().Contain("ui-personal-notification-menu__footer");
        html.Should().Contain($"href=\"{AccountUiRoutes.Notifications}\"");
        html.Should().Contain("Notification settings");
        html.Should().Contain($"href=\"{PersonalNotificationUiRoutes.Inbox()}\"");
        html.Should().Contain("View all notifications");

        var viewAllIndex = html.IndexOf("View all notifications", StringComparison.Ordinal);
        var settingsIndex = html.IndexOf("Notification settings", StringComparison.Ordinal);
        viewAllIndex.Should().BeGreaterThan(-1);
        settingsIndex.Should().BeGreaterThan(viewAllIndex, "settings link follows the view-all link in the footer");
    }
}
