using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Organisms;
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
/// Renders the REAL structure browser and drives the same handlers its buttons call. It proves the tool is structure-only,
/// makes no request until the operator asks, fetches each node once (so tree navigation does not multiply remote requests and
/// audit records), and reports failures with fixed messages.
/// </summary>
public sealed class SqlStructureBrowserRenderTests
{
    private sealed class Host : ComponentBase
    {
        [Parameter] public Action<SqlStructureBrowser>? OnCapture { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<SqlStructureBrowser>(0);
            builder.AddAttribute(1, nameof(SqlStructureBrowser.ConnectionId), "conn-1");
            builder.AddComponentReferenceCapture(2, o => OnCapture?.Invoke((SqlStructureBrowser)o));
            builder.CloseComponent();
        }
    }

    private sealed class Rig : IAsyncDisposable
    {
        public required Mock<IManagementClient> Client { get; init; }
        public required ServiceProvider Services { get; init; }
        public required HtmlRenderer Renderer { get; init; }
        public required Func<string> ReadHtml { get; init; }
        public required SqlStructureBrowser Browser { get; init; }

        public string Html => ReadHtml();
        public Task Do(Func<Task> action) => Renderer.Dispatcher.InvokeAsync(action);

        public async ValueTask DisposeAsync()
        {
            await Renderer.DisposeAsync();
            await Services.DisposeAsync();
        }
    }

    private static SqlStructureResponseDto<T> Ok<T>(params T[] items) => new("Succeeded", items, false, 500, null);

    private static async Task<Rig> RenderAsync(Action<Mock<IManagementClient>>? configure = null)
    {
        var client = new Mock<IManagementClient>(MockBehavior.Strict);
        configure?.Invoke(client);
        var services = new ServiceCollection().AddLogging().AddSingleton(client.Object).BuildServiceProvider();
        var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        SqlStructureBrowser? browser = null;
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<Host>(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(Host.OnCapture)] = (Action<SqlStructureBrowser>)(b => browser = b) })));
        return new Rig
        {
            Client = client, Services = services, Renderer = renderer, Browser = browser!,
            ReadHtml = () => renderer.Dispatcher.InvokeAsync(root.ToHtmlString).GetAwaiter().GetResult()
        };
    }

    private static Action<Mock<IManagementClient>> Schemas(params string[] names) => client =>
        client.Setup(c => c.ListSqlSchemasAsync("conn-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(names.Select(n => new SqlSchemaDto(n)).ToArray()));

    private static void Tables(Mock<IManagementClient> client, string schema, params SqlTableDto[] tables) =>
        client.Setup(c => c.ListSqlTablesAsync("conn-1", schema, It.IsAny<CancellationToken>())).ReturnsAsync(Ok(tables));

    private static void Columns(Mock<IManagementClient> client, string schema, string table, params SqlColumnDto[] columns) =>
        client.Setup(c => c.ListSqlColumnsAsync("conn-1", schema, table, It.IsAny<CancellationToken>())).ReturnsAsync(Ok(columns));

    // ---- Nothing happens until the operator asks -------------------------------------------------------

    [Fact]
    public async Task PR5_rendering_makes_no_remote_request_and_offers_one_explicit_button()
    {
        await using var rig = await RenderAsync(); // a strict client: ANY call would throw

        rig.Html.Should().Contain("Browse structure").And.Contain("Structure only");
        rig.Client.Invocations.Should().BeEmpty("nothing is requested when the tool renders");
    }

    // ---- Structure only --------------------------------------------------------------------------------

    [Fact]
    public async Task The_tool_offers_no_way_to_read_rows_type_sql_or_change_anything()
    {
        await using var rig = await RenderAsync(client =>
        {
            Schemas("dbo")(client);
            Tables(client, "dbo", new SqlTableDto("dbo", "Orders", false));
            Columns(client, "dbo", "Orders", new SqlColumnDto("Id", "int", false, 1, null));
        });
        await rig.Do(rig.Browser.BrowseAsync);
        await rig.Do(() => rig.Browser.ToggleSchemaAsync("dbo"));
        await rig.Do(() => rig.Browser.ToggleTableAsync("dbo", "Orders"));

        var html = rig.Html;

        html.Should().NotContain("<input").And.NotContain("<textarea").And.NotContain("<select").And.NotContain("<form");
        html.Should().NotContainEquivalentOf("run query").And.NotContainEquivalentOf("execute").And.NotContainEquivalentOf("preview")
            .And.NotContainEquivalentOf("SELECT ").And.NotContainEquivalentOf("delete").And.NotContainEquivalentOf("export");
        html.Should().NotMatchRegex(@"<button[^>]*>\s*(Run|Execute|Preview|Sample|Query|Open data|View data)");
        typeof(SqlStructureBrowser).GetMethods().Where(m => m.DeclaringType == typeof(SqlStructureBrowser))
            .Select(m => m.Name).Should().NotContain(n => n.Contains("Query") || n.Contains("Execute") || n.Contains("Rows") || n.Contains("Preview"));
    }

    // ---- Progressive browsing --------------------------------------------------------------------------

    [Fact]
    public async Task Schemas_then_tables_and_views_then_columns_load_progressively()
    {
        await using var rig = await RenderAsync(client =>
        {
            Schemas("dbo", "sales")(client);
            Tables(client, "sales", new SqlTableDto("sales", "Orders", false), new SqlTableDto("sales", "OrderTotals", true));
            Columns(client, "sales", "Orders",
                new SqlColumnDto("Id", "int", false, 1, null), new SqlColumnDto("Note", "nvarchar", true, 2, 200), new SqlColumnDto("Body", "varchar", true, 3, -1));
        });

        await rig.Do(rig.Browser.BrowseAsync);
        rig.Html.Should().Contain("dbo").And.Contain("sales").And.NotContain("Orders");

        await rig.Do(() => rig.Browser.ToggleSchemaAsync("sales"));
        rig.Html.Should().Contain("Orders").And.Contain("OrderTotals").And.Contain(">View<").And.Contain(">Table<").And.NotContain("Note");

        await rig.Do(() => rig.Browser.ToggleTableAsync("sales", "Orders"));
        rig.Html.Should().Contain("Note").And.Contain("nvarchar(200)").And.Contain("varchar(max)").And.NotContain("(-1)").And.Contain(">int<");
        rig.Client.Verify(c => c.ListSqlSchemasAsync("conn-1", It.IsAny<CancellationToken>()), Times.Once);
        rig.Client.Verify(c => c.ListSqlTablesAsync("conn-1", "sales", It.IsAny<CancellationToken>()), Times.Once);
        rig.Client.Verify(c => c.ListSqlColumnsAsync("conn-1", "sales", "Orders", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Collapsing_and_re_expanding_reuses_the_loaded_node_and_sends_no_further_request()
    {
        await using var rig = await RenderAsync(client =>
        {
            Schemas("dbo")(client);
            Tables(client, "dbo", new SqlTableDto("dbo", "Orders", false));
            Columns(client, "dbo", "Orders", new SqlColumnDto("Id", "int", false, 1, null));
        });
        await rig.Do(rig.Browser.BrowseAsync);

        for (var i = 0; i < 4; i++)
        {
            await rig.Do(() => rig.Browser.ToggleSchemaAsync("dbo"));
            await rig.Do(() => rig.Browser.ToggleTableAsync("dbo", "Orders"));
        }

        rig.Client.Verify(c => c.ListSqlTablesAsync("conn-1", "dbo", It.IsAny<CancellationToken>()), Times.Once);
        rig.Client.Verify(c => c.ListSqlColumnsAsync("conn-1", "dbo", "Orders", It.IsAny<CancellationToken>()), Times.Once);
        rig.Client.Invocations.Should().HaveCount(3, "one request per node, however often the tree is expanded and collapsed");
    }

    [Fact]
    public async Task A_double_invocation_of_the_first_request_sends_only_one()
    {
        var gate = new TaskCompletionSource<SqlStructureResponseDto<SqlSchemaDto>>();
        await using var rig = await RenderAsync(client =>
            client.Setup(c => c.ListSqlSchemasAsync("conn-1", It.IsAny<CancellationToken>())).Returns(gate.Task));

        var first = rig.Do(rig.Browser.BrowseAsync);
        var second = rig.Do(rig.Browser.BrowseAsync);
        gate.SetResult(Ok(new SqlSchemaDto("dbo")));
        await Task.WhenAll(first, second);

        rig.Client.Verify(c => c.ListSqlSchemasAsync("conn-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- Caps and empty states -------------------------------------------------------------------------

    [Fact]
    public async Task A_truncated_list_says_so_honestly()
    {
        await using var rig = await RenderAsync(client =>
            client.Setup(c => c.ListSqlSchemasAsync("conn-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SqlStructureResponseDto<SqlSchemaDto>("Succeeded", [new SqlSchemaDto("dbo")], true, 500, null)));

        await rig.Do(rig.Browser.BrowseAsync);

        rig.Html.Should().Contain("Showing the first 500 schemas. More exist that are not shown.");
    }

    [Fact]
    public async Task An_empty_result_is_stated_not_left_blank()
    {
        await using var rig = await RenderAsync(client =>
        {
            Schemas("dbo")(client);
            Tables(client, "dbo");
        });
        await rig.Do(rig.Browser.BrowseAsync);
        await rig.Do(() => rig.Browser.ToggleSchemaAsync("dbo"));

        rig.Html.Should().Contain("No tables or views in this schema.");
    }

    // ---- Failures --------------------------------------------------------------------------------------

    [Fact]
    public async Task SF8_a_remote_refusal_shows_the_fixed_message_and_offers_a_retry_that_asks_again()
    {
        var calls = 0;
        await using var rig = await RenderAsync(client =>
            client.Setup(c => c.ListSqlSchemasAsync("conn-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => ++calls == 1
                    ? new SqlStructureResponseDto<SqlSchemaDto>("RemoteDenied", [], false, 500, "The database refused this request for the login.")
                    : Ok(new SqlSchemaDto("dbo"))));

        await rig.Do(rig.Browser.BrowseAsync);
        rig.Html.Should().Contain("The database refused this request for the login.").And.Contain("Try again");

        await rig.Do(rig.Browser.RetrySchemasAsync);
        rig.Html.Should().Contain("dbo").And.NotContain("refused");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task A_failed_request_shows_a_generic_fixed_message_and_never_the_exception_text()
    {
        await using var rig = await RenderAsync(client =>
            client.Setup(c => c.ListSqlSchemasAsync("conn-1", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Data Source=db.example;Password=hunter2 returned 500")));

        await rig.Do(rig.Browser.BrowseAsync);

        var html = rig.Html;
        html.Should().Contain("The request could not be completed.");
        html.Should().NotContain("hunter2").And.NotContain("db.example").And.NotContain("500");
    }

    [Fact]
    public async Task A_failure_deep_in_the_tree_leaves_the_rest_of_the_tree_intact()
    {
        await using var rig = await RenderAsync(client =>
        {
            Schemas("dbo", "sales")(client);
            client.Setup(c => c.ListSqlTablesAsync("conn-1", "sales", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("boom"));
            Tables(client, "dbo", new SqlTableDto("dbo", "Orders", false));
        });
        await rig.Do(rig.Browser.BrowseAsync);
        await rig.Do(() => rig.Browser.ToggleSchemaAsync("dbo"));

        await rig.Do(() => rig.Browser.ToggleSchemaAsync("sales"));

        rig.Html.Should().Contain("Orders", "the other schema's tables are unaffected").And.Contain("The request could not be completed.");
    }

    [Fact]
    public async Task M5_a_request_that_times_out_is_a_retryable_failure_not_a_node_stuck_on_loading()
    {
        var calls = 0;
        await using var rig = await RenderAsync(client =>
        {
            Schemas("dbo")(client);
            client.Setup(c => c.ListSqlTablesAsync("conn-1", "dbo", It.IsAny<CancellationToken>()))
                .Returns(() => ++calls == 1
                    ? Task.FromException<SqlStructureResponseDto<SqlTableDto>>(new TaskCanceledException("HttpClient.Timeout elapsed"))
                    : Task.FromResult(Ok(new SqlTableDto("dbo", "Orders", false))));
        });
        await rig.Do(rig.Browser.BrowseAsync);

        await rig.Do(() => rig.Browser.ToggleSchemaAsync("dbo"));
        rig.Html.Should().Contain("The request could not be completed.").And.Contain("Try again").And.NotContain("Loading…");

        await rig.Do(() => rig.Browser.RetryTablesAsync("dbo"));
        rig.Html.Should().Contain("Orders").And.NotContain("The request could not be completed.");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task I2_a_busy_response_is_explained_and_can_be_retried()
    {
        var calls = 0;
        await using var rig = await RenderAsync(client =>
            client.Setup(c => c.ListSqlSchemasAsync("conn-1", It.IsAny<CancellationToken>()))
                .Returns(() => ++calls == 1
                    ? Task.FromException<SqlStructureResponseDto<SqlSchemaDto>>(new HttpRequestException("busy", null, System.Net.HttpStatusCode.TooManyRequests))
                    : Task.FromResult(Ok(new SqlSchemaDto("dbo")))));

        await rig.Do(rig.Browser.BrowseAsync);
        rig.Html.Should().Contain("Too many structure requests are running for this connection. Try again in a moment.").And.Contain("Try again");

        await rig.Do(rig.Browser.RetrySchemasAsync);
        rig.Html.Should().Contain("dbo").And.NotContain("Too many structure requests");
    }

    // ---- Lifecycle -------------------------------------------------------------------------------------

    [Fact]
    public async Task Leaving_the_page_cancels_an_in_flight_request_and_nothing_is_raised()
    {
        CancellationToken seen = default;
        var started = new TaskCompletionSource();
        await using var rig = await RenderAsync(client =>
            client.Setup(c => c.ListSqlSchemasAsync("conn-1", It.IsAny<CancellationToken>()))
                .Returns(async (string _, CancellationToken ct) =>
                {
                    seen = ct;
                    started.SetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                    return Ok<SqlSchemaDto>();
                }));

        var pending = rig.Do(rig.Browser.BrowseAsync);
        await started.Task;
        await rig.Renderer.Dispatcher.InvokeAsync(rig.Browser.Dispose);
        await pending;

        seen.IsCancellationRequested.Should().BeTrue();
    }
}
