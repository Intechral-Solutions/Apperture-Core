using Apperture.Core.Application.Recovery;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.ControlPlane.Security;
using Apperture.Management.Api.Controllers;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>SWM-9a: Epic72B runtime-worker lifecycle is quarantined.</summary>
public sealed class WorkerLifecycleQuarantineSwm9aTests
{
    [Fact]
    public async Task DevWorkerRecovery_StartAsync_always_fails_with_quarantine_message()
    {
        var svc = new DevWorkerRuntimeRecoveryService(new StubHostEnvironment(isDevelopment: true));
        var result = await svc.StartAsync(restart: false);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be(WorkerLifecycleQuarantine.ErrorCode);
        result.Message.Should().Contain("quarantined");
        result.Message.Should().Contain("Aspire");
    }

    [Fact]
    public async Task DevWorkerRecovery_GetStatusAsync_returns_quarantined()
    {
        var svc = new DevWorkerRuntimeRecoveryService(new StubHostEnvironment(isDevelopment: false));
        var status = await svc.GetStatusAsync();
        status.Status.Should().Be(WorkerLifecycleQuarantine.Status);
        status.Pid.Should().BeNull();
        status.Message.Should().Contain("heartbeat");
    }

    [Fact]
    public async Task StartRuntimeWorker_returns_410_with_error_code()
    {
        var controller = NewRecoveryController(canManage: true);
        var result = await controller.StartRuntimeWorker(restart: false, CancellationToken.None);
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status410Gone);
        var error = objectResult.Value!.GetType().GetProperty("error")?.GetValue(objectResult.Value) as string;
        error.Should().Be(WorkerLifecycleQuarantine.ErrorCode);
        var quarantined = objectResult.Value.GetType().GetProperty("quarantined")?.GetValue(objectResult.Value);
        quarantined.Should().Be(true);
    }

    [Fact]
    public async Task StartRuntimeWorker_without_manage_returns_403()
    {
        var controller = NewRecoveryController(canManage: false);
        var result = await controller.StartRuntimeWorker(restart: true, CancellationToken.None);
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetRuntimeWorkerStatus_returns_quarantined_body()
    {
        var controller = NewRecoveryController(canManage: true, canRead: true);
        var result = await controller.GetRuntimeWorkerStatus(CancellationToken.None);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<DevWorkerRuntimeStatus>().Subject;
        body.Status.Should().Be(WorkerLifecycleQuarantine.Status);
    }

    private static RecoveryController NewRecoveryController(bool canManage, bool canRead = true) =>
        new(
            new EmptyRecoveryQuery(),
            new NoRetry(),
            new NoRunner(),
            new DevWorkerRuntimeRecoveryService(new StubHostEnvironment(true)),
            new StubGov(canRead, canManage),
            new StubPrincipal());

    private sealed class StubHostEnvironment(bool isDevelopment) : IHostEnvironment
    {
        public string EnvironmentName
        {
            get => isDevelopment ? Environments.Development : Environments.Production;
            set { }
        }

        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class StubGov(bool read, bool manage) : IRecoveryGovernanceService
    {
        public ValueTask<bool> CanReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(read);
        public ValueTask<bool> CanManageAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(manage);
    }

    private sealed class StubPrincipal : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(null);
    }

    private sealed class EmptyRecoveryQuery : IRecoveryQueryService
    {
        public ValueTask<IReadOnlyList<BlockedWorkRemediationEntry>> GetBlockedWorkRemediationAsync(
            string? runtimePlaneId = "local",
            int windowHours = 24,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<BlockedWorkRemediationEntry>>([]);

        public ValueTask<BlockedWorkRemediationEntry?> GetBlockedWorkRemediationForRunAsync(
            string runId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<BlockedWorkRemediationEntry?>(null);

        public ValueTask<ScheduleRemediationEntry?> GetScheduleRemediationAsync(
            string scheduleId,
            string? runtimePlaneId = "local",
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ScheduleRemediationEntry?>(null);

        public ValueTask<ModuleRemediationEntry?> GetModuleRemediationAsync(
            string moduleId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ModuleRemediationEntry?>(null);
    }

    private sealed class NoRetry : IWorkflowRetryService
    {
        public ValueTask<WorkflowRetryResult?> RequestRetryAsync(OperationRunId runId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowRetryResult?>(null);
    }

    private sealed class NoRunner : IScheduleDispatchRunner
    {
        public ValueTask<ScheduleDispatchCycleResult> RunCycleAsync(
            RuntimePlaneId planeId, DateTimeOffset asOfUtc, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ScheduleDispatchCycleResult(0, 0, 0));

        public ValueTask<ScheduleDispatchCycleResult> RecoverStaleAsync(
            RuntimePlaneId planeId, DateTimeOffset asOfUtc, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ScheduleDispatchCycleResult(0, 0, 0));
    }
}
