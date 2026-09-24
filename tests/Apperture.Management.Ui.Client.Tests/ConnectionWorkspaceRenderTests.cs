using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Client;
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
/// Renders the REAL <c>ConnectionDetail</c> page (Blazor's own renderer, a mocked management client) and asserts on the
/// produced HTML. This is what proves the workspace behaves as designed, rather than merely that its source contains the
/// right words: the connector link, the readable configuration, secret present/absent, health, and graceful fallback.
/// </summary>
public sealed class ConnectionWorkspaceRenderTests
{
    private const string SecretGuid = "3f2a9c1e-77aa-4c1d-9b1e-0a4d0d5b7c11";
    private const string SqlId = "Apperture.Platform.Sql";

    private static ConnectionDto Connection(
        string state = "Active",
        string status = "NeverTested",
        bool supportsVerification = true,
        string? connectorDisplayName = "Platform SQL",
        string connectorId = SqlId,
        string contextId = "shared",
        string? summary = null,
        DateTimeOffset? verifiedAt = null,
        IReadOnlyDictionary<string, string>? secretRefs = null) =>
        new(
            "conn-0001", connectorId, "Warehouse",
            new Dictionary<string, string> { ["Host"] = "db.example", ["Database"] = "Ops" },
            secretRefs ?? new Dictionary<string, string> { ["Password"] = SecretGuid },
            null, null, state, supportsVerification, status, verifiedAt, summary, [],
            ResourceContextId: contextId,
            ConnectorDisplayName: connectorDisplayName);

    private static ConnectorDefinitionCatalogDto Definition() =>
        new(
            SqlId, "Platform SQL", null, "SQL Server connection",
            [
                new ConnectionFieldCatalogDto("Host", "Server name", false, "Host name or address of the SQL Server.", true),
                new ConnectionFieldCatalogDto("Database", "Database", false, null),
                new ConnectionFieldCatalogDto("Password", "Password", true, "Stored securely."),
            ],
            "SupportsConnectionVerification");

    private sealed class Harness
    {
        public required Mock<IManagementClient> Client { get; init; }
        public required string Html { get; init; }
    }

    private static async Task<Harness> RenderAsync(
        ConnectionDto? connection,
        ConnectorDefinitionCatalogDto? definition,
        OperatorCapabilitiesDto? capabilities = null,
        Exception? definitionFailure = null)
    {
        capabilities ??= new OperatorCapabilitiesDto(false, false, false, false, false)
            with { CanViewConnections = true, CanViewConnectorDefinitions = true };

        var client = new Mock<IManagementClient>(MockBehavior.Loose);
        client.Setup(c => c.GetConnectionAsync("conn-0001", It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        if (definitionFailure is not null)
            client.Setup(c => c.GetConnectorDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(definitionFailure);
        else
            client.Setup(c => c.GetConnectorDefinitionAsync(SqlId, It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        client.Setup(c => c.GetResourceContextsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[
                new ResourceContextDto(ResourceContextIds.SharedValue, "Shared", "shared", ResourceContextKinds.Shared, ResourceContextStates.Active, true, null, default, default)]);

        var caps = new Mock<IOperatorCapabilitiesService>();
        caps.Setup(c => c.GetCapabilitiesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(capabilities);

        var nav = new TestNavigationManager("https://localhost/", "https://localhost/connections/conn-0001");
        var session = new SessionStateService(client.Object);
        var js = new FakeJsRuntime();

        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(client.Object)
            .AddSingleton(caps.Object)
            .AddSingleton<NavigationManager>(nav)
            .AddSingleton<IJSRuntime>(js)
            .AddSingleton(session)
            .AddSingleton(new ShellOrientationService())
            .AddSingleton<IToastService>(new ToastService())
            .AddSingleton(new ResourceContextViewState(js, nav, client.Object, session))
            .BuildServiceProvider();

        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<ConnectionDetail>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(ConnectionDetail.ConnectionId)] = "conn-0001" }));
            return output.ToHtmlString();
        });

        return new Harness { Client = client, Html = html };
    }

    [Fact]
    public async Task The_workspace_shows_the_connector_by_name_and_links_to_its_component()
    {
        var page = await RenderAsync(Connection(), Definition());

        page.Html.Should().Contain("href=\"/components/connector/Apperture.Platform.Sql\"");
        page.Html.Should().Contain(">Platform SQL</a>");
    }

    [Fact]
    public async Task Configuration_uses_the_declared_labels_and_descriptions_and_secrets_are_only_present_or_absent()
    {
        var page = await RenderAsync(Connection(), Definition());

        page.Html.Should().Contain("Server name").And.Contain("Host name or address of the SQL Server.").And.Contain("db.example");
        page.Html.Should().Contain(">Set<", "the stored secret is reported as present");
        page.Html.Should().NotContain(SecretGuid, "a secret reference never reaches the page");
        page.Html.Should().NotContain("Host = db.example", "the raw key = value dump is gone");
    }

    [Fact]
    public async Task An_absent_secret_reads_not_set_and_a_declared_field_with_no_value_reads_not_set()
    {
        var page = await RenderAsync(Connection(secretRefs: new Dictionary<string, string>()), Definition());

        page.Html.Should().Contain(">Not set<");
    }

    [Fact]
    public async Task Raw_identifiers_are_inside_the_collapsed_technical_details_only()
    {
        var page = await RenderAsync(Connection(), Definition());

        var technical = page.Html[page.Html.IndexOf("ui-technical-details", StringComparison.Ordinal)..];
        technical.Should().Contain("conn-0001").And.Contain(SqlId);
        page.Html[..page.Html.IndexOf("ui-technical-details", StringComparison.Ordinal)]
            .Should().NotContain("conn-0001", "the opaque connection id is not part of the headline content");
    }

    [Fact]
    public async Task Health_shows_a_failed_verification_as_a_fact_and_the_rest_of_the_page_still_renders()
    {
        var page = await RenderAsync(
            Connection(status: "Failed", summary: "SQL verification failed: login timeout", verifiedAt: new DateTimeOffset(2026, 9, 24, 8, 30, 0, TimeSpan.Zero)),
            Definition());

        page.Html.Should().Contain("Verification failed").And.Contain("login timeout").And.Contain("Last tested 2026-09-24 08:30:00Z");
        page.Html.Should().Contain("Configuration").And.Contain("Used in");
    }

    [Fact]
    public async Task An_unregistered_connector_loads_shows_the_notice_and_never_asks_for_a_definition()
    {
        var page = await RenderAsync(
            Connection(connectorDisplayName: null, connectorId: "Gone.Connector", supportsVerification: false),
            definition: null);

        page.Html.Should().Contain(ConnectionWorkspacePresentation.UnregisteredConnectorNotice);
        page.Html.Should().Contain("Gone.Connector");
        page.Html.Should().NotContain("href=\"/components/connector/Gone.Connector\"");
        page.Html.Should().Contain("Host", "the stored settings still render by key").And.Contain("db.example");
        page.Client.Verify(c => c.GetConnectorDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_failing_definition_lookup_degrades_to_stored_keys_instead_of_failing_the_page()
    {
        var page = await RenderAsync(Connection(), Definition(), definitionFailure: new System.Text.Json.JsonException("not json"));

        page.Html.Should().Contain("Host").And.Contain("db.example").And.Contain("Configuration");
        page.Html.Should().NotContain("Server name");
    }

    [Fact]
    public async Task A_caller_who_cannot_read_definitions_is_not_sent_the_request_and_sees_the_name_unlinked()
    {
        var page = await RenderAsync(
            Connection(), Definition(),
            new OperatorCapabilitiesDto(false, false, false, false, false) with { CanViewConnections = true });

        page.Client.Verify(c => c.GetConnectorDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        page.Html.Should().Contain("Platform SQL").And.NotContain("href=\"/components/connector/");
    }

    [Fact]
    public async Task Loading_the_workspace_never_runs_a_connection_test()
    {
        var page = await RenderAsync(Connection(), Definition());

        page.Client.Verify(c => c.TestConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_caller_without_connection_read_gets_the_forbidden_state_and_no_connection_is_fetched()
    {
        var page = await RenderAsync(Connection(), Definition(), new OperatorCapabilitiesDto(false, false, false, false, false));

        page.Html.Should().Contain("You do not have permission to view connection details.");
        page.Client.Verify(c => c.GetConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
