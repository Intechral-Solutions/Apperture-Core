using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Scheduling;
using Apperture.ControlPlane.Security;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sample.HelloWorld;
using Xunit;

namespace Apperture.UnitTests.Runtime;

/// <summary>
/// Epic 36: Multi-host scheduler coordination and claiming.
/// </summary>
public sealed class Epic36MultiHostSchedulerTests
{
    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    /// <summary>
    /// Two hosts, one due schedule: only one claim succeeds, only one WorkflowStartRequest created.
    /// </summary>
    [Fact]
    public async Task TwoHosts_OneDueSchedule_OnlyOneDispatch()
    {
        var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedScheduleWithWorkflowTarget(db, "s1", "a1", "Sample.HelloWorld.HelloWorld", "* * * * *", "UTC", now.AddSeconds(-35));
        await db.SaveChangesAsync();

        var services = CreateServices(db, "hostA");
        await using (var scopeA = services.BuildServiceProvider().CreateAsyncScope())
        {
            var runnerA = scopeA.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();
            await runnerA.RunCycleAsync(new RuntimePlaneId("local"), now);
        }

        var startCountAfterA = await db.WorkflowStartRequests.AsNoTracking().CountAsync();
        startCountAfterA.Should().Be(1, "hostA should have claimed and dispatched");

        // Host B runs; the fire is already claimed/completed, so hostB gets nothing
        var servicesB = CreateServices(db, "hostB");
        await using (var scopeB = servicesB.BuildServiceProvider().CreateAsyncScope())
        {
            var runnerB = scopeB.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();
            await runnerB.RunCycleAsync(new RuntimePlaneId("local"), now.AddSeconds(1));
        }

        var startCountAfterB = await db.WorkflowStartRequests.AsNoTracking().CountAsync();
        startCountAfterB.Should().Be(1, "hostB should not create a second dispatch");
    }

    /// <summary>
    /// Stale claim recovery: InProgress row with old StartedAtUtc can be recovered; dispatch proceeds.
    /// Uses cron that yields one fire so only the stale candidate is processed.
    /// </summary>
    [Fact]
    public async Task StaleClaimRecovery_RecoversAndDispatches()
    {
        var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        // Use "59 * * * *" so one fire per hour; set LastAttemptedUtc so evaluator window has one fire
        var fireUtc = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 59, 0, TimeSpan.Zero);
        if (fireUtc >= now)
            fireUtc = fireUtc.AddHours(-1);
        SeedScheduleWithWorkflowTarget(db, "s1", "a1", "Sample.HelloWorld.HelloWorld", "59 * * * *", "UTC", fireUtc.AddMinutes(-2));
        await db.SaveChangesAsync();

        // Seed an InProgress run with very old StartedAtUtc (simulate host crash)
        db.ScheduleRuns.Add(new ScheduleRunEntity
        {
            ScheduleId = "s1",
            AssignmentId = "a1",
            FireTimeUtc = fireUtc,
            StartedAtUtc = now.AddMinutes(-10), // > 5 min ago => stale
            Outcome = "InProgress",
            ClaimedByHostId = "crashed-host"
        });
        await db.SaveChangesAsync();

        var services = CreateServices(db, "recovery-host", claimLeaseMinutes: 5);
        await using (var scope = services.BuildServiceProvider().CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();
            await runner.RunCycleAsync(new RuntimePlaneId("local"), now);
        }

        var run = await db.ScheduleRuns.AsNoTracking().FirstAsync(r => r.ScheduleId == "s1" && r.FireTimeUtc == fireUtc);
        run.ClaimedByHostId.Should().Be("recovery-host");
        run.Outcome.Should().Be("Dispatched");
        var startCount = await db.WorkflowStartRequests.AsNoTracking().CountAsync();
        startCount.Should().Be(1);
    }

    /// <summary>
    /// Loser does not dispatch: Host B tries after Host A claimed; claim fails (not stale); no dispatch.
    /// </summary>
    [Fact]
    public async Task LoserHost_DoesNotDispatch()
    {
        var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedScheduleWithWorkflowTarget(db, "s1", "a1", "Sample.HelloWorld.HelloWorld", "* * * * *", "UTC", now.AddSeconds(-35));
        await db.SaveChangesAsync();

        // Host A claims and completes
        var servicesA = CreateServices(db, "hostA");
        await using (var scopeA = servicesA.BuildServiceProvider().CreateAsyncScope())
        {
            var runnerA = scopeA.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();
            await runnerA.RunCycleAsync(new RuntimePlaneId("local"), now);
        }

        // Host B runs immediately after (claim not stale) - should find nothing to do
        var servicesB = CreateServices(db, "hostB", claimLeaseMinutes: 5);
        await using (var scopeB = servicesB.BuildServiceProvider().CreateAsyncScope())
        {
            var runnerB = scopeB.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();
            await runnerB.RunCycleAsync(new RuntimePlaneId("local"), now.AddSeconds(1));
        }

        var startCount = await db.WorkflowStartRequests.AsNoTracking().CountAsync();
        startCount.Should().Be(1);
    }

    /// <summary>
    /// When InProgress run exists, schedule detail returns ClaimedByHostId.
    /// </summary>
    [Fact]
    public async Task ScheduleDetail_WhenInProgressRun_ReturnsClaimedByHostId()
    {
        var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedScheduleWithWorkflowTarget(db, "s1", "a1", "Sample.HelloWorld.HelloWorld", "* * * * *", "UTC", now.AddSeconds(-35));
        db.ScheduleRuns.Add(new ScheduleRunEntity
        {
            ScheduleId = "s1",
            AssignmentId = "a1",
            FireTimeUtc = now,
            StartedAtUtc = now.AddMinutes(-1),
            Outcome = "InProgress",
            ClaimedByHostId = "my-host"
        });
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddScoped<AppertureDbContext>(_ => db);
        services.AddScoped<IScheduleRepository, SqlScheduleRepository>();
        services.AddSingleton<ISchedulerClaimOptions>(new DefaultSchedulerClaimOptions(5));
        services.AddScoped<IScheduleManagementService, SqlScheduleManagementService>();

        await using var scope = services.BuildServiceProvider().CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IScheduleManagementService>();
        var detail = await mgmt.GetScheduleWithExecutionStateAsync(new ScheduleId("s1"), new RuntimePlaneId("local"));

        detail.Should().NotBeNull();
        detail!.InProgressClaim.Should().NotBeNull();
        detail.InProgressClaim!.ClaimedByHostId.Should().Be("my-host");
        detail.InProgressClaim.ClaimedAtUtc.Should().BeCloseTo(now.AddMinutes(-1), TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Completed runs do not surface as an active claim (operator-safe: no confusion with historical owner).
    /// </summary>
    [Fact]
    public async Task GetInProgressRunClaimAsync_ReturnsNull_WhenOnlyCompletedRun()
    {
        var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedScheduleWithWorkflowTarget(db, "s1", "a1", "Sample.HelloWorld.HelloWorld", "* * * * *", "UTC", now.AddSeconds(-35));
        db.ScheduleRuns.Add(new ScheduleRunEntity
        {
            ScheduleId = "s1",
            AssignmentId = "a1",
            FireTimeUtc = now,
            StartedAtUtc = now.AddMinutes(-2),
            CompletedAtUtc = now.AddMinutes(-1),
            Outcome = "Dispatched",
            ClaimedByHostId = "old-host"
        });
        await db.SaveChangesAsync();

        var repo = new SqlScheduleRepository(db);
        var claim = await repo.GetInProgressRunClaimAsync("a1");
        claim.Should().BeNull();

        var services = new ServiceCollection();
        services.AddScoped<AppertureDbContext>(_ => db);
        services.AddScoped<IScheduleRepository, SqlScheduleRepository>();
        services.AddSingleton<ISchedulerClaimOptions>(new DefaultSchedulerClaimOptions(5));
        services.AddScoped<IScheduleManagementService, SqlScheduleManagementService>();
        await using var scope = services.BuildServiceProvider().CreateAsyncScope();
        var detail = await scope.ServiceProvider.GetRequiredService<IScheduleManagementService>()
            .GetScheduleWithExecutionStateAsync(new ScheduleId("s1"), new RuntimePlaneId("local"));
        detail!.InProgressClaim.Should().BeNull("API must not surface historical claim as active");
    }

    /// <summary>
    /// IsStale is true only when StartedAtUtc is before the lease cutoff (server-side lease minutes).
    /// </summary>
    [Fact]
    public async Task InProgressClaim_IsStale_WhenStartedBeforeLeaseWindow()
    {
        var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedScheduleWithWorkflowTarget(db, "s1", "a1", "Sample.HelloWorld.HelloWorld", "* * * * *", "UTC", now.AddSeconds(-35));
        db.ScheduleRuns.Add(new ScheduleRunEntity
        {
            ScheduleId = "s1",
            AssignmentId = "a1",
            FireTimeUtc = now,
            StartedAtUtc = now.AddMinutes(-10),
            Outcome = "InProgress",
            ClaimedByHostId = "slow-host"
        });
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddScoped<AppertureDbContext>(_ => db);
        services.AddScoped<IScheduleRepository, SqlScheduleRepository>();
        services.AddSingleton<ISchedulerClaimOptions>(new DefaultSchedulerClaimOptions(5));
        services.AddScoped<IScheduleManagementService, SqlScheduleManagementService>();

        await using var scope = services.BuildServiceProvider().CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IScheduleManagementService>();
        var detail = await mgmt.GetScheduleWithExecutionStateAsync(new ScheduleId("s1"), new RuntimePlaneId("local"));

        detail!.InProgressClaim.Should().NotBeNull();
        detail.InProgressClaim!.IsStale.Should().BeTrue("StartedAtUtc is 10 minutes ago with 5-minute lease");
    }

    [Fact]
    public async Task InProgressClaim_IsNotStale_WhenStartedInsideLeaseWindow()
    {
        var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedScheduleWithWorkflowTarget(db, "s1", "a1", "Sample.HelloWorld.HelloWorld", "* * * * *", "UTC", now.AddSeconds(-35));
        db.ScheduleRuns.Add(new ScheduleRunEntity
        {
            ScheduleId = "s1",
            AssignmentId = "a1",
            FireTimeUtc = now,
            StartedAtUtc = now.AddMinutes(-1),
            Outcome = "InProgress",
            ClaimedByHostId = "fresh-host"
        });
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddScoped<AppertureDbContext>(_ => db);
        services.AddScoped<IScheduleRepository, SqlScheduleRepository>();
        services.AddSingleton<ISchedulerClaimOptions>(new DefaultSchedulerClaimOptions(5));
        services.AddScoped<IScheduleManagementService, SqlScheduleManagementService>();

        await using var scope = services.BuildServiceProvider().CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IScheduleManagementService>();
        var detail = await mgmt.GetScheduleWithExecutionStateAsync(new ScheduleId("s1"), new RuntimePlaneId("local"));

        detail!.InProgressClaim.Should().NotBeNull();
        detail.InProgressClaim!.IsStale.Should().BeFalse();
    }

    private static ServiceCollection CreateServices(AppertureDbContext db, string hostId, int claimLeaseMinutes = 5)
    {
        var services = new ServiceCollection();
        services.AddScoped<AppertureDbContext>(_ => db);
        services.AddScoped<IScheduleRepository, SqlScheduleRepository>();
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

    private static void SeedScheduleWithWorkflowTarget(
        AppertureDbContext db,
        string scheduleId,
        string assignmentId,
        string workflowDefinitionId,
        string cron,
        string timeZone,
        DateTimeOffset? lastAttemptedUtc)
    {
        var configJson = $"{{\"workflowDefinitionId\":\"{workflowDefinitionId}\"}}";
        db.ScheduleDefinitions.Add(new ScheduleDefinitionEntity
        {
            Id = scheduleId,
            Name = "Test",
            RecurrenceExpression = cron,
            TimeZone = timeZone,
            Enabled = true,
            TargetJobType = (int)ScheduledJobType.WorkflowExecution,
            TargetJobConfigurationJson = configJson,
            Version = 1
        });
        db.ScheduleAssignments.Add(new ScheduleAssignmentEntity
        {
            Id = assignmentId,
            ScheduleId = scheduleId,
            RuntimePlaneId = "local",
            DesiredVersion = 1,
            AppliedVersion = 1
        });
        if (lastAttemptedUtc is not null)
        {
            db.ScheduleStates.Add(new ScheduleStateEntity
            {
                AssignmentId = assignmentId,
                LastAttemptedUtc = lastAttemptedUtc.Value
            });
        }
    }
}
