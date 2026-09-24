using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Pages.ComponentCatalog;
using Apperture.Management.Ui.Client.Pages.Connections;
using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moq;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// Real renders of the two pages that replace the retired Connectors tab: the Connections list (which now only lists
/// Connections and links to the catalog) and the Components catalog (which must actually open filtered when the link's
/// <c>?kind=connector</c> is followed, on a hard load or an in-app navigation).
/// </summary>
public sealed class CatalogNavigationRenderTests
{
    private static ComponentSummaryDto Connector() => new("connector", "Apperture.Platform.Sql", "Platform SQL", "SQL Server", "system", null);

    private static ComponentSummaryDto Definition() =>
        new("notification-definition", "run.failed", "Run failed", "A run failed", "system", null);

    private static OperatorCapabilitiesDto Caps(bool connections = false, bool definitions = false, bool notifications = false, bool create = false) =>
        new OperatorCapabilitiesDto(false, false, false, false, false)
            with { CanViewConnections = connections, CanViewConnectorDefinitions = definitions, CanViewNotificationDefinitions = notifications, CanCreateConnections = create };

    private sealed class Rig : IAsyncDisposable
    {
        public required Mock<IManagementClient> Client { get; init; }
        public required TestNavigationManager Nav { get; init; }
        public required ServiceProvider Services { get; init; }
        public required HtmlRenderer Renderer { get; init; }
        public async ValueTask DisposeAsync()
        {
            await Renderer.DisposeAsync();
            await Services.DisposeAsync();
        }
    }

    private static Rig NewRig(string uri, OperatorCapabilitiesDto caps, IReadOnlyList<ComponentSummaryDto> catalog, Task? firstLoadGate = null)
    {
        var client = new Mock<IManagementClient>();
        var gateUsed = 0;
        client.Setup(c => c.GetComponentsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async (string? kind, string? _, string? _, string? _, int _, CancellationToken _) =>
            {
                if (kind is null && firstLoadGate is not null && Interlocked.Exchange(ref gateUsed, 1) == 0)
                    await firstLoadGate;
                var items = catalog.Where(i => kind is null || i.Kind == kind).ToList();
                // Like the real server: a kind-restricted request only ever reports that one kind.
                return new ComponentCatalogResultDto(items, items.Select(i => i.Kind).Distinct().ToList(), [], items.Count);
            });
        client.Setup(c => c.GetConnectionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ConnectionDto>)[]);
        client.Setup(c => c.GetResourceContextsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[
                new ResourceContextDto(ResourceContextIds.SharedValue, "Shared", "shared", ResourceContextKinds.Shared, ResourceContextStates.Active, true, null, default, default)]);

        var capabilities = new Mock<IOperatorCapabilitiesService>();
        capabilities.Setup(c => c.GetCapabilitiesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(caps);

        var nav = new TestNavigationManager("https://localhost/", uri);
        var session = new SessionStateService(client.Object);
        var js = new FakeJsRuntime();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(client.Object)
            .AddSingleton(capabilities.Object)
            .AddSingleton<NavigationManager>(nav)
            .AddSingleton<IJSRuntime>(js)
            .AddSingleton(session)
            .AddSingleton(new ShellOrientationService())
            .AddSingleton<IToastService>(new ToastService())
            .AddSingleton(new ResourceContextViewState(js, nav, client.Object, session))
            .BuildServiceProvider();
        var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return new Rig { Client = client, Nav = nav, Services = services, Renderer = renderer };
    }

    // Renders the page once and returns a function that reads its CURRENT html, so later navigation can be observed.
    private static async Task<Func<string>> RenderAsync<T>(Rig rig) where T : IComponent
    {
        var root = await rig.Renderer.Dispatcher.InvokeAsync(() => rig.Renderer.RenderComponentAsync<T>(ParameterView.Empty));
        return () => rig.Renderer.Dispatcher.InvokeAsync(root.ToHtmlString).GetAwaiter().GetResult();
    }

    private static void VerifyKindRequested(Rig rig, string? kind, Times times) =>
        rig.Client.Verify(c => c.GetComponentsAsync(kind, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), times);

    // ---- Connections list ----------------------------------------------------------------------------

    [Fact]
    public async Task UI8_the_connections_list_has_no_connectors_tab_and_links_to_the_catalog()
    {
        await using var rig = NewRig("https://localhost/connections", Caps(connections: true, definitions: true, create: true), []);

        var html = (await RenderAsync<ConnectionsPage>(rig))();

        html.Should().Contain("Browse the connector catalog").And.Contain("href=\"/components?kind=connector\"");
        html.Should().NotContain(">Connectors<", "the duplicate discovery tab is retired");
        html.Should().NotContain("Connector definitions describe the");
        html.Should().Contain("Connections");
    }

    [Fact]
    public async Task The_catalog_link_is_offered_only_to_a_caller_who_can_read_connector_definitions()
    {
        await using var rig = NewRig("https://localhost/connections", Caps(connections: true, definitions: false), []);

        var html = (await RenderAsync<ConnectionsPage>(rig))();

        html.Should().NotContain("Browse the connector catalog").And.NotContain("/components?kind=connector");
    }

    [Fact]
    public async Task A_caller_who_can_read_only_definitions_no_longer_gets_a_connectors_view_here_and_is_told_so()
    {
        await using var rig = NewRig("https://localhost/connections", Caps(connections: false, definitions: true), []);

        var html = (await RenderAsync<ConnectionsPage>(rig))();

        html.Should().Contain("You do not have permission to view connections.");
        html.Should().NotContain("Browse the connector catalog");
    }

    [Fact]
    public async Task An_old_tab_query_string_is_simply_ignored()
    {
        await using var rig = NewRig("https://localhost/connections?tab=connectors", Caps(connections: true, definitions: true), []);

        var html = (await RenderAsync<ConnectionsPage>(rig))();

        html.Should().NotContain("Connector definitions describe the").And.Contain("Browse the connector catalog");
    }

    // ---- Components catalog --------------------------------------------------------------------------

    [Fact]
    public async Task A_direct_load_of_the_connector_catalog_url_opens_filtered_to_connectors()
    {
        await using var rig = NewRig("https://localhost/components?kind=connector", Caps(definitions: true, notifications: true), [Connector(), Definition()]);

        var html = (await RenderAsync<ComponentsList>(rig))();

        VerifyKindRequested(rig, "connector", Times.Once());
        html.Should().Contain("Platform SQL").And.NotContain("Run failed", "the notification definition is filtered out");
    }

    [Fact]
    public async Task Ordinary_components_is_unchanged_and_never_filters_by_kind()
    {
        await using var rig = NewRig("https://localhost/components", Caps(definitions: true, notifications: true), [Connector(), Definition()]);

        var html = (await RenderAsync<ComponentsList>(rig))();

        VerifyKindRequested(rig, "connector", Times.Never());
        VerifyKindRequested(rig, "notification-definition", Times.Never());
        html.Should().Contain("Platform SQL").And.Contain("Run failed");
    }

    [Theory]
    [InlineData("https://localhost/components?kind=bogus")]
    [InlineData("https://localhost/components?kind=")]
    [InlineData("https://localhost/components?kind=CONNECTOR")]
    public async Task An_unknown_kind_falls_back_to_the_whole_catalog(string uri)
    {
        await using var rig = NewRig(uri, Caps(definitions: true, notifications: true), [Connector(), Definition()]);

        var html = (await RenderAsync<ComponentsList>(rig))();

        VerifyKindRequested(rig, "bogus", Times.Never());
        VerifyKindRequested(rig, "connector", Times.Never());
        html.Should().Contain("Platform SQL").And.Contain("Run failed");
    }

    [Fact]
    public async Task A_kind_the_caller_cannot_see_is_not_applied()
    {
        // Only notification definitions are readable, so "connector" is not one of this caller's kinds.
        await using var rig = NewRig("https://localhost/components?kind=connector", Caps(notifications: true), [Definition()]);

        var html = (await RenderAsync<ComponentsList>(rig))();

        VerifyKindRequested(rig, "connector", Times.Never());
        html.Should().Contain("Run failed");
    }

    // Navigation is handled on the renderer's dispatcher, so a positive expectation is polled (bounded) rather than slept on.
    private static async Task EventuallyAsync(Action assertion)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (true)
        {
            try
            {
                assertion();
                return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }
        }
    }

    // A negative expectation cannot be polled, so first let everything already queued on the dispatcher finish.
    private static async Task DrainAsync(Rig rig)
    {
        for (var i = 0; i < 5; i++)
        {
            await rig.Renderer.Dispatcher.InvokeAsync(() => Task.CompletedTask);
            await Task.Yield();
        }
    }

    [Fact]
    public async Task Following_the_catalog_link_from_within_the_app_filters_and_going_back_clears_it()
    {
        await using var rig = NewRig("https://localhost/components", Caps(definitions: true, notifications: true), [Connector(), Definition()]);
        var root = await RenderAsync<ComponentsList>(rig);
        root().Should().Contain("Run failed");

        await rig.Renderer.Dispatcher.InvokeAsync(() => rig.Nav.NavigateTo("/components?kind=connector"));
        await EventuallyAsync(() => root().Should().Contain("Platform SQL").And.NotContain("Run failed"));
        VerifyKindRequested(rig, "connector", Times.Once());

        await rig.Renderer.Dispatcher.InvokeAsync(() => rig.Nav.NavigateTo("/components"));
        await EventuallyAsync(() => root().Should().Contain("Platform SQL").And.Contain("Run failed", "back on the plain catalog the filter is gone"));
    }

    [Fact]
    public async Task Moving_in_app_from_one_kind_to_another_keeps_the_full_set_of_kinds_available()
    {
        // A kind-restricted load only reports that kind, so the options must not be overwritten by it.
        await using var rig = NewRig("https://localhost/components?kind=connector", Caps(definitions: true, notifications: true), [Connector(), Definition()]);
        var root = await RenderAsync<ComponentsList>(rig);
        root().Should().Contain("Platform SQL").And.NotContain("Run failed");

        await rig.Renderer.Dispatcher.InvokeAsync(() => rig.Nav.NavigateTo("/components?kind=notification-definition"));

        await EventuallyAsync(() => root().Should().Contain("Run failed").And.NotContain("Platform SQL"));
        VerifyKindRequested(rig, "notification-definition", Times.Once());
    }

    [Fact]
    public async Task A_navigation_during_the_initial_loads_is_not_missed()
    {
        // The address changes while the first (unfiltered) load is still in flight; the newest address wins.
        var gate = new TaskCompletionSource();
        await using var rig = NewRig("https://localhost/components?kind=connector", Caps(definitions: true, notifications: true), [Connector(), Definition()], firstLoadGate: gate.Task);

        var rendering = RenderAsync<ComponentsList>(rig);
        await rig.Renderer.Dispatcher.InvokeAsync(() => rig.Nav.NavigateTo("/components"));
        gate.SetResult();
        var root = await rendering;

        await EventuallyAsync(() => root().Should().Contain("Platform SQL").And.Contain("Run failed"));
        VerifyKindRequested(rig, "connector", Times.Never());
    }

    [Fact]
    public async Task Leaving_the_catalog_does_not_re_filter_it()
    {
        await using var rig = NewRig("https://localhost/components", Caps(definitions: true, notifications: true), [Connector(), Definition()]);
        await RenderAsync<ComponentsList>(rig);

        await rig.Renderer.Dispatcher.InvokeAsync(() => rig.Nav.NavigateTo("/connections?kind=connector"));
        await DrainAsync(rig);

        VerifyKindRequested(rig, "connector", Times.Never());
    }

    [Fact]
    public async Task A_disposed_catalog_detaches_and_ignores_later_navigation()
    {
        var rig = NewRig("https://localhost/components", Caps(definitions: true, notifications: true), [Connector(), Definition()]);
        await RenderAsync<ComponentsList>(rig);

        await rig.DisposeAsync(); // disposes the renderer, and with it the page
        rig.Nav.NavigateTo("/components?kind=connector");
        await Task.Yield();

        VerifyKindRequested(rig, "connector", Times.Never());
    }
}
