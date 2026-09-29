using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Pages.Connections;
using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moq;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// Renders the real connection workspace and checks the retire confirmation and retired-state copy.
/// </summary>
public sealed class ConnectionRetireCopyRenderTests
{
    private const string ConnectionId = "conn-0001";
    private const string ConnectionName = "Warehouse";

    [Fact]
    public async Task Retire_confirmation_says_bindings_stay_recorded_and_do_not_keep_working()
    {
        await using var rig = await RenderAsync(state: "Active", canRetire: true);

        rig.Html.Should().Contain("id=\"connection-retire-trigger\"");
        rig.Html.Should().NotContain("Retire connection?");
        rig.Html.Should().NotContain(ConnectionWorkspacePresentation.RetiredNotice);

        await rig.Do(rig.Page.OpenRetireConfirmAsync);

        var dialog = Dialog(rig.Html);
        dialog.Should().Contain("role=\"dialog\"").And.Contain("aria-modal=\"true\"");
        dialog.Should().Contain("Retire connection?");
        dialog.Should().Contain(ConnectionName);
        dialog.Should().Contain(ConnectionWorkspacePresentation.RetireConfirmConsequence);
        dialog.Should().Contain(ConnectionWorkspacePresentation.RetireConfirmDetails);
        dialog.Should().Contain("cannot be undone");
        dialog.Should().Contain("not deleted");
        dialog.Should().Contain("stay recorded");
        dialog.Should().Contain("will not be able to use this connection");
        dialog.Should().Contain("History for this connection remains");
        dialog.Should().Contain(">Retire connection<");
        dialog.Should().NotContain("keep working");
        dialog.Should().NotContain("continue working");
        dialog.Should().NotContain("secret");
        AssertNoRestoreOrDelete(rig.Html);
        rig.Client.Verify(c => c.RetireConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_retired_connection_states_the_terminal_notice_and_offers_no_restore_or_delete()
    {
        await using var rig = await RenderAsync(state: "Retired", canRetire: true);

        rig.Html.Should().Contain(ConnectionWorkspacePresentation.RetiredNotice);
        rig.Html.Should().Contain("stay in place and do not resolve until they are changed");
        rig.Html.Should().NotContain("keep working");
        rig.Html.Should().NotContain("id=\"connection-retire-trigger\"");
        rig.Html.Should().NotContain("Retire connection?");
        AssertNoRestoreOrDelete(rig.Html);

        await rig.Do(rig.Page.OpenRetireConfirmAsync);

        rig.Html.Should().NotContain("role=\"dialog\"");
        rig.Client.Verify(c => c.RetireConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_still_retires_the_connection_and_does_not_add_another_lifecycle_action()
    {
        await using var rig = await RenderAsync(state: "Active", canRetire: true);
        rig.Client.Setup(c => c.RetireConnectionAsync(ConnectionId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await rig.Do(rig.Page.OpenRetireConfirmAsync);

        await rig.Do(rig.Page.ConfirmRetireAsync);

        rig.Client.Verify(c => c.RetireConnectionAsync(ConnectionId, It.IsAny<CancellationToken>()), Times.Once);
        rig.Nav.Navigations.Should().Contain(n => n.Uri == "/connections" || n.Uri.EndsWith("/connections"));
        rig.Toasts.Toasts.Should().Contain(t => t.Severity == ToastSeverity.Success && t.Message == "Connection retired.");
        AssertNoRestoreOrDelete(rig.Html);
    }

    private static void AssertNoRestoreOrDelete(string html)
    {
        html.Should().NotContain("Un-retire");
        html.Should().NotContain("Unretire");
        html.Should().NotContain(">Restore<");
        html.Should().NotContain("Delete connection");
        html.Should().NotContain("id=\"connection-delete-trigger\"");
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

    private static ConnectionDto Connection(string state) =>
        new(
            ConnectionId,
            "Apperture.Platform.Sql",
            ConnectionName,
            new Dictionary<string, string> { ["Host"] = "db.example" },
            new Dictionary<string, string>(),
            null,
            null,
            state,
            false,
            "NeverTested",
            null,
            null,
            [],
            ResourceContextId: ResourceContextIds.SharedValue,
            ConnectorDisplayName: "Platform SQL");

    private static async Task<Rig> RenderAsync(string state, bool canRetire)
    {
        var client = new Mock<IManagementClient>(MockBehavior.Loose);
        client.Setup(c => c.GetConnectionAsync(ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Connection(state));
        client.Setup(c => c.GetResourceContextsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)
            [
                new(ResourceContextIds.SharedValue, "Shared", "shared", ResourceContextKinds.Shared, ResourceContextStates.Active, true, null, default, default)
            ]);

        var caps = new Mock<IOperatorCapabilitiesService>();
        caps.Setup(c => c.GetCapabilitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorCapabilitiesDto(false, false, false, false, false)
                with
                {
                    CanViewConnections = true,
                    CanRetireConnections = canRetire
                });

        var nav = new TestNavigationManager("https://localhost/", $"https://localhost/connections/{ConnectionId}");
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
            .AddSingleton<IJSRuntime>(js)
            .AddSingleton(session)
            .AddSingleton(new ShellOrientationService())
            .AddSingleton<IToastService>(toasts)
            .AddSingleton(viewState)
            .BuildServiceProvider();

        var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        ConnectionDetail? page = null;
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<Host>(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(Host.ConnectionId)] = ConnectionId,
                [nameof(Host.OnCapture)] = (Action<ConnectionDetail>)(p => page = p)
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
        [Parameter] public string ConnectionId { get; set; } = "";
        [Parameter] public Action<ConnectionDetail>? OnCapture { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<ConnectionDetail>(0);
            builder.AddAttribute(1, nameof(ConnectionDetail.ConnectionId), ConnectionId);
            builder.AddComponentReferenceCapture(2, component => OnCapture?.Invoke((ConnectionDetail)component));
            builder.CloseComponent();
        }
    }

    private sealed class Rig : IAsyncDisposable
    {
        public required Mock<IManagementClient> Client { get; init; }
        public required ServiceProvider Services { get; init; }
        public required HtmlRenderer Renderer { get; init; }
        public required ConnectionDetail Page { get; init; }
        public required TestNavigationManager Nav { get; init; }
        public required ToastService Toasts { get; init; }
        public required Func<string> ReadHtml { get; init; }

        public string Html => ReadHtml();

        public Task Do(Func<Task> action) => Renderer.Dispatcher.InvokeAsync(action);

        public async ValueTask DisposeAsync()
        {
            await Renderer.DisposeAsync();
            await Services.DisposeAsync();
            Toasts.Dispose();
        }
    }
}
