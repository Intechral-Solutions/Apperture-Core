using System.Net;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Pages.Schedules;
using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// Renders the real schedule detail page and drives the same handlers its delete controls call.
/// </summary>
public sealed class ScheduleDetailDeleteRenderTests
{
    private const string ScheduleId = "sched-1";
    private const string ScheduleName = "Nightly import";
    private const string EnabledRefusal = "Cannot delete schedule: it is still enabled. Disable it first.";

    [Fact]
    public async Task A_disabled_schedule_offers_delete_to_an_operator_who_can_manage_it()
    {
        await using var rig = await RenderAsync(enabled: false, canManage: true);

        rig.Html.Should().Contain("id=\"schedule-delete-trigger\"");
        rig.Html.Should().Contain("aria-label=\"Delete schedule\"");
        Button(rig.Html, "id=\"schedule-delete-trigger\"").Should().NotContain("disabled");
        rig.Html.Should().NotContain("id=\"schedule-delete-unavailable\"");
        rig.Html.Should().NotContain("Delete schedule?");
        EnableAndDisableRemain(rig.Html, scheduleEnabled: false);
    }

    [Fact]
    public async Task An_enabled_schedule_does_not_offer_delete_and_says_to_disable_first()
    {
        await using var rig = await RenderAsync(enabled: true, canManage: true);

        rig.Html.Should().NotContain("id=\"schedule-delete-trigger\"");
        rig.Html.Should().Contain("id=\"schedule-delete-unavailable\"");
        rig.Html.Should().Contain("Disable the schedule before deleting it.");
        rig.Html.Should().NotContain("Delete schedule?");
        EnableAndDisableRemain(rig.Html, scheduleEnabled: true);

        await rig.Do(rig.Page.OpenDeleteConfirmAsync);
        rig.Html.Should().NotContain("role=\"dialog\"", "an enabled schedule has no confirm path");
        await rig.PageInvoked(rig.Page.ConfirmDeleteAsync);
        rig.Client.Verify(c => c.DeleteScheduleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_read_only_operator_sees_no_delete_action_and_no_disable_first_prompt()
    {
        await using var rig = await RenderAsync(enabled: false, canManage: false);

        rig.Html.Should().Contain(ScheduleName);
        rig.Html.Should().NotContain("id=\"schedule-delete-trigger\"");
        rig.Html.Should().NotContain("id=\"schedule-delete-unavailable\"");
        rig.Html.Should().NotContain("title=\"Enable this schedule\"");
        rig.Html.Should().NotContain("title=\"Disable this schedule\"");

        await rig.PageInvoked(rig.Page.ConfirmDeleteAsync);
        rig.Client.Verify(c => c.DeleteScheduleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Confirmation_states_permanence_removed_assignments_and_retained_history()
    {
        await using var rig = await RenderAsync(enabled: false, canManage: true);

        await rig.Do(rig.Page.OpenDeleteConfirmAsync);

        var dialog = Dialog(rig.Html);
        dialog.Should().Contain("role=\"dialog\"").And.Contain("aria-modal=\"true\"");
        dialog.Should().Contain("Delete schedule?");
        dialog.Should().Contain(ScheduleName);
        dialog.Should().Contain("permanently removes");
        dialog.Should().Contain("This cannot be undone.");
        dialog.Should().Contain("Current assignments and live scheduling state are removed.");
        dialog.Should().Contain("Past firings and the runs they started stay in history.");
        dialog.Should().Contain("Opening this schedule from those records will no longer be available.");
        dialog.Should().Contain("data-testid=\"confirm-accept\"");
        dialog.Should().Contain(">Delete schedule<");
        dialog.Should().Contain("data-testid=\"confirm-cancel\"");
        dialog.Should().NotContain("cursor");
        dialog.Should().NotContain("runs are deleted");
        dialog.Should().NotContain("integration will", "delete does not claim the integration is removed");
    }

    [Fact]
    public async Task Cancel_closes_the_dialog_and_sends_no_request()
    {
        await using var rig = await RenderAsync(enabled: false, canManage: true);
        await rig.Do(rig.Page.OpenDeleteConfirmAsync);
        rig.Html.Should().Contain("Delete schedule?");

        await rig.Do(rig.Page.CancelDeleteAsync);

        rig.Html.Should().NotContain("Delete schedule?");
        rig.Html.Should().Contain(ScheduleName);
        rig.Client.Verify(c => c.DeleteScheduleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        rig.Nav.Navigations.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_deletes_that_schedule_then_leaves_for_the_list_with_success_feedback()
    {
        await using var rig = await RenderAsync(enabled: false, canManage: true);
        rig.Client.Setup(c => c.DeleteScheduleAsync(ScheduleId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await rig.Do(rig.Page.OpenDeleteConfirmAsync);

        await rig.Do(rig.Page.ConfirmDeleteAsync);

        rig.Client.Verify(c => c.DeleteScheduleAsync(ScheduleId, It.IsAny<CancellationToken>()), Times.Once);
        rig.Nav.Navigations.Should().Contain(n => n.Uri == "/schedules" || n.Uri.EndsWith("/schedules"));
        rig.Toasts.Toasts.Should().Contain(t => t.Severity == ToastSeverity.Success && t.Message == "Schedule deleted.");
        rig.Client.Verify(c => c.GetScheduleAsync(ScheduleId, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_409_leaves_the_schedule_in_place_and_says_it_must_be_disabled()
    {
        await using var rig = await RenderAsync(enabled: false, canManage: true, refreshAsEnabled: true);
        rig.Client.Setup(c => c.DeleteScheduleAsync(ScheduleId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException(EnabledRefusal, null, HttpStatusCode.Conflict));
        await rig.Do(rig.Page.OpenDeleteConfirmAsync);
        var navigationsBefore = rig.Nav.Navigations.Count;

        var act = () => rig.Do(rig.Page.ConfirmDeleteAsync);

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ex.Which.Message.Should().Be(EnabledRefusal);
        rig.Html.Should().Contain(ScheduleName);
        rig.Html.Should().Contain("Disable the schedule before deleting it.");
        rig.Html.Should().NotContain("id=\"schedule-delete-trigger\"");
        rig.Html.Should().Contain("Delete schedule?");
        rig.Nav.Navigations.Should().HaveCount(navigationsBefore);
        rig.Toasts.Toasts.Should().NotContain(t => t.Severity == ToastSeverity.Success);
        rig.Client.Verify(c => c.DeleteScheduleAsync(ScheduleId, It.IsAny<CancellationToken>()), Times.Once);
        rig.Client.Verify(c => c.GetScheduleAsync(ScheduleId, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task A_404_closes_confirmation_and_shows_the_schedule_is_gone()
    {
        await using var rig = await RenderAsync(enabled: false, canManage: true);
        rig.Client.Setup(c => c.DeleteScheduleAsync(ScheduleId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("404 Not Found", null, HttpStatusCode.NotFound));
        await rig.Do(rig.Page.OpenDeleteConfirmAsync);

        await rig.Do(rig.Page.ConfirmDeleteAsync);

        rig.Html.Should().Contain("Schedule not found");
        rig.Html.Should().NotContain("Delete schedule?");
        rig.Html.Should().NotContain("id=\"schedule-delete-trigger\"");
        rig.Html.Should().NotContain(ScheduleName);
        rig.Toasts.Toasts.Should().Contain(t => t.Severity == ToastSeverity.Warning && t.Message == "This schedule no longer exists.");
        rig.Toasts.Toasts.Should().NotContain(t => t.Severity == ToastSeverity.Success);
        rig.Nav.Navigations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_403_keeps_the_schedule_and_does_not_add_resource_detail_to_the_error()
    {
        await using var rig = await RenderAsync(enabled: false, canManage: true);
        rig.Client.Setup(c => c.DeleteScheduleAsync(ScheduleId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("403 Forbidden", null, HttpStatusCode.Forbidden));
        await rig.Do(rig.Page.OpenDeleteConfirmAsync);
        var navigationsBefore = rig.Nav.Navigations.Count;

        var act = () => rig.Do(rig.Page.ConfirmDeleteAsync);

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        ex.Which.Message.Should().Be("403 Forbidden");
        ex.Which.Message.Should().NotContain(ScheduleId).And.NotContain(ScheduleName);
        rig.Html.Should().Contain(ScheduleName);
        rig.Html.Should().Contain("id=\"schedule-delete-trigger\"");
        rig.Html.Should().Contain("Delete schedule?");
        rig.Nav.Navigations.Should().HaveCount(navigationsBefore);
        rig.Toasts.Toasts.Should().BeEmpty();
    }

    [Fact]
    public async Task A_network_failure_keeps_the_schedule_and_does_not_navigate()
    {
        await using var rig = await RenderAsync(enabled: false, canManage: true);
        rig.Client.Setup(c => c.DeleteScheduleAsync(ScheduleId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("The connection was refused."));
        await rig.Do(rig.Page.OpenDeleteConfirmAsync);

        var act = () => rig.Do(rig.Page.ConfirmDeleteAsync);

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.Message.Should().Be("The connection was refused.");
        rig.Html.Should().Contain(ScheduleName);
        rig.Html.Should().Contain("id=\"schedule-delete-trigger\"");
        rig.Nav.Navigations.Should().BeEmpty();
        rig.Toasts.Toasts.Should().NotContain(t => t.Severity == ToastSeverity.Success);
    }

    private static void EnableAndDisableRemain(string html, bool scheduleEnabled)
    {
        var enable = Button(html, "title=\"Enable this schedule\"");
        var disable = Button(html, "title=\"Disable this schedule\"");
        enable.Should().MatchRegex(@">\s*Enable\s*</button>");
        disable.Should().MatchRegex(@">\s*Disable\s*</button>");
        if (scheduleEnabled)
        {
            enable.Should().Contain("disabled");
            disable.Should().NotContain("disabled");
        }
        else
        {
            enable.Should().NotContain("disabled");
            disable.Should().Contain("disabled");
        }
    }

    private static string Dialog(string html)
    {
        var start = html.LastIndexOf("<div", html.IndexOf("role=\"dialog\"", StringComparison.Ordinal), StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = html.IndexOf("data-testid=\"confirm-accept\"", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        var close = html.IndexOf("</button>", end, StringComparison.Ordinal);
        return html[start..(close + "</button>".Length)];
    }

    private static string Button(string html, string marker)
    {
        var markerAt = html.IndexOf(marker, StringComparison.Ordinal);
        markerAt.Should().BeGreaterThanOrEqualTo(0, $"expected markup containing {marker}");
        var start = html.LastIndexOf("<button", markerAt, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = html.IndexOf("</button>", markerAt, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return html[start..(end + "</button>".Length)];
    }

    private static ScheduleDetailDto Detail(bool enabled) =>
        new(
            ScheduleId,
            ScheduleName,
            "0 2 * * *",
            "UTC",
            enabled,
            0,
            null,
            1,
            null,
            null,
            null,
            null);

    private static async Task<Rig> RenderAsync(bool enabled, bool canManage, bool refreshAsEnabled = false)
    {
        var reads = 0;
        var client = new Mock<IManagementClient>(MockBehavior.Loose);
        client.Setup(c => c.GetScheduleAsync(ScheduleId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                var showEnabled = refreshAsEnabled && reads > 0 ? true : enabled;
                reads++;
                return Task.FromResult<ScheduleDetailDto?>(Detail(showEnabled));
            });
        client.Setup(c => c.GetResourceContextsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)
            [
                new(ResourceContextIds.SharedValue, "Shared", "shared", ResourceContextKinds.Shared, ResourceContextStates.Active, true, null, default, default)
            ]);

        var caps = new Mock<IOperatorCapabilitiesService>();
        caps.Setup(c => c.GetCapabilitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorCapabilitiesDto(canManage, false, false, false, false));

        var nav = new TestNavigationManager("https://localhost/", $"https://localhost/schedules/{ScheduleId}");
        var session = new SessionStateService(client.Object);
        var js = new FakeJsRuntime();
        var viewState = new ResourceContextViewState(js, nav, client.Object, session);
        await viewState.InitializeAsync();
        nav.Navigations.Clear();

        var toasts = new ToastService();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(client.Object)
            .AddSingleton(caps.Object)
            .AddSingleton<NavigationManager>(nav)
            .AddSingleton<Microsoft.JSInterop.IJSRuntime>(js)
            .AddSingleton(session)
            .AddSingleton(new ShellOrientationService())
            .AddSingleton<IToastService>(toasts)
            .AddSingleton(viewState)
            .BuildServiceProvider();

        var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        ScheduleDetail? page = null;
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<Host>(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(Host.ScheduleId)] = ScheduleId,
                [nameof(Host.OnCapture)] = (Action<ScheduleDetail>)(p => page = p)
            })));

        return new Rig
        {
            Client = client,
            Services = services,
            Renderer = renderer,
            Page = page!,
            Nav = nav,
            Toasts = toasts,
            ReadHtml = () => renderer.Dispatcher.InvokeAsync(root.ToHtmlString).GetAwaiter().GetResult()
        };
    }

    private sealed class Host : ComponentBase
    {
        [Parameter] public string ScheduleId { get; set; } = "";
        [Parameter] public Action<ScheduleDetail>? OnCapture { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<ScheduleDetail>(0);
            builder.AddAttribute(1, nameof(ScheduleDetail.ScheduleId), ScheduleId);
            builder.AddComponentReferenceCapture(2, component => OnCapture?.Invoke((ScheduleDetail)component));
            builder.CloseComponent();
        }
    }

    private sealed class Rig : IAsyncDisposable
    {
        public required Mock<IManagementClient> Client { get; init; }
        public required ServiceProvider Services { get; init; }
        public required HtmlRenderer Renderer { get; init; }
        public required ScheduleDetail Page { get; init; }
        public required TestNavigationManager Nav { get; init; }
        public required ToastService Toasts { get; init; }
        public required Func<string> ReadHtml { get; init; }

        public string Html => ReadHtml();

        public Task Do(Func<Task> action) => Renderer.Dispatcher.InvokeAsync(action);

        /// <summary>Invokes a page handler that is already running on the renderer dispatcher.</summary>
        public Task PageInvoked(Func<Task> action) => Do(action);

        public async ValueTask DisposeAsync()
        {
            await Renderer.DisposeAsync();
            await Services.DisposeAsync();
            Toasts.Dispose();
        }
    }
}
