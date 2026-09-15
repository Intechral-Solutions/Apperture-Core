using Apperture.ControlPlane.Security;
using Apperture.Management.Api.Controllers;
using Apperture.UnitTests.ResourceContexts;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// CTX-3A Plan 4, Task 9: <see cref="InventoryController"/>'s <c>context</c> query parameter is
/// resolved and validated before querying (mirroring <c>IntegrationsController.ResolveContextFilterAsync</c>
/// and the fleet endpoint's 400 pattern from Task 1).
/// </summary>
public sealed class InventoryControllerTests
{
    [Fact]
    public async Task Get_WithUnknownContextId_Returns400()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var controller = new InventoryController(harness.Inventory, harness.ResourceContexts);

        var result = await controller.Get(context: "not-a-real-context-id");

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Get_NotRecordedWithResourcesMode_Returns400()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var controller = new InventoryController(harness.Inventory, harness.ResourceContexts);

        var result = await controller.Get(mode: InventoryModes.Resources, context: "notRecorded");

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Get_NotRecordedWithActivityMode_Returns200()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var controller = new InventoryController(harness.Inventory, harness.ResourceContexts);

        var result = await controller.Get(mode: InventoryModes.Activity, context: "notRecorded");

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Get_WithShared_Returns200()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var controller = new InventoryController(harness.Inventory, harness.ResourceContexts);

        var result = await controller.Get(context: "shared");

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Get_WithArchivedApplicationId_Returns200()
    {
        await using var harness = await InventoryTestHarness.CreateAsync();
        var app = await harness.CreateApplicationContextAsync("Archived app");
        await harness.ResourceContexts.ArchiveAsync(app.Id);
        var controller = new InventoryController(harness.Inventory, harness.ResourceContexts);

        var result = await controller.Get(context: app.Id.Value);

        result.Result.Should().BeOfType<OkObjectResult>();
    }
}
