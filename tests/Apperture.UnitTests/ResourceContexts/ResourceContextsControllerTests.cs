using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Api.Controllers;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using ResourceContextDto = Apperture.Management.Api.Controllers.ResourceContextsController.ResourceContextDto;
using CreateResourceContextRequest = Apperture.Management.Api.Controllers.ResourceContextsController.CreateResourceContextRequest;
using UpdateResourceContextRequest = Apperture.Management.Api.Controllers.ResourceContextsController.UpdateResourceContextRequest;
using ResourceContextOwnershipSummaryDto = Apperture.Management.Api.Controllers.ResourceContextsController.ResourceContextOwnershipSummaryDto;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// <see cref="ResourceContextsController"/> (CTX-3A §9.1 A4): every mutating action delegates
/// authorization/audit entirely to <see cref="IResourceContextManagementService"/> (Task 5) -- the
/// controller itself performs zero permission checks beyond <c>[Authorize]</c> for reads, and only its
/// own additional per-caller read-permission probes for the summary-masking endpoint (§9.2).
/// </summary>
public sealed class ResourceContextsControllerTests
{
    // -------------------------------------------------------------------------------------------
    // List / Get -- reads require only [Authorize], never resource_context.manage
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task List_RequiresOnlyAuthentication_NotResourceContextManage()
    {
        await using var harness = ManagementServiceTestHarness.WithNoPermissions();
        var controller = NewController(harness.Service);

        var result = await controller.List(includeArchived: false, CancellationToken.None);

        var dtos = OkValue<IReadOnlyList<ResourceContextDto>>(result.Result);
        dtos.Should().Contain(d => d.Id == ResourceContextIds.SharedValue);
    }

    [Fact]
    public async Task Get_WhenFound_ReturnsOkRegardlessOfPermission()
    {
        await using var harness = ManagementServiceTestHarness.WithNoPermissions();
        var controller = NewController(harness.Service);

        var result = await controller.Get(ResourceContextIds.SharedValue, CancellationToken.None);

        OkValue<ResourceContextDto>(result.Result).Slug.Should().Be("shared");
    }

    [Fact]
    public async Task Get_WhenMissing_ReturnsNotFound()
    {
        await using var harness = ManagementServiceTestHarness.WithNoPermissions();
        var controller = NewController(harness.Service);

        var result = await controller.Get("does-not-exist", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    // -------------------------------------------------------------------------------------------
    // Create
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_WithoutPermission_Returns403()
    {
        await using var harness = ManagementServiceTestHarness.WithNoPermissions();
        var controller = NewController(harness.Service);

        var result = await controller.Create(new CreateResourceContextRequest("Canvas", null, null), CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Create_WithPermission_Returns201AndTheCreatedContext()
    {
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var controller = NewController(harness.Service);

        var result = await controller.Create(new CreateResourceContextRequest("Canvas", null, null), CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        ((ResourceContextDto)created.Value!).Name.Should().Be("Canvas");
    }

    [Fact]
    public async Task Create_WithInvalidName_Returns400()
    {
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var controller = NewController(harness.Service);

        var result = await controller.Create(new CreateResourceContextRequest("   ", null, null), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // -------------------------------------------------------------------------------------------
    // Update
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Update_WithoutPermission_Returns403()
    {
        await using var harness = ManagementServiceTestHarness.WithNoPermissions();
        var controller = NewController(harness.Service);

        var result = await controller.Update(ResourceContextIds.SharedValue, new UpdateResourceContextRequest("Renamed", null, null), CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Update_OnShared_Returns409()
    {
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var controller = NewController(harness.Service);

        var result = await controller.Update(ResourceContextIds.SharedValue, new UpdateResourceContextRequest("Renamed", null, null), CancellationToken.None);

        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Update_UnknownId_Returns404()
    {
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var controller = NewController(harness.Service);

        var result = await controller.Update("does-not-exist", new UpdateResourceContextRequest("X", null, null), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Update_WithPermission_Returns200AndUpdatedContext()
    {
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);
        var controller = NewController(harness.Service);

        var result = await controller.Update(created.Id.Value, new UpdateResourceContextRequest("Renamed", null, null), CancellationToken.None);

        OkValue<ResourceContextDto>(result.Result).Name.Should().Be("Renamed");
    }

    // -------------------------------------------------------------------------------------------
    // Archive / Unarchive
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Archive_WithoutPermission_Returns403()
    {
        await using var harness = ManagementServiceTestHarness.WithNoPermissions();
        var controller = NewController(harness.Service);

        var result = await controller.Archive(ResourceContextIds.SharedValue, CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Archive_UnknownId_Returns404()
    {
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var controller = NewController(harness.Service);

        var result = await controller.Archive("does-not-exist", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Archive_WhenBlocked_Returns409WithOwnershipSummaryBody()
    {
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);
        await harness.SeedLiveIntegration(created.Id);
        var controller = NewController(harness.Service);

        var result = await controller.Archive(created.Id.Value, CancellationToken.None);

        var conflict = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        var body = (ResourceContextOwnershipSummaryDto)conflict.Value!;
        body.LiveIntegrations.Should().Be(1);
    }

    [Fact]
    public async Task Archive_WhenNotBlocked_Returns200AndArchivedContext()
    {
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);
        var controller = NewController(harness.Service);

        var result = await controller.Archive(created.Id.Value, CancellationToken.None);

        OkValue<ResourceContextDto>(result.Result).State.Should().Be(ResourceContextStates.Archived);
    }

    [Fact]
    public async Task Unarchive_UnknownId_Returns404()
    {
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var controller = NewController(harness.Service);

        var result = await controller.Unarchive("does-not-exist", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Unarchive_WithPermission_Returns200AndActiveContext()
    {
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);
        await harness.Service.ArchiveAsync(created.Id, CancellationToken.None);
        var controller = NewController(harness.Service);

        var result = await controller.Unarchive(created.Id.Value, CancellationToken.None);

        OkValue<ResourceContextDto>(result.Result).State.Should().Be(ResourceContextStates.Active);
    }

    // -------------------------------------------------------------------------------------------
    // Summary -- permission-masked per caller (§9.2). The controller must correctly wire whatever
    // the service exposes; this is verified here, not merely trusted from the service layer.
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Summary_MasksCountsTheCallerCannotRead()
    {
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);
        await harness.SeedLiveIntegration(created.Id);
        var controller = NewController(harness.Service, grantedReadPermissions: []);

        var result = await controller.Summary(CancellationToken.None);

        var body = OkValue<IReadOnlyDictionary<string, ResourceContextOwnershipSummaryDto>>(result.Result);
        body.Should().NotBeEmpty();
        body.Values.Should().OnlyContain(s =>
            s.LiveIntegrations == null && s.RetiredIntegrations == null &&
            s.ActiveConnections == null && s.RetiredConnections == null &&
            s.EnabledSchedules == null && s.DisabledSchedules == null);
    }

    [Fact]
    public async Task Summary_ExposesOnlyTheFamiliesTheCallerCanRead()
    {
        await using var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);
        await harness.SeedLiveIntegration(created.Id);
        var controller = NewController(harness.Service, grantedReadPermissions:
        [
            (ActionConstants.IntegrationRead, ResourceTypeConstants.Integration)
        ]);

        var result = await controller.Summary(CancellationToken.None);

        var body = OkValue<IReadOnlyDictionary<string, ResourceContextOwnershipSummaryDto>>(result.Result);
        var summary = body[created.Id.Value];
        summary.LiveIntegrations.Should().Be(1, "the caller holds integration.read");
        summary.ActiveConnections.Should().BeNull("the caller does not hold connection.read");
        summary.EnabledSchedules.Should().BeNull("the caller does not hold schedule.manage");
    }

    // -------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------

    private static T OkValue<T>(IActionResult? result) =>
        (T)result.Should().BeOfType<OkObjectResult>().Subject.Value!;

    private static ResourceContextsController NewController(
        IResourceContextManagementService management,
        IReadOnlyList<(string Action, string ResourceType)>? grantedReadPermissions = null)
    {
        var principal = new Mock<IPrincipalContext>();
        principal.Setup(p => p.GetCurrentActorAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new FixedActor());

        var authorization = new Mock<IAuthorizationEvaluationService>();
        authorization
            .Setup(a => a.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthorizationEvaluationContext ctx, CancellationToken _) =>
                grantedReadPermissions is not null &&
                grantedReadPermissions.Any(p =>
                    string.Equals(p.Action, ctx.Permission.Action, StringComparison.Ordinal) &&
                    string.Equals(p.ResourceType, ctx.Permission.Resource?.ResourceType, StringComparison.Ordinal)));

        return new ResourceContextsController(management, principal.Object, authorization.Object);
    }

    private sealed class FixedActor : IActor
    {
        public ActorId Id => new("test-actor");
        public ActorKind Kind => ActorKind.Human;
        public string? DisplayName => "Test Actor";
    }
}
