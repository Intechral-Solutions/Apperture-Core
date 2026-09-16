using Apperture.Core.Application.Integration;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Client;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using ResourceMoveBlockerCodes = Apperture.Core.Application.ResourceContexts.ResourceMoveBlockerCodes;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// Design gate §8.3's 200/400/409 mapping is controller-only logic. The service-level tests in Plans 1-4
/// observe <c>ResourceMovePreview.CanMove</c> and blocker codes, never HTTP status codes.
/// </summary>
public sealed class ResourceOwnershipMoveControllerTests
{
    private static readonly ResourceContextRef Ref = new("app-a", "App A", "app-a");

    [Fact]
    public async Task Common_blocker_maps_to_400_with_a_plain_error()
    {
        var preview = Blocked(ResourceMoveBlockerCodes.TargetArchived, "archived");
        var controller = BuildController(new StubMoveService(preview));

        var result = await controller.PostOwnershipMove("int-1", new ResourceOwnershipMoveRequest("app-b"), default);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Family_blocker_maps_to_409_with_the_full_preview_body()
    {
        var preview = Blocked(ResourceMoveBlockerCodes.BindingConnectionUnreachable, "unreachable");
        var controller = BuildController(new StubMoveService(preview));

        var result = await controller.PostOwnershipMove("int-1", new ResourceOwnershipMoveRequest("app-b"), default);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().BeOfType<ResourceMovePreviewDto>(
            "the UI needs the blockers and effects, not a bare message");
    }

    [Fact]
    public async Task Success_maps_to_200()
    {
        var preview = new ResourceMovePreview("Integration", "int-1", Ref, Ref, true, [], []);
        var controller = BuildController(new StubMoveService(preview));

        var result = await controller.PostOwnershipMove("int-1", new ResourceOwnershipMoveRequest("app-b"), default);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Ownership_gate_timeout_maps_to_409_with_the_busy_code()
    {
        var controller = BuildController(new ThrowingMoveService(
            new ResourceOwnershipBusyException("Another ownership change is in progress. Wait a moment and try again.")));

        var result = await controller.PostOwnershipMove("int-1", new ResourceOwnershipMoveRequest("app-b"), default);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value!.ToString().Should().Contain(ResourceOwnershipBusyException.BusyCode,
            "a transient ownership conflict must be a retryable 409 with a stable code, never a 500");
    }

    [Fact]
    public async Task Unauthorized_maps_to_403()
    {
        var controller = BuildController(new ThrowingMoveService(new UnauthorizedAccessException()));

        var result = await controller.PostOwnershipMove("int-1", new ResourceOwnershipMoveRequest("app-b"), default);

        result.Should().BeOfType<ForbidResult>();
    }

    private static ResourceMovePreview Blocked(string code, string message) =>
        new("Integration", "int-1", Ref, Ref, false,
            [new ResourceMoveBlocker(code, message, null, null, null, null)], []);

    private static IntegrationsController BuildController(IResourceOwnershipMoveService moveService) =>
        new(
            Mock.Of<IIntegrationManagementService>(),
            Mock.Of<IIntegrationReadinessService>(),
            Mock.Of<IIntegrationConfigurationRegistry>(),
            Mock.Of<IIntegrationScheduleDiagnosticsService>(),
            Mock.Of<IAttentionIncidentTracker>(),
            Mock.Of<IAttentionRoutingManagementService>(),
            Mock.Of<IPrincipalContext>(),
            TimeProvider.System,
            Mock.Of<IResourceContextService>(),
            moveService);

    private sealed class StubMoveService : IResourceOwnershipMoveService
    {
        private readonly ResourceMovePreview _preview;

        public StubMoveService(ResourceMovePreview preview) => _preview = preview;

        public ValueTask<ResourceMovePreview> PreviewIntegrationMoveAsync(IntegrationId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_preview);

        public ValueTask<ResourceMovePreview> PreviewConnectionMoveAsync(Apperture.Core.Domain.Connector.ConnectionId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_preview);

        public ValueTask<ResourceMovePreview> PreviewScheduleMoveAsync(Apperture.Core.Domain.Scheduling.ScheduleId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_preview);

        public ValueTask<ResourceMovePreview> MoveIntegrationAsync(IntegrationId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_preview);

        public ValueTask<ResourceMovePreview> MoveConnectionAsync(Apperture.Core.Domain.Connector.ConnectionId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_preview);

        public ValueTask<ResourceMovePreview> MoveScheduleAsync(Apperture.Core.Domain.Scheduling.ScheduleId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_preview);
    }

    private sealed class ThrowingMoveService : IResourceOwnershipMoveService
    {
        private readonly Exception _exception;

        public ThrowingMoveService(Exception exception) => _exception = exception;

        public ValueTask<ResourceMovePreview> PreviewIntegrationMoveAsync(IntegrationId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default) =>
            throw _exception;

        public ValueTask<ResourceMovePreview> PreviewConnectionMoveAsync(Apperture.Core.Domain.Connector.ConnectionId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default) =>
            throw _exception;

        public ValueTask<ResourceMovePreview> PreviewScheduleMoveAsync(Apperture.Core.Domain.Scheduling.ScheduleId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default) =>
            throw _exception;

        public ValueTask<ResourceMovePreview> MoveIntegrationAsync(IntegrationId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default) =>
            throw _exception;

        public ValueTask<ResourceMovePreview> MoveConnectionAsync(Apperture.Core.Domain.Connector.ConnectionId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default) =>
            throw _exception;

        public ValueTask<ResourceMovePreview> MoveScheduleAsync(Apperture.Core.Domain.Scheduling.ScheduleId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default) =>
            throw _exception;
    }
}
