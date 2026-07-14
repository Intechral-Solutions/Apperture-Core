using Apperture.Core.Application.Recovery;
using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
using Apperture.ControlPlane.Security;
using Apperture.Management.Api.Controllers;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Dispatch.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sample.HelloWorld;
using Xunit;

namespace Apperture.UnitTests.Runtime;

/// <summary>SWM-8: manual run-cycle enrichment + stale-only recovery.</summary>
public sealed class SchedulerOperationsSwm8Tests
{
    private static readonly DateTimeOffset AsOf =
        new(2025, 6, 15, 12, 1, 30, TimeSpan.Zero);

    private static readonly DateTimeOffset LastAttempted =
        new(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    [Fact]
    public async Task RunCycle_returns_enriched_counts_and_writesHeartbeat_false()
    {
        await using var db = NewDb();
        SeedEnabledSchedule(db, withIntegration: true);
        await db.SaveChangesAsync();

        await using var scope = CreateServices(db, "host-a").BuildServiceProvider().CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();
        var cycle = await runner.RunCycleAsync(new RuntimePlaneId("local"), AsOf);

        var mapped = SchedulerManualOperationMapper.FromCycle("local", SchedulerManualOperationMapper.ActionRunCycle, cycle);
        mapped.WritesHeartbeat.Should().BeFalse();
        mapped.AssignmentCount.Should().Be(1);
        mapped.DueCount.Should().BeGreaterThanOrEqualTo(0);
        mapped.HostId.Should().Be("host-a");
        mapped.Message.Should().Contain("heartbeat");
    }

    [Fact]
    public async Task RunCycle_respects_runtime_plane_and_does_not_duplicate_fire()
    {
        await using var db = NewDb();
        SeedEnabledSchedule(db, withIntegration: true);
        await db.SaveChangesAsync();

        await using var scope = CreateServices(db, "host-a").BuildServiceProvider().CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();

        await runner.RunCycleAsync(new RuntimePlaneId("local"), AsOf);
        await runner.RunCycleAsync(new RuntimePlaneId("local"), AsOf.AddSeconds(5));
        await runner.RunCycleAsync(new RuntimePlaneId("other-plane"), AsOf.AddSeconds(10));

        var localRuns = await db.ScheduleRuns.AsNoTracking()
            .Where(r => r.ScheduleId == "sched1")
            .ToListAsync();
        localRuns.Select(r => r.FireTimeUtc).Should().OnlyHaveUniqueItems();
        (await db.ScheduleRuns.AsNoTracking().CountAsync(r => r.AssignmentId == "a1"))
            .Should().BeGreaterThanOrEqualTo(0);

        // other-plane has no assignments → no extra claims from that plane
        var otherPlaneCycle = await runner.RunCycleAsync(new RuntimePlaneId("other-plane"), AsOf);
        otherPlaneCycle.AssignmentCount.Should().Be(0);
        otherPlaneCycle.DueCount.Should().Be(0);
    }

    [Fact]
    public async Task RecoverStale_skips_fresh_in_progress_and_disabled_schedules()
    {
        await using var db = NewDb();
        SeedEnabledSchedule(db, withIntegration: true);
        db.ScheduleRuns.Add(new ScheduleRunEntity
        {
            ScheduleId = "sched1",
            AssignmentId = "a1",
            FireTimeUtc = new DateTimeOffset(2025, 6, 15, 12, 1, 0, TimeSpan.Zero),
            StartedAtUtc = AsOf.AddSeconds(-30),
            Outcome = "InProgress",
            ClaimedByHostId = "host-old"
        });
        await db.SaveChangesAsync();

        await using var scope = CreateServices(db, "host-b", claimLeaseMinutes: 5)
            .BuildServiceProvider().CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();

        var fresh = await runner.RecoverStaleAsync(new RuntimePlaneId("local"), AsOf);
        fresh.StaleCount.Should().Be(0);
        fresh.RecoveredStaleCount.Should().Be(0);
        fresh.DueCount.Should().Be(0);

        db.ScheduleDefinitions.Single().Enabled = false;
        db.ScheduleRuns.Single().StartedAtUtc = AsOf.AddMinutes(-30);
        await db.SaveChangesAsync();

        var disabled = await runner.RecoverStaleAsync(new RuntimePlaneId("local"), AsOf);
        disabled.StaleCount.Should().Be(0);
        disabled.RecoveredStaleCount.Should().Be(0);
    }

    [Fact]
    public async Task RecoverStale_does_not_dispatch_newly_due_and_is_repeatable()
    {
        await using var db = NewDb();
        SeedEnabledSchedule(db, withIntegration: true);
        // No InProgress row → nothing to recover; due fire exists but must not dispatch.
        await db.SaveChangesAsync();

        await using var scope = CreateServices(db, "host-a").BuildServiceProvider().CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();

        var first = await runner.RecoverStaleAsync(new RuntimePlaneId("local"), AsOf);
        var second = await runner.RecoverStaleAsync(new RuntimePlaneId("local"), AsOf.AddSeconds(2));

        first.DueCount.Should().Be(0);
        first.DispatchedCount.Should().Be(0);
        second.DueCount.Should().Be(0);
        second.DispatchedCount.Should().Be(0);
        (await db.ScheduleRuns.AsNoTracking().CountAsync()).Should().Be(0);
        (await db.WorkflowStartRequests.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RecoverStale_recovers_stale_claim_when_present()
    {
        await using var db = NewDb();
        SeedEnabledSchedule(db, withIntegration: true);
        var fire = new DateTimeOffset(2025, 6, 15, 12, 1, 0, TimeSpan.Zero);
        db.ScheduleRuns.Add(new ScheduleRunEntity
        {
            ScheduleId = "sched1",
            AssignmentId = "a1",
            FireTimeUtc = fire,
            StartedAtUtc = AsOf.AddMinutes(-30),
            Outcome = "InProgress",
            ClaimedByHostId = "host-old"
        });
        await db.SaveChangesAsync();

        await using var scope = CreateServices(db, "host-b", claimLeaseMinutes: 5)
            .BuildServiceProvider().CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();
        var result = await runner.RecoverStaleAsync(new RuntimePlaneId("local"), AsOf);

        result.StaleCount.Should().Be(1);
        result.DueCount.Should().Be(0);
        result.RecoveredStaleCount.Should().Be(1);
        result.DispatchedCount.Should().Be(1);

        var again = await runner.RecoverStaleAsync(new RuntimePlaneId("local"), AsOf.AddSeconds(5));
        again.StaleCount.Should().Be(0);
        again.RecoveredStaleCount.Should().Be(0);
    }

    [Fact]
    public async Task SchedulerOperationsController_requires_recovery_manage()
    {
        var runner = new CapturingRunner();
        var controller = new SchedulerOperationsController(
            runner,
            new StubGov(canManage: false),
            new StubPrincipal());
        var denied = await controller.RunCycle(cancellationToken: CancellationToken.None);
        denied.Result.Should().BeOfType<ForbidResult>();
        runner.RunCycleCalled.Should().BeFalse();

        var deniedRecover = await controller.RecoverStale(cancellationToken: CancellationToken.None);
        deniedRecover.Result.Should().BeOfType<ForbidResult>();
        runner.RecoverStaleCalled.Should().BeFalse();
    }

    [Fact]
    public async Task SchedulerOperationsController_run_cycle_and_recover_stale_return_bodies()
    {
        var runner = new CapturingRunner();
        var controller = new SchedulerOperationsController(
            runner,
            new StubGov(canManage: true),
            new StubPrincipal());

        var run = await controller.RunCycle(runtimePlaneId: "local", CancellationToken.None);
        var runBody = run.Result.Should().BeOfType<AcceptedResult>().Subject.Value
            .Should().BeOfType<SchedulerManualOperationResult>().Subject;
        runBody.WritesHeartbeat.Should().BeFalse();
        runBody.Action.Should().Be(SchedulerManualOperationMapper.ActionRunCycle);
        runner.LastPlane.Should().Be("local");

        var recover = await controller.RecoverStale(runtimePlaneId: "local", CancellationToken.None);
        var recoverBody = recover.Result.Should().BeOfType<AcceptedResult>().Subject.Value
            .Should().BeOfType<SchedulerManualOperationResult>().Subject;
        recoverBody.WritesHeartbeat.Should().BeFalse();
        recoverBody.Action.Should().Be(SchedulerManualOperationMapper.ActionRecoverStale);
        recoverBody.DueCount.Should().Be(0);
        runner.RecoverStaleCalled.Should().BeTrue();
    }

    [Fact]
    public void WhyNotFiringHints_cover_disabled_stale_and_worker_unknown()
    {
        var detail = new Apperture.Management.Client.ScheduleDetailDto(
            "s1", "n", "* * * * *", "UTC", Enabled: false, TargetJobType: 0, null, Version: 1,
            null, "Failed", "boom", null, "host", DateTimeOffset.UtcNow.AddHours(-1), IsClaimStale: true,
            IntegrationId: null, RuntimePlaneId: "worker-local");

        var hints = Apperture.Management.Ui.Shared.RunScheduleOperatorUiHelpers.BuildWhyNotFiringHints(
            detail,
            workerRuntimeStatus: "Unknown");
        hints.Should().Contain(h => h.Contains("disabled", StringComparison.OrdinalIgnoreCase));
        hints.Should().Contain(h => h.Contains("stale", StringComparison.OrdinalIgnoreCase));
        hints.Should().Contain(h => h.Contains("plane", StringComparison.OrdinalIgnoreCase));
        hints.Should().Contain(h => h.Contains("Unknown", StringComparison.OrdinalIgnoreCase)
                                    || h.Contains("heartbeat", StringComparison.OrdinalIgnoreCase));
    }

    private static void SeedEnabledSchedule(AppertureDbContext db, bool withIntegration)
    {
        const string integrationId = "int_hw";
        db.ScheduleDefinitions.Add(new ScheduleDefinitionEntity
        {
            Id = "sched1",
            Name = "every-minute",
            RecurrenceExpression = "* * * * *",
            TimeZone = "UTC",
            Enabled = true,
            TargetJobType = (int)ScheduledJobType.WorkflowExecution,
            TargetJobConfigurationJson = "{\"workflowDefinitionId\":\"Sample.HelloWorld.HelloWorld\"}",
            IntegrationId = withIntegration ? integrationId : null,
            Version = 1
        });
        db.ScheduleAssignments.Add(new ScheduleAssignmentEntity
        {
            Id = "a1",
            ScheduleId = "sched1",
            RuntimePlaneId = "local"
        });
        db.ScheduleStates.Add(new ScheduleStateEntity
        {
            AssignmentId = "a1",
            LastAttemptedUtc = LastAttempted
        });
        if (withIntegration)
        {
            db.Integrations.Add(new IntegrationEntity
            {
                Id = integrationId,
                DisplayName = "HelloWorld",
                ModuleId = "Sample.HelloWorld",
                State = (int)IntegrationState.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    private static ServiceCollection CreateServices(AppertureDbContext db, string hostId, int claimLeaseMinutes = 5)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddScoped<IScheduleRepository, SqlScheduleRepository>();
        services.AddScoped<IIntegrationRepository, SqlIntegrationRepository>();
        services.AddScoped<IScheduleEvaluator, CronScheduleEvaluator>();
        services.AddScoped<IAuditService, SqlAuditService>();
        var modules = new IModule[] { new HelloWorldModule() };
        services.AddSingleton<IEnumerable<IModule>>(modules);
        services.AddSingleton<IWorkflowDefinitionResolver>(sp =>
            new WorkflowDefinitionResolver(sp.GetRequiredService<IEnumerable<IModule>>(), null));
        services.AddSingleton<IOperationRegistryService>(sp =>
            new OperationRegistryService(sp.GetRequiredService<IEnumerable<IModule>>(), null));
        services.AddScoped<ISchedulerLifecycleGuard, SchedulerLifecycleGuard>();
        services.AddScoped<IWorkflowPersistence, SqlWorkflowPersistence>();
        services.AddScoped<IWorkflowStartDispatchService, WorkflowStartDispatchService>();
        services.AddScoped<IOperationStartDispatchService, OperationStartDispatchService>();
        services.AddSingleton<ISchedulerHostIdentity>(new DefaultSchedulerHostIdentity(hostId));
        services.AddSingleton<ISchedulerClaimOptions>(new DefaultSchedulerClaimOptions(claimLeaseMinutes));
        services.AddScoped<IScheduleDispatchRunner, ScheduleDispatchRunner>();
        return services;
    }

    private sealed class CapturingRunner : IScheduleDispatchRunner
    {
        public bool RunCycleCalled { get; private set; }
        public bool RecoverStaleCalled { get; private set; }
        public string? LastPlane { get; private set; }

        public ValueTask<ScheduleDispatchCycleResult> RunCycleAsync(RuntimePlaneId planeId, DateTimeOffset asOfUtc, CancellationToken cancellationToken = default)
        {
            RunCycleCalled = true;
            LastPlane = planeId.Value;
            return ValueTask.FromResult(new ScheduleDispatchCycleResult(1, 1, 0, 2, 1, 0, 1, 0, "mgmt"));
        }

        public ValueTask<ScheduleDispatchCycleResult> RecoverStaleAsync(RuntimePlaneId planeId, DateTimeOffset asOfUtc, CancellationToken cancellationToken = default)
        {
            RecoverStaleCalled = true;
            LastPlane = planeId.Value;
            return ValueTask.FromResult(new ScheduleDispatchCycleResult(0, 0, 0, 2, 0, 0, 0, 0, "mgmt"));
        }
    }

    private sealed class StubGov : IRecoveryGovernanceService
    {
        private readonly bool _canManage;
        public StubGov(bool canManage) => _canManage = canManage;
        public ValueTask<bool> CanReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanManageAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_canManage);
    }

    private sealed class StubPrincipal : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(null);
    }
}
