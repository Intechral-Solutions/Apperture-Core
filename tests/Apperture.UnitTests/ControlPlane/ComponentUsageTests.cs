using Apperture.ControlPlane.Security;
using Apperture.Core.Application.ComponentCatalog;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Notifications;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Current usage on Component DETAIL only. Connector usage is the Active Connections made from the connector;
/// Notification Definition usage is the current subscription count. Both come through authorized services and
/// are absent (never zero) when the caller may not read them.
/// </summary>
public sealed class ComponentUsageTests
{
    private const string SqlId = "Apperture.Platform.Sql";

    private static ConnectorDefinition Connector(string id = SqlId) =>
        new(new ConnectorDefinitionId(id), "Platform SQL", null, new Dictionary<string, string>());

    private static Connection Conn(string id, string name, string connectorId = SqlId, ConnectionState state = ConnectionState.Active) =>
        new(new ConnectionId(id), new ConnectorDefinitionId(connectorId), name,
            new Dictionary<string, string>(), new Dictionary<string, string>(), null, null, state);

    private static Mock<IConnectorDefinitionCatalogReadService> Definitions(params ConnectorDefinition[] definitions)
    {
        var mock = new Mock<IConnectorDefinitionCatalogReadService>();
        mock.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(definitions);
        mock.Setup(s => s.GetByIdAsync(It.IsAny<ConnectorDefinitionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConnectorDefinitionId id, CancellationToken _) => definitions.FirstOrDefault(d => d.Id.Value == id.Value));
        return mock;
    }

    private static Mock<IConnectionManagementService> Connections(params Connection[] connections)
    {
        var mock = new Mock<IConnectionManagementService>(MockBehavior.Strict);
        mock.Setup(s => s.ListConnectionsByConnectorAsync(It.IsAny<ConnectorDefinitionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connections);
        return mock;
    }

    // ---- connector usage ------------------------------------------------------------------------

    [Fact]
    public async Task U1_connector_usage_lists_exactly_the_connections_of_that_connector()
    {
        var connections = Connections(
            Conn("c1", "Reporting DB"),
            Conn("c2", "Finance DB"),
            Conn("other", "Some SFTP", connectorId: "Apperture.Platform.Sftp"));
        var sut = new ConnectorComponentCatalogProvider(Definitions(Connector()).Object, connections.Object);

        var detail = await sut.GetAsync(SqlId);

        var usage = detail!.Usage!.Connector!;
        usage.ActiveConnectionCount.Should().Be(2);
        usage.Connections.Select(c => c.ConnectionId).Should().Equal("c2", "c1");
        usage.Connections.Select(c => c.Name).Should().Equal("Finance DB", "Reporting DB");
        detail.Usage.NotificationDefinition.Should().BeNull();
        connections.Verify(s => s.ListConnectionsByConnectorAsync(new ConnectorDefinitionId(SqlId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task U2_an_authorized_connector_with_no_connections_reports_zero_not_absence()
    {
        var sut = new ConnectorComponentCatalogProvider(Definitions(Connector()).Object, Connections().Object);

        var detail = await sut.GetAsync(SqlId);

        detail!.Usage.Should().NotBeNull();
        detail.Usage!.Connector!.ActiveConnectionCount.Should().Be(0);
        detail.Usage.Connector.Connections.Should().BeEmpty();
    }

    [Fact]
    public async Task U3_retired_connections_are_not_current_usage()
    {
        // Ruling: usage means Active Connections -- the same "live" meaning the ownership summary uses for
        // Connections. A retired Connection is excluded from binding eligibility and is decommissioned
        // configuration, so counting it would make an idle connector look in use.
        var sut = new ConnectorComponentCatalogProvider(
            Definitions(Connector()).Object,
            Connections(
                Conn("active", "Live DB"),
                Conn("retired", "Old DB", state: ConnectionState.Retired)).Object);

        var usage = (await sut.GetAsync(SqlId))!.Usage!.Connector!;

        usage.ActiveConnectionCount.Should().Be(1);
        usage.Connections.Should().ContainSingle().Which.ConnectionId.Should().Be("active");
    }

    [Fact]
    public async Task A7_without_connection_read_the_usage_block_is_absent_not_zero()
    {
        var connections = new Mock<IConnectionManagementService>();
        connections.Setup(s => s.ListConnectionsByConnectorAsync(It.IsAny<ConnectorDefinitionId>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException());
        var sut = new ConnectorComponentCatalogProvider(Definitions(Connector()).Object, connections.Object);

        var detail = await sut.GetAsync(SqlId);

        detail.Should().NotBeNull("the Component itself is still readable");
        detail!.Usage.Should().BeNull();
        detail.Connector.Should().NotBeNull();
    }

    [Fact]
    public async Task Connector_usage_names_at_most_the_documented_limit_but_counts_them_all()
    {
        var many = Enumerable.Range(0, ConnectorComponentCatalogProvider.MaxNamedConnections + 5)
            .Select(i => Conn($"c{i:000}", $"Connection {i:000}"))
            .ToArray();
        var sut = new ConnectorComponentCatalogProvider(Definitions(Connector()).Object, Connections(many).Object);

        var usage = (await sut.GetAsync(SqlId))!.Usage!.Connector!;

        usage.ActiveConnectionCount.Should().Be(ConnectorComponentCatalogProvider.MaxNamedConnections + 5);
        usage.Connections.Should().HaveCount(ConnectorComponentCatalogProvider.MaxNamedConnections);
        usage.Connections.Select(c => c.Name).Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Connector_usage_ignores_connections_of_a_differently_cased_connector_id()
    {
        // SQL Server's default collation is case-insensitive, so the query can return case variants. Connector
        // ids are ordinal keys of the definition registry, so only the exact id is usage.
        var sut = new ConnectorComponentCatalogProvider(
            Definitions(Connector()).Object,
            Connections(
                Conn("exact", "Exact"),
                Conn("variant", "Variant", connectorId: SqlId.ToUpperInvariant())).Object);

        var usage = (await sut.GetAsync(SqlId))!.Usage!.Connector!;

        usage.ActiveConnectionCount.Should().Be(1);
        usage.Connections.Should().ContainSingle().Which.ConnectionId.Should().Be("exact");
    }

    [Fact]
    public async Task Connector_usage_is_not_looked_up_for_an_unknown_connector()
    {
        var connections = Connections();
        var sut = new ConnectorComponentCatalogProvider(Definitions().Object, connections.Object);

        (await sut.GetAsync("missing")).Should().BeNull();
        connections.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task U6_connector_list_never_touches_usage()
    {
        var connections = new Mock<IConnectionManagementService>(MockBehavior.Strict);
        var sut = new ConnectorComponentCatalogProvider(Definitions(Connector()).Object, connections.Object);

        var list = await sut.ListAsync();

        list.Should().ContainSingle();
        connections.VerifyNoOtherCalls();
    }

    // ---- notification definition usage ----------------------------------------------------------

    private static NotificationDefinition Def(string id, string origin, string? moduleId, string scope = NotificationDefinitionScopes.Module) =>
        new(NotificationDefinitionId.From(id), "Definition", null, NotificationSourceKinds.PlatformEvent, origin, scope,
            NotificationDefinitionMutabilities.StaticRuleMutableSubscription, moduleId, null, "ev", true, null, "{}", "{}",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null);

    private static NotificationSubscription Sub(string id) => new(
        new NotificationSubscriptionId(id), "module.x", null, "user", "u-1", "email", "platformBuiltIn", true,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null);

    private static Mock<INotificationDefinitionService> DefinitionService(params NotificationDefinition[] rows)
    {
        var mock = new Mock<INotificationDefinitionService>();
        mock.Setup(s => s.ListAsync(It.IsAny<NotificationDefinitionListQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        mock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => rows.FirstOrDefault(r => r.Id.Value == id));
        return mock;
    }

    private static Mock<INotificationSubscriptionService> Subscriptions(int count)
    {
        var mock = new Mock<INotificationSubscriptionService>(MockBehavior.Strict);
        mock.Setup(s => s.ListByDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, count).Select(i => Sub($"s{i}")).ToList());
        return mock;
    }

    [Fact]
    public async Task U4_definition_usage_is_the_current_subscription_count()
    {
        var subscriptions = Subscriptions(3);
        var sut = new NotificationDefinitionComponentCatalogProvider(
            DefinitionService(Def("module.x", NotificationDefinitionOrigins.ModuleBuiltIn, "Sample.HelloWorld")).Object,
            subscriptions.Object);

        var detail = await sut.GetAsync("module.x");

        detail!.Usage!.NotificationDefinition!.SubscriptionCount.Should().Be(3);
        detail.Usage.Connector.Should().BeNull();
        subscriptions.Verify(s => s.ListByDefinitionAsync("module.x", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task U5_a_definition_with_no_subscriptions_reports_zero_regardless_of_historical_activity()
    {
        // The provider has no NotificationInstance dependency at all (pinned by the architecture test), so
        // historical instances cannot contribute. Zero subscriptions is zero usage.
        var sut = new NotificationDefinitionComponentCatalogProvider(
            DefinitionService(Def("platform.a", NotificationDefinitionOrigins.PlatformBuiltIn, null, NotificationDefinitionScopes.Platform)).Object,
            Subscriptions(0).Object);

        var detail = await sut.GetAsync("platform.a");

        detail!.Usage!.NotificationDefinition!.SubscriptionCount.Should().Be(0);
    }

    [Fact]
    public async Task Definition_usage_is_absent_when_the_subscription_service_denies()
    {
        var subscriptions = new Mock<INotificationSubscriptionService>();
        subscriptions.Setup(s => s.ListByDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException());
        var sut = new NotificationDefinitionComponentCatalogProvider(
            DefinitionService(Def("platform.a", NotificationDefinitionOrigins.PlatformBuiltIn, null, NotificationDefinitionScopes.Platform)).Object,
            subscriptions.Object);

        var detail = await sut.GetAsync("platform.a");

        detail.Should().NotBeNull();
        detail!.Usage.Should().BeNull();
    }

    [Fact]
    public async Task Definition_usage_is_absent_when_the_definition_disappears_mid_request()
    {
        var subscriptions = new Mock<INotificationSubscriptionService>();
        subscriptions.Setup(s => s.ListByDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Notification definition 'platform.a' not found."));
        var sut = new NotificationDefinitionComponentCatalogProvider(
            DefinitionService(Def("platform.a", NotificationDefinitionOrigins.PlatformBuiltIn, null, NotificationDefinitionScopes.Platform)).Object,
            subscriptions.Object);

        var detail = await sut.GetAsync("platform.a");

        detail.Should().NotBeNull("the component was read successfully");
        detail!.Usage.Should().BeNull();
    }

    [Fact]
    public async Task Definition_usage_counts_every_subscription_row_including_disabled_ones()
    {
        // Same rows the authoritative subscriptions section lists, so the two surfaces never disagree.
        var disabled = Sub("s-disabled") with { IsEnabled = false };
        var subscriptions = new Mock<INotificationSubscriptionService>();
        subscriptions.Setup(s => s.ListByDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Sub("s-1"), disabled]);
        var sut = new NotificationDefinitionComponentCatalogProvider(
            DefinitionService(Def("platform.a", NotificationDefinitionOrigins.PlatformBuiltIn, null, NotificationDefinitionScopes.Platform)).Object,
            subscriptions.Object);

        (await sut.GetAsync("platform.a"))!.Usage!.NotificationDefinition!.SubscriptionCount.Should().Be(2);
    }

    [Fact]
    public async Task Usage_is_not_looked_up_for_a_definition_that_is_not_a_component()
    {
        var subscriptions = new Mock<INotificationSubscriptionService>(MockBehavior.Strict);
        var sut = new NotificationDefinitionComponentCatalogProvider(
            DefinitionService(Def("ndef_user", NotificationDefinitionOrigins.UserCreated, null, NotificationDefinitionScopes.Platform)).Object,
            subscriptions.Object);

        (await sut.GetAsync("ndef_user")).Should().BeNull();
        subscriptions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task U6_definition_list_never_touches_usage()
    {
        var subscriptions = new Mock<INotificationSubscriptionService>(MockBehavior.Strict);
        var sut = new NotificationDefinitionComponentCatalogProvider(
            DefinitionService(Def("platform.a", NotificationDefinitionOrigins.PlatformBuiltIn, null, NotificationDefinitionScopes.Platform)).Object,
            subscriptions.Object);

        (await sut.ListAsync()).Should().ContainSingle();
        subscriptions.VerifyNoOtherCalls();
    }

    // ---- catalog + wire -------------------------------------------------------------------------

    [Fact]
    public async Task Catalog_detail_carries_the_provider_usage_through_module_attribution()
    {
        var modules = new Mock<IModuleWorkflowListingService>();
        modules.Setup(m => m.GetModuleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((ModuleCatalogEntry?)null);
        var provider = new NotificationDefinitionComponentCatalogProvider(
            DefinitionService(Def("module.x", NotificationDefinitionOrigins.ModuleBuiltIn, "Sample.HelloWorld")).Object,
            Subscriptions(2).Object);
        var sut = new ComponentCatalogService([provider], modules.Object);

        var detail = await sut.GetAsync(new ComponentKey(ComponentKinds.NotificationDefinition, "module.x"));

        detail!.Usage!.NotificationDefinition!.SubscriptionCount.Should().Be(2);
    }

    [Fact]
    public async Task U6_the_catalog_list_payload_has_no_usage_anywhere()
    {
        var connections = new Mock<IConnectionManagementService>(MockBehavior.Strict);
        var subscriptions = new Mock<INotificationSubscriptionService>(MockBehavior.Strict);
        var sut = new ComponentCatalogService(
            [
                new ConnectorComponentCatalogProvider(Definitions(Connector()).Object, connections.Object),
                new NotificationDefinitionComponentCatalogProvider(
                    DefinitionService(Def("platform.a", NotificationDefinitionOrigins.PlatformBuiltIn, null, NotificationDefinitionScopes.Platform)).Object,
                    subscriptions.Object),
            ],
            new Mock<IModuleWorkflowListingService>(MockBehavior.Strict).Object);

        var result = await sut.QueryAsync(new ComponentCatalogQuery());

        result.Items.Should().HaveCount(2);
        connections.VerifyNoOtherCalls();
        subscriptions.VerifyNoOtherCalls();
        typeof(ComponentSummary).GetProperties().Select(p => p.Name).Should().NotContain(n => n.Contains("Usage") || n.Contains("Count"));
        typeof(Apperture.Management.Client.ComponentSummaryDto).GetProperties().Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Usage") || n.Contains("Count"));
        typeof(Apperture.Management.Client.ComponentCatalogResultDto).GetProperties().Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Usage"));
    }
}
