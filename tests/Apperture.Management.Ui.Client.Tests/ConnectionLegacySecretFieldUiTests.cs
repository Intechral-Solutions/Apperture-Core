using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Pages.Connections;
using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// SEC-2.2 (A2.10): the real connection page, rendered by bUnit, over the <c>secretFields</c> read model. The page learns whether each
/// secret field is configured, and which stored secret keys the connector no longer declares, from <c>secretFields</c> alone; it is
/// given no reference. A legacy key can be removed, and the only request that does so names the key to clear.
/// </summary>
public sealed class ConnectionLegacySecretFieldUiTests : BunitContext
{
    private const string SqlId = "Apperture.Platform.Sql";

    private readonly Mock<IManagementClient> _client = new(MockBehavior.Loose);

    public ConnectionLegacySecretFieldUiTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private static ConnectionDto Connection(params ConnectionSecretFieldDto[] secretFields) =>
        new(
            "conn-0001", SqlId, "Warehouse",
            new Dictionary<string, string> { ["Host"] = "db.example", ["Database"] = "Ops" },
            secretFields,
            null, null, "Active", true, "NeverTested", null, null, [],
            ResourceContextId: "shared",
            ConnectorDisplayName: "Platform SQL");

    private static ConnectorDefinitionCatalogDto Definition() =>
        new(
            SqlId, "Platform SQL", null, "SQL Server connection",
            [
                new ConnectionFieldCatalogDto("Host", "Server name", false, null, true),
                new ConnectionFieldCatalogDto("Database", "Database", false, null),
                new ConnectionFieldCatalogDto("Password", "Password", true, "Stored securely."),
            ],
            "SupportsConnectionVerification");

    private IRenderedComponent<ConnectionDetail> RenderPage(ConnectionDto connection, bool canUpdate)
    {
        _client.Setup(c => c.GetConnectionAsync("conn-0001", It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _client.Setup(c => c.GetConnectorDefinitionAsync(SqlId, It.IsAny<CancellationToken>())).ReturnsAsync(Definition());
        _client.Setup(c => c.GetConnectionToolsAsync("conn-0001", It.IsAny<CancellationToken>())).ReturnsAsync((ConnectionToolsDto?)null);
        _client.Setup(c => c.GetResourceContextsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)
            [
                new ResourceContextDto(ResourceContextIds.SharedValue, "Shared", "shared", ResourceContextKinds.Shared, ResourceContextStates.Active, true, null, default, default),
            ]);
        var capabilities = new Mock<IOperatorCapabilitiesService>();
        capabilities.Setup(c => c.GetCapabilitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorCapabilitiesDto(false, false, false, false, false)
                with { CanViewConnections = true, CanViewConnectorDefinitions = true, CanUpdateConnections = canUpdate });

        var nav = new TestNavigationManager("https://localhost/", "https://localhost/connections/conn-0001");
        var session = new SessionStateService(_client.Object);
        Services.AddSingleton(_client.Object);
        Services.AddSingleton(capabilities.Object);
        Services.AddSingleton<NavigationManager>(nav);
        Services.AddSingleton(new ShellOrientationService());
        Services.AddSingleton<IToastService>(new ToastService());
        Services.AddSingleton(session);
        Services.AddSingleton(new ResourceContextViewState(new FakeJsRuntime(), nav, _client.Object, session));

        return Render<ConnectionDetail>(p => p.Add(x => x.ConnectionId, "conn-0001"));
    }

    private static IReadOnlyList<AngleSharp.Dom.IElement> RemoveButtons(IRenderedComponent<ConnectionDetail> cut) =>
        cut.FindAll("button").Where(b => b.TextContent.Trim() == "Remove").ToList();

    [Fact]
    public void A_stored_key_the_connector_does_not_declare_is_listed_with_a_Remove_action_and_a_declared_field_is_not()
    {
        var cut = RenderPage(Connection(new ConnectionSecretFieldDto("Password", true), new ConnectionSecretFieldDto("OldApiKey", true)), canUpdate: true);

        cut.Markup.Should().Contain("OldApiKey").And.Contain(">Set<");
        RemoveButtons(cut).Should().ContainSingle().Which.Id.Should().Be("connection-secret-remove-OldApiKey");
    }

    [Fact]
    public void A_declared_field_with_nothing_stored_reads_Not_set_and_has_no_Remove_action()
    {
        var cut = RenderPage(Connection(new ConnectionSecretFieldDto("Password", false)), canUpdate: true);

        cut.Markup.Should().Contain(">Not set<");
        RemoveButtons(cut).Should().BeEmpty();
    }

    [Fact]
    public void Without_permission_to_update_connections_no_Remove_action_is_offered()
    {
        var cut = RenderPage(Connection(new ConnectionSecretFieldDto("OldApiKey", true)), canUpdate: false);

        cut.Markup.Should().Contain("OldApiKey");
        RemoveButtons(cut).Should().BeEmpty();
    }

    [Fact]
    public void Removing_a_legacy_key_sends_only_the_key_to_clear_and_reloads_the_connection()
    {
        var cut = RenderPage(Connection(new ConnectionSecretFieldDto("Password", true), new ConnectionSecretFieldDto("OldApiKey", true)), canUpdate: true);
        _client.Setup(c => c.UpdateConnectionAsync("conn-0001", It.IsAny<UpdateConnectionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Connection(new ConnectionSecretFieldDto("Password", true)));

        RemoveButtons(cut).Single().Click();

        // The click is dispatched on the renderer, so wait for the request rather than assuming it has been made.
        cut.WaitForAssertion(() => _client.Verify(
            c => c.UpdateConnectionAsync(
                "conn-0001",
                It.Is<UpdateConnectionRequest>(r =>
                    r.ClearSecretFields != null && r.ClearSecretFields.SequenceEqual(new[] { "OldApiKey" })
                    && r.SecretValues == null && r.Name == null && r.Config == null),
                It.IsAny<CancellationToken>()),
            Times.Once));
        cut.WaitForAssertion(() => _client.Verify(c => c.GetConnectionAsync("conn-0001", It.IsAny<CancellationToken>()), Times.Exactly(2)));
    }
}
