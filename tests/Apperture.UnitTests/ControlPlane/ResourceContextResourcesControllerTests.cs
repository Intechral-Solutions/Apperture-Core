using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Notifications;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Client;
using Apperture.UnitTests.ResourceContexts;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
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
