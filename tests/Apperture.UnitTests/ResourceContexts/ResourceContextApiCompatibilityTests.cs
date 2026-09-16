using System.Text.Json;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using ApiCreateConnectionRequest = Apperture.Core.Application.Connector.CreateConnectionRequest;
using ApiCreateIntegrationRequest = Apperture.Core.Application.Integration.CreateIntegrationRequest;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// Existing clients keep working: creation contracts accept an optional ResourceContextId, list
/// endpoints default to the same global sets, and id-based routes are unchanged.
/// </summary>
public sealed class ResourceContextApiCompatibilityTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // -------------------------------------------------------------------------------------------
    // Wire compatibility
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void LegacyCreateIntegrationBody_WithoutResourceContextId_StillBinds()
    {
        var body = JsonSerializer.Deserialize<CreateIntegrationRequestBody>(
            """{"displayName":"Payments","moduleId":"Sample.Mod","scopeKey":null,"settings":null}""", Json);

        body.Should().NotBeNull();
        body!.DisplayName.Should().Be("Payments");
        body.ResourceContextId.Should().BeNull("omission means the reserved Shared context");
    }

    [Fact]
    public void LegacyCreateConnectionBody_WithoutResourceContextId_StillBinds()
    {
        var body = JsonSerializer.Deserialize<CreateConnectionRequestBody>(
            """{"connectorDefinitionId":"platform.sql","name":"Ops","config":null,"secretRefs":null,"scopeKey":null,"ownerId":null}""", Json);

        body.Should().NotBeNull();
        body!.Name.Should().Be("Ops");
        body.ResourceContextId.Should().BeNull();
    }

    [Fact]
    public void LegacyCreateScheduleBodies_WithoutResourceContextId_StillBind()
    {
        JsonSerializer.Deserialize<CreateScheduleRequestBody>(
            """{"name":"Nightly","recurrenceExpression":"0 0 * * *","integrationId":"int-1"}""", Json)!
            .ResourceContextId.Should().BeNull();

        JsonSerializer.Deserialize<CreateIntegrationScheduleRequestBody>(
            """{"name":"Nightly","recurrenceExpression":"0 0 * * *"}""", Json)!
            .ResourceContextId.Should().BeNull("omission means inherit the Integration's ResourceContext");
    }

    [Fact]
    public void CreateBodies_AcceptAnExplicitResourceContextId()
    {
        JsonSerializer.Deserialize<CreateIntegrationRequestBody>(
            """{"displayName":"Payments","moduleId":"Sample.Mod","resourceContextId":"app-a"}""", Json)!
            .ResourceContextId.Should().Be("app-a");

        JsonSerializer.Deserialize<CreateConnectionRequestBody>(
            """{"connectorDefinitionId":"platform.sql","name":"Ops","resourceContextId":"app-a"}""", Json)!
            .ResourceContextId.Should().Be("app-a");
    }

    // -------------------------------------------------------------------------------------------
    // Integrations controller
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task IntegrationsList_WithoutContextId_DoesNotFilter()
    {
        var (service, resourceContexts) = NewIntegrationServiceMocks();
        service
            .Setup(s => s.ListIntegrationsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewIntegration("i-1", ResourceContextIds.Shared), NewIntegration("i-2", new ResourceContextId("app-a"))]);

        var result = await NewIntegrationsController(service, resourceContexts).List(contextId: null, CancellationToken.None);

        var dtos = OkValue<IReadOnlyList<IntegrationDto>>(result.Result);
        dtos.Should().HaveCount(2);
        service.Verify(s => s.ListIntegrationsAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IntegrationsList_WithContextId_FiltersToThatContext()
    {
        var (service, resourceContexts) = NewIntegrationServiceMocks();
        service
            .Setup(s => s.ListIntegrationsAsync(It.Is<ResourceContextId>(c => c.Value == "app-a"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewIntegration("i-2", new ResourceContextId("app-a"))]);

        var result = await NewIntegrationsController(service, resourceContexts).List("app-a", CancellationToken.None);

        OkValue<IReadOnlyList<IntegrationDto>>(result.Result).Should().ContainSingle()
            .Which.ResourceContextId.Should().Be("app-a");
    }

    [Fact]
    public async Task IntegrationsList_WithUnknownContextId_IsRejectedRatherThanSilentlyEmpty()
    {
        var (service, resourceContexts) = NewIntegrationServiceMocks();
        resourceContexts
            .Setup(r => r.GetAsync(It.Is<ResourceContextId>(c => c.Value == "nope"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResourceContext?)null);

        var result = await NewIntegrationsController(service, resourceContexts).List("nope", CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task IntegrationDto_ExposesOwnershipWithoutAPerRowContextQuery()
    {
        var (service, resourceContexts) = NewIntegrationServiceMocks();
        service
            .Setup(s => s.ListIntegrationsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 25).Select(i => NewIntegration($"i-{i}", ResourceContextIds.Shared)).ToList());

        var result = await NewIntegrationsController(service, resourceContexts).List(contextId: null, CancellationToken.None);

        var dtos = OkValue<IReadOnlyList<IntegrationDto>>(result.Result);
        dtos.Should().HaveCount(25);
        dtos.Should().OnlyContain(d => d.ResourceContextId == ResourceContextIds.SharedValue);
        dtos[0].ResourceContext.Should().NotBeNull();
        dtos[0].ResourceContext!.Slug.Should().Be("shared");

        resourceContexts.Verify(
            r => r.ListAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "the ResourceContext dimension is fetched once per request, never once per row");
        resourceContexts.Verify(r => r.GetAsync(It.IsAny<ResourceContextId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IntegrationCreate_ForwardsTheOptionalResourceContextId()
    {
        var (service, resourceContexts) = NewIntegrationServiceMocks();
        ApiCreateIntegrationRequest? captured = null;
        service
            .Setup(s => s.CreateIntegrationAsync(It.IsAny<ApiCreateIntegrationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ApiCreateIntegrationRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(NewIntegration("i-new", new ResourceContextId("app-a")));

        var controller = NewIntegrationsController(service, resourceContexts);

        await controller.Create(new CreateIntegrationRequestBody("Payments", "Sample.Mod", null), CancellationToken.None);
        captured!.ResourceContextId.Should().BeNull("an omitted context defaults to Shared in the service");

        await controller.Create(new CreateIntegrationRequestBody("Payments", "Sample.Mod", null, null, "app-a"), CancellationToken.None);
        captured!.ResourceContextId!.Value.Should().Be("app-a");
    }

    // -------------------------------------------------------------------------------------------
    // Connections controller
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task ConnectionsList_WithoutContextId_DoesNotFilter()
    {
        var (service, resourceContexts, bindings, integrations, connectors) = NewConnectionMocks();
        service
            .Setup(s => s.ListConnectionsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewConnection("c-1", ResourceContextIds.Shared), NewConnection("c-2", new ResourceContextId("app-a"))]);

        var controller = new ConnectionsController(service.Object, bindings.Object, integrations.Object, connectors.Object, resourceContexts.Object, Mock.Of<IResourceOwnershipMoveService>());
        var result = await controller.List(contextId: null, CancellationToken.None);

        OkValue<IReadOnlyList<ConnectionDto>>(result.Result).Should().HaveCount(2);
        service.Verify(s => s.ListConnectionsAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConnectionsList_WithContextId_FiltersAndExposesOwnership()
    {
        var (service, resourceContexts, bindings, integrations, connectors) = NewConnectionMocks();
        service
            .Setup(s => s.ListConnectionsAsync(It.Is<ResourceContextId>(c => c.Value == "shared"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewConnection("c-1", ResourceContextIds.Shared)]);

        var controller = new ConnectionsController(service.Object, bindings.Object, integrations.Object, connectors.Object, resourceContexts.Object, Mock.Of<IResourceOwnershipMoveService>());
        var result = await controller.List("shared", CancellationToken.None);

        var dto = OkValue<IReadOnlyList<ConnectionDto>>(result.Result).Should().ContainSingle().Subject;
        dto.ResourceContextId.Should().Be(ResourceContextIds.SharedValue);
        dto.ResourceContext!.Slug.Should().Be("shared");
    }

    [Fact]
    public async Task ConnectionsList_WithUnknownContextId_IsRejected()
    {
        var (service, resourceContexts, bindings, integrations, connectors) = NewConnectionMocks();
        resourceContexts
            .Setup(r => r.GetAsync(It.Is<ResourceContextId>(c => c.Value == "nope"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResourceContext?)null);

        var controller = new ConnectionsController(service.Object, bindings.Object, integrations.Object, connectors.Object, resourceContexts.Object, Mock.Of<IResourceOwnershipMoveService>());
        var result = await controller.List("nope", CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ConnectionCreate_ForwardsTheOptionalResourceContextId()
    {
        var (service, resourceContexts, bindings, integrations, connectors) = NewConnectionMocks();
        ApiCreateConnectionRequest? captured = null;
        service
            .Setup(s => s.CreateConnectionAsync(It.IsAny<ApiCreateConnectionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ApiCreateConnectionRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(NewConnection("c-new", new ResourceContextId("app-a")));

        var controller = new ConnectionsController(service.Object, bindings.Object, integrations.Object, connectors.Object, resourceContexts.Object, Mock.Of<IResourceOwnershipMoveService>());

        await controller.Create(new CreateConnectionRequestBody("platform.sql", "Ops", null, null, null, null), CancellationToken.None);
        captured!.ResourceContextId.Should().BeNull();

        await controller.Create(
            new CreateConnectionRequestBody("platform.sql", "Ops", null, null, null, null, null, "app-a"),
            CancellationToken.None);
        captured!.ResourceContextId!.Value.Should().Be("app-a");
    }

    // -------------------------------------------------------------------------------------------
    // Integration bindings controller (CTX-3A Plan 4, A8/A9: Context labels on the binding picker
    // and "Used by" rows read these ids, via ResourceContextViewState.DescribeContext).
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetEligibleConnections_IncludesResourceContextIdOnEachRow()
    {
        const string expectedContextId = "app-a";

        var integrationManagement = new Mock<IIntegrationManagementService>();
        var integration = NewIntegration("i-1", new ResourceContextId(expectedContextId));
        integrationManagement
            .Setup(s => s.GetIntegrationAsync(It.Is<IntegrationId>(id => id.Value == "i-1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(integration);

        var bindingService = new Mock<IBindingService>();
        bindingService
            .Setup(s => s.GetEligibleConnectionsAsync(
                integration.ModuleId,
                "x",
                "y",
                It.Is<ResourceContextId?>(c => c!.Value == expectedContextId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewConnection("c-1", new ResourceContextId(expectedContextId))]);

        var controller = new IntegrationBindingsController(
            integrationManagement.Object,
            Mock.Of<IBindingRepository>(),
            bindingService.Object,
            Mock.Of<IModuleSlotRegistry>());

        var result = await controller.GetEligibleConnections("i-1", "x", "y", CancellationToken.None);

        var rows = OkValue<IReadOnlyList<EligibleConnectionResponseRow>>(result.Result);
        rows.Should().ContainSingle(r => r.ResourceContextId == expectedContextId);
    }

    [Fact]
    public async Task ConnectionUsedInEntry_IncludesIntegrationResourceContextId()
    {
        const string boundIntegrationContextId = "A-context-id";

        var (service, resourceContexts, bindings, integrations, connectors) = NewConnectionMocks();
        var connection = NewConnection("c-1", ResourceContextIds.Shared);
        service
            .Setup(s => s.GetConnectionAsync(It.Is<ConnectionId>(id => id.Value == "c-1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        var binding = new Binding(
            new BindingId("b-1"),
            new IntegrationId("i-a"),
            "Sample.Mod",
            "slot-x",
            "contract-y",
            connection.Id,
            null,
            BindingState.Active);
        bindings
            .Setup(b => b.GetByConnectionIdAsync(It.Is<ConnectionId>(id => id.Value == "c-1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([binding]);

        integrations
            .Setup(i => i.GetByIdAsync(It.Is<IntegrationId>(id => id.Value == "i-a"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewIntegration("i-a", new ResourceContextId(boundIntegrationContextId)));

        var controller = new ConnectionsController(service.Object, bindings.Object, integrations.Object, connectors.Object, resourceContexts.Object, Mock.Of<IResourceOwnershipMoveService>());
        var result = await controller.Get("c-1", CancellationToken.None);

        var connectionDto = OkValue<ConnectionDto>(result.Result);
        connectionDto.UsedIn.Should().ContainSingle(e => e.IntegrationResourceContextId == boundIntegrationContextId);
    }

    // -------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------

    private static T OkValue<T>(IActionResult? result) =>
        (T)result.Should().BeOfType<OkObjectResult>().Subject.Value!;

    private static Core.Domain.Integration.Integration NewIntegration(string id, ResourceContextId resourceContextId) =>
        new(new IntegrationId(id), id, "Sample.Mod", null, IntegrationState.Active, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
        {
            ResourceContextId = resourceContextId
        };

    private static Connection NewConnection(string id, ResourceContextId resourceContextId) =>
        new(
            new ConnectionId(id),
            new ConnectorDefinitionId("platform.sql"),
            id,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            null,
            null)
        {
            ResourceContextId = resourceContextId
        };

    private static Mock<IResourceContextService> NewResourceContextsMock()
    {
        var mock = new Mock<IResourceContextService>();
        var shared = InMemoryResourceContextRepository.SharedContext();
        mock.Setup(r => r.ListAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([shared]);
        mock.Setup(r => r.GetAsync(It.IsAny<ResourceContextId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResourceContextId id, CancellationToken _) =>
                id.Value == ResourceContextIds.SharedValue
                    ? shared
                    : new ResourceContext(id, id.Value, id.Value, ResourceContextKinds.Application, ResourceContextStates.Active, false, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        return mock;
    }

    private static (Mock<IIntegrationManagementService> Service, Mock<IResourceContextService> ResourceContexts) NewIntegrationServiceMocks() =>
        (new Mock<IIntegrationManagementService>(), NewResourceContextsMock());

    private static IntegrationsController NewIntegrationsController(
        Mock<IIntegrationManagementService> service,
        Mock<IResourceContextService> resourceContexts) =>
        new(
            service.Object,
            Mock.Of<IIntegrationReadinessService>(),
            Mock.Of<IIntegrationConfigurationRegistry>(),
            Mock.Of<IIntegrationScheduleDiagnosticsService>(),
            Mock.Of<IAttentionIncidentTracker>(),
            Mock.Of<IAttentionRoutingManagementService>(),
            Mock.Of<IPrincipalContext>(),
            TimeProvider.System,
            resourceContexts.Object,
            Mock.Of<IResourceOwnershipMoveService>());

    private static (
        Mock<IConnectionManagementService> Service,
        Mock<IResourceContextService> ResourceContexts,
        Mock<IBindingRepository> Bindings,
        Mock<IIntegrationRepository> Integrations,
        Mock<IConnectorDefinitionRegistry> Connectors) NewConnectionMocks()
    {
        var bindings = new Mock<IBindingRepository>();
        bindings.Setup(b => b.GetByConnectionIdAsync(It.IsAny<ConnectionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return (new Mock<IConnectionManagementService>(), NewResourceContextsMock(), bindings, new Mock<IIntegrationRepository>(), new Mock<IConnectorDefinitionRegistry>());
    }
}
