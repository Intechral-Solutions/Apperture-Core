using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Notifications;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Client;
using Apperture.UnitTests.ResourceContexts;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// <c>GET api/resource-contexts/{id}/resources</c>: the operational "what does this Context currently own"
/// endpoint. It is deliberately separate from <c>api/inventory</c> (the diagnostic explorer) even though both
/// read the same normalized resource model.
/// </summary>
public sealed class ResourceContextResourcesControllerTests
{
    private static async Task<ResourceContextResourcesResult> OkAsync(Task<ActionResult<ResourceContextResourcesResult>> call)
    {
        var result = await call;
        return result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<ResourceContextResourcesResult>().Subject;
    }

    [Fact]
    public async Task Get_ListsOnlyTheContextsOwnedIntegrationsConnectionsAndSchedules_WithOperatorFacingLabels()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var appA = await harness.CreateApplicationContextAsync("A");
        var appB = await harness.CreateApplicationContextAsync("B");
        await harness.CreateIntegrationAsync(appA.Id, "A integration");
        await harness.CreateConnectionAsync(appA.Id, "A connection");
        await harness.CreateScheduleAsync(appA.Id, "A schedule", enabled: false);
        await harness.CreateIntegrationAsync(appB.Id, "B integration");
        await harness.CreateConnectionAsync(ResourceContextIds.Shared, "Shared connection");
        await harness.CreateNotificationDefinitionAsync("Definition", NotificationDefinitionScopes.Platform);
        var controller = new ResourceContextResourcesController(harness.Inventory, harness.ResourceContexts);

        var result = await OkAsync(controller.Get(appA.Id.Value));

        result.Items.Select(i => (i.Name, i.TypeLabel, i.State)).Should().BeEquivalentTo(
        [
            ("A integration", "Integration", (string?)"Active"),
            ("A connection", "Connection", "Active"),
            ("A schedule", "Schedule", "Disabled")
        ]);
        result.TotalMatching.Should().Be(3);
        result.Items.Should().OnlyContain(i => i.Href.StartsWith("/integrations/") || i.Href.StartsWith("/connections/") || i.Href.StartsWith("/schedules/"));
    }

    [Fact]
    public async Task Get_WithTypeAndQuery_NarrowsTheOwnedSet()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var appA = await harness.CreateApplicationContextAsync("A");
        await harness.CreateIntegrationAsync(appA.Id, "Payroll sync");
        await harness.CreateConnectionAsync(appA.Id, "Payroll database");
        await harness.CreateConnectionAsync(appA.Id, "Warehouse database");
        var controller = new ResourceContextResourcesController(harness.Inventory, harness.ResourceContexts);

        var byType = await OkAsync(controller.Get(appA.Id.Value, type: "connection"));
        var composed = await OkAsync(controller.Get(appA.Id.Value, type: "connection", q: "payroll"));

        byType.Items.Select(i => i.Name).Should().BeEquivalentTo("Payroll database", "Warehouse database");
        composed.Items.Should().ContainSingle().Which.Name.Should().Be("Payroll database");
    }

    [Fact]
    public async Task Get_ForArchivedApplication_StillListsWhatItOwns()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var appA = await harness.CreateApplicationContextAsync("A");
        var connection = await harness.CreateConnectionAsync(appA.Id, "A connection");
        await harness.RetireConnectionAsync(connection.Id);
        await harness.ResourceContexts.ArchiveAsync(appA.Id);
        var controller = new ResourceContextResourcesController(harness.Inventory, harness.ResourceContexts);

        var result = await OkAsync(controller.Get(appA.Id.Value));

        result.Items.Should().ContainSingle().Which.State.Should().Be("Retired");
    }

    [Fact]
    public async Task Get_ForSharedContext_ListsSharedOwnedResources()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var appA = await harness.CreateApplicationContextAsync("A");
        await harness.CreateConnectionAsync(ResourceContextIds.Shared, "Shared connection");
        await harness.CreateConnectionAsync(appA.Id, "A connection");
        var controller = new ResourceContextResourcesController(harness.Inventory, harness.ResourceContexts);

        var result = await OkAsync(controller.Get(ResourceContextIds.SharedValue));

        result.Items.Should().ContainSingle().Which.Name.Should().Be("Shared connection");
    }

    /// <summary>
    /// Each family is gated by the same capability the ordinary family surface uses (integration.read,
    /// connection.read, schedule.manage -- see OperatorCapabilitiesQueryService). A family the caller cannot
    /// read yields no rows, no names, no ids, no states and no contribution to TotalMatching, whether it is
    /// omitted from the whole list or asked for directly.
    /// </summary>
    [Theory]
    [InlineData("integration")]
    [InlineData("connection")]
    [InlineData("schedule")]
    public async Task Get_WithAFamilyTheCallerCannotRead_ExposesNothingAboutThatFamily(string hiddenType)
    {
        await using var harness = await InventoryTestHarness.CreateAsync(
            canViewIntegrations: hiddenType != "integration",
            canViewConnections: hiddenType != "connection",
            canManageSchedules: hiddenType != "schedule");
        var appA = await harness.CreateApplicationContextAsync("A");
        await harness.CreateIntegrationAsync(appA.Id, "Hidden-or-visible integration");
        await harness.CreateConnectionAsync(appA.Id, "Hidden-or-visible connection");
        await harness.CreateScheduleAsync(appA.Id, "Hidden-or-visible schedule");
        var controller = new ResourceContextResourcesController(harness.Inventory, harness.ResourceContexts);

        var everything = await OkAsync(controller.Get(appA.Id.Value));
        var asked = await OkAsync(controller.Get(appA.Id.Value, type: hiddenType));
        var searched = await OkAsync(controller.Get(appA.Id.Value, q: "Hidden-or-visible"));

        everything.Items.Select(i => i.Type).Should().BeEquivalentTo(
            ResourceContextResourceTypes.All.Where(t => t != hiddenType));
        everything.TotalMatching.Should().Be(2);
        asked.Items.Should().BeEmpty();
        asked.TotalMatching.Should().Be(0);
        searched.Items.Should().NotContain(i => i.Type == hiddenType);
        searched.TotalMatching.Should().Be(2);
    }

    [Fact]
    public async Task Get_FiltersByTheResolvedContextsCanonicalId_NotTheRawRouteValue()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var appA = await harness.CreateApplicationContextAsync("A");
        var contexts = new Mock<IResourceContextService>();
        contexts.Setup(c => c.GetAsync(It.IsAny<ResourceContextId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appA);
        var inventory = new Mock<IInventoryQueryService>();
        InventoryQuery? captured = null;
        inventory.Setup(i => i.QueryAsync(It.IsAny<InventoryQuery>(), It.IsAny<CancellationToken>()))
            .Callback<InventoryQuery, CancellationToken>((query, _) => captured = query)
            .ReturnsAsync(new Apperture.ControlPlane.Security.InventoryQueryResult([], []));
        var controller = new ResourceContextResourcesController(inventory.Object, contexts.Object);

        // A store with case-insensitive text comparison resolves this value to the same context.
        await OkAsync(controller.Get(appA.Id.Value.ToUpperInvariant()));

        captured!.Context!.ResourceContextId.Should().Be(appA.Id.Value);
    }

    [Fact]
    public async Task Get_WithUnknownContext_Returns404()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var controller = new ResourceContextResourcesController(harness.Inventory, harness.ResourceContexts);

        var result = await controller.Get("not-a-real-context-id");

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Theory]
    [InlineData("module")]
    [InlineData("notificationDefinition")]
    [InlineData("run")]
    [InlineData("nonsense")]
    public async Task Get_WithATypeOutsideTheOperationalFamilies_Returns400(string type)
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var appA = await harness.CreateApplicationContextAsync("A");
        var controller = new ResourceContextResourcesController(harness.Inventory, harness.ResourceContexts);

        var result = await controller.Get(appA.Id.Value, type: type);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void ClientFamilyTokens_MatchTheServersInventoryTypeTokens()
    {
        ResourceContextResourceTypes.Integration.Should().Be(InventoryTypeTokens.Integration);
        ResourceContextResourceTypes.Connection.Should().Be(InventoryTypeTokens.Connection);
        ResourceContextResourceTypes.Schedule.Should().Be(InventoryTypeTokens.Schedule);
        ResourceContextResourceTypes.All.Should().BeEquivalentTo(
            [InventoryTypeTokens.Integration, InventoryTypeTokens.Connection, InventoryTypeTokens.Schedule]);
    }
}
