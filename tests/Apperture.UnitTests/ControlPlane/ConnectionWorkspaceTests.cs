using Apperture.Core.Application.Connector;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using ClientConnectionDto = Apperture.Management.Client.ConnectionDto;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Connection workspace read contract. The workspace is the existing <c>/connections/{id}</c> page, so its read
/// model is <see cref="ConnectionDto"/>: identity stays <c>ConnectionId</c>, ownership stays
/// <c>Connection.ResourceContextId</c>, and the connector is reached through <c>ConnectorDefinitionId</c> alone.
/// </summary>
public sealed class ConnectionWorkspaceTests
{
    private static readonly ConnectorDefinitionId Sql = new("Apperture.Platform.Sql");

    private static Connection NewConnection(string id, ConnectorDefinitionId connector, ResourceContextId? context = null) =>
        new(
            new ConnectionId(id),
            connector,
            id,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            null,
            null)
        {
            ResourceContextId = context ?? ResourceContextIds.Shared
        };

    private static (ConnectionsController Controller, Mock<IConnectionManagementService> Service, Mock<IConnectorDefinitionRegistry> Registry)
        Build(params ConnectorDefinition[] registered)
    {
        var service = new Mock<IConnectionManagementService>();
        var bindings = new Mock<IBindingRepository>();
        bindings.Setup(b => b.GetByConnectionIdAsync(It.IsAny<ConnectionId>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var contexts = new Mock<IResourceContextService>();
        contexts.Setup(r => r.ListAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        contexts.Setup(r => r.GetAsync(It.IsAny<ResourceContextId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResourceContext?)null);
        var registry = new Mock<IConnectorDefinitionRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.Get(It.IsAny<ConnectorDefinitionId>()))
            .Returns((ConnectorDefinitionId id) => registered.FirstOrDefault(d => d.Id == id));
        var controller = new ConnectionsController(
            service.Object, bindings.Object, new Mock<IIntegrationRepository>().Object,
            registry.Object, contexts.Object, Mock.Of<IResourceOwnershipMoveService>());
        return (controller, service, registry);
    }

    private static T OkValue<T>(IActionResult? result) =>
        (T)result.Should().BeOfType<OkObjectResult>().Subject.Value!;

    private static ConnectorDefinition SqlDefinition() =>
        new(Sql, "Platform SQL", null, new Dictionary<string, string>());

    // CP4 / DV3 -------------------------------------------------------------------------------------

    [Fact]
    public async Task CP4_get_carries_the_connector_display_name_from_the_in_memory_registry()
    {
        var (controller, service, registry) = Build(SqlDefinition());
        service.Setup(s => s.GetConnectionAsync(It.Is<ConnectionId>(i => i.Value == "c-1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewConnection("c-1", Sql));

        var dto = OkValue<ConnectionDto>((await controller.Get("c-1", CancellationToken.None)).Result);

        dto.ConnectorDisplayName.Should().Be("Platform SQL");
        dto.ConnectorDefinitionId.Should().Be("Apperture.Platform.Sql", "the raw id is still carried for TechnicalDetails and the Component route");
        registry.Verify(r => r.Get(Sql), Times.AtLeastOnce);
    }

    [Fact]
    public async Task CP4_list_derives_the_display_name_per_row_from_the_registry_only()
    {
        var (controller, service, registry) = Build(SqlDefinition());
        service.Setup(s => s.ListConnectionsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewConnection("c-1", Sql), NewConnection("c-2", Sql), NewConnection("c-3", new ConnectorDefinitionId("Gone.Connector"))]);

        var rows = OkValue<IReadOnlyList<ConnectionDto>>((await controller.List(null, CancellationToken.None)).Result);

        rows.Select(r => r.ConnectorDisplayName).Should().Equal("Platform SQL", "Platform SQL", null);
        // The registry is the only connector source the list path touches (strict mock: any other member would throw).
        registry.VerifyAll();
    }

    [Fact]
    public async Task CP5_an_unregistered_connector_yields_no_display_name_and_does_not_throw()
    {
        var (controller, service, _) = Build();
        service.Setup(s => s.GetConnectionAsync(It.IsAny<ConnectionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewConnection("c-1", new ConnectorDefinitionId("Gone.Connector")));

        var dto = OkValue<ConnectionDto>((await controller.Get("c-1", CancellationToken.None)).Result);

        dto.ConnectorDisplayName.Should().BeNull("no name is fabricated for a connector this host does not register");
        dto.ConnectorDefinitionId.Should().Be("Gone.Connector");
        dto.SupportsVerification.Should().BeFalse();
    }

    [Fact]
    public async Task Ownership_still_comes_from_the_connection_row()
    {
        var (controller, service, _) = Build(SqlDefinition());
        service.Setup(s => s.GetConnectionAsync(It.IsAny<ConnectionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewConnection("c-1", Sql, new ResourceContextId("app-a")));

        var dto = OkValue<ConnectionDto>((await controller.Get("c-1", CancellationToken.None)).Result);

        dto.ResourceContextId.Should().Be("app-a");
    }

    [Fact]
    public void The_client_twin_exposes_the_display_name()
    {
        typeof(ClientConnectionDto).GetProperty(nameof(ConnectionDto.ConnectorDisplayName)).Should().NotBeNull();
    }
}
