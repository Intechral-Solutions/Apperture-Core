using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.Models;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;
using Apperture.Management.Api.Controllers;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Epic 34: Authorization hardening tests for Recovery, Audit, RuntimeHealth, and Capabilities.
/// </summary>
public sealed class Epic34AuthorizationTests
{
    [Fact]
    public async Task RuntimeHealthController_Get_WithoutRuntimeHealthRead_Returns403()
    {
        var principal = new StubPrincipalContext();
        var auth = new StubAuthorizationEvaluationService(allowed: false);
        var health = new StubRuntimeHealthQueryService();
        var scheduler = new StubSchedulerStatusQueryService();
        var controller = new RuntimeHealthController(health, scheduler, principal, auth);
        var result = await controller.GetRuntimeHealth(cancellationToken: CancellationToken.None);
        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task RuntimeHealthController_Get_WithRuntimeHealthRead_Returns200()
    {
        var principal = new StubPrincipalContext();
        var auth = new StubAuthorizationEvaluationService(allowed: true);
        var health = new StubRuntimeHealthQueryService();
        var scheduler = new StubSchedulerStatusQueryService();
        var controller = new RuntimeHealthController(health, scheduler, principal, auth);
        var result = await controller.GetRuntimeHealth(cancellationToken: CancellationToken.None);
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task RuntimeHealthController_GetSchedulerStatus_WithoutRuntimeHealthRead_Returns403()
    {
        var principal = new StubPrincipalContext();
        var auth = new StubAuthorizationEvaluationService(allowed: false);
        var health = new StubRuntimeHealthQueryService();
        var scheduler = new StubSchedulerStatusQueryService();
        var controller = new RuntimeHealthController(health, scheduler, principal, auth);
        var result = await controller.GetSchedulerStatus(cancellationToken: CancellationToken.None);
        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task RuntimeHealthController_GetSchedulerStatus_WithRuntimeHealthRead_Returns200()
    {
        var principal = new StubPrincipalContext();
        var auth = new StubAuthorizationEvaluationService(allowed: true);
        var health = new StubRuntimeHealthQueryService();
        var scheduler = new StubSchedulerStatusQueryService();
        var controller = new RuntimeHealthController(health, scheduler, principal, auth);
        var result = await controller.GetSchedulerStatus(cancellationToken: CancellationToken.None);
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ControlCapabilitiesController_ReturnsCorrectCapabilities_BasedOnPermissions()
    {
        var capabilities = new StubOperatorCapabilitiesQueryService(
            new OperatorCapabilitiesDto(true, false, true, false, true));
        var controller = new ControlCapabilitiesController(capabilities);
        var result = await controller.GetCapabilities(cancellationToken: CancellationToken.None);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<OperatorCapabilitiesDto>().Subject;
        dto.CanManageSchedules.Should().BeTrue();
        dto.CanManageRecovery.Should().BeFalse();
        dto.CanViewAudit.Should().BeTrue();
        dto.CanViewRuntimeHealth.Should().BeFalse();
        dto.CanManageModules.Should().BeTrue();
    }

    private sealed class StubPrincipalContext : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new StubActor());
    }

    private sealed class StubActor : IActor
    {
        public ActorId Id => new("user:test");
        public ActorKind Kind => ActorKind.Human;
        public string? DisplayName => "Test User";
    }

    private sealed class StubAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        private readonly bool _allowed;

        public StubAuthorizationEvaluationService(bool allowed = true) => _allowed = allowed;

        public ValueTask<bool> EvaluateAsync(
            Apperture.Core.Domain.Governance.AuthorizationEvaluationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_allowed);
    }

    private sealed class StubRuntimeHealthQueryService : IRuntimeHealthQueryService
    {
        public ValueTask<RuntimeHealthSnapshot> GetSnapshotAsync(
            string? runtimePlaneId,
            int blockedDeferredWindowHours,
            int scheduleFailureWindowHours,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeHealthSnapshot(
                DateTimeOffset.UtcNow,
                "Healthy",
                [],
                [],
                [],
                [],
                [],
                [],
                []));
    }

    private sealed class StubSchedulerStatusQueryService : ISchedulerStatusQueryService
    {
        public ValueTask<SchedulerStatusSnapshot> GetStatusAsync(
            string? runtimePlaneId = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SchedulerStatusSnapshot(
                DateTimeOffset.UtcNow,
                SchedulerLoopStatus.Unknown,
                ScheduleRuntimePlaneDefaults.ExpectedManagementPlaneId,
                null,
                null,
                SchedulerPlaneAlignment.Unknown,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "No scheduler heartbeat yet."));
    }

    private sealed class StubOperatorCapabilitiesQueryService : IOperatorCapabilitiesQueryService
    {
        private readonly OperatorCapabilitiesDto _dto;

        public StubOperatorCapabilitiesQueryService(OperatorCapabilitiesDto dto) => _dto = dto;

        public ValueTask<OperatorCapabilitiesDto> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_dto);
    }
}
