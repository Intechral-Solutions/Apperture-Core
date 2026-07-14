using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Dispatch.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sample.HelloWorld;
using Xunit;

namespace Apperture.UnitTests.Runtime;

/// <summary>
/// Scheduling correctness: every-minute cron must not burst-dispatch multiple fires per cycle,
/// and repeated polls for the same UTC fire instant remain idempotent.
/// </summary>
public sealed class ScheduleMinuteFireIdempotencyTests
{
    private static readonly DateTimeOffset AsOfWithinMinute =
        new(2025, 6, 15, 12, 1, 45, TimeSpan.Zero);

    private static readonly DateTimeOffset LastAttemptedPreviousMinute =
        new(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ExpectedFire =
        new(2025, 6, 15, 12, 1, 0, TimeSpan.Zero);

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    [Fact]
    public async Task GetDueAsync_every_minute_behind_returns_earliest_unclaimed_only()
    {
        await using var db = NewDb();
        // lastAttempted at 11:57:00 → window includes 11:58, 11:59, 12:00, 12:01
        var lastAttempted = new DateTimeOffset(2025, 6, 15, 11, 57, 0, TimeSpan.Zero);
        SeedEveryMinute(db, lastAttempted);
        await db.SaveChangesAsync();

        var evaluator = new CronScheduleEvaluator(new SqlScheduleRepository(db));
        var due = await evaluator.GetDueAsync(new RuntimePlaneId("local"), AsOfWithinMinute);

        due.Should().ContainSingle("catch-up must drain one fire per evaluation, not every missed minute");
        due[0].FireTimeUtc.Should().Be(new DateTimeOffset(2025, 6, 15, 11, 58, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetDueAsync_does_not_return_duplicate_fire_instants()
    {
        await using var db = NewDb();
        SeedEveryMinute(db, LastAttemptedPreviousMinute);
        await db.SaveChangesAsync();

        var evaluator = new CronScheduleEvaluator(new SqlScheduleRepository(db));
        var due = await evaluator.GetDueAsync(new RuntimePlaneId("local"), AsOfWithinMinute);
        due.Select(d => d.FireTimeUtc).Should().OnlyHaveUniqueItems();
        due.Should().ContainSingle();
        due[0].FireTimeUtc.Should().Be(ExpectedFire);
    }

    [Fact]
    public async Task Multiple_dispatch_cycles_same_minute_create_only_one_schedule_run()
    {
        await using var db = NewDb();
        SeedEveryMinute(db, LastAttemptedPreviousMinute, withIntegration: true);
        await db.SaveChangesAsync();

        var services = CreateServices(db, "host-a");
        await using var scope = services.BuildServiceProvider().CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();

        await runner.RunCycleAsync(new RuntimePlaneId("local"), AsOfWithinMinute);
        await runner.RunCycleAsync(new RuntimePlaneId("local"), AsOfWithinMinute.AddSeconds(5));
        await runner.RunCycleAsync(new RuntimePlaneId("local"), AsOfWithinMinute.AddSeconds(10));

        var runs = await db.ScheduleRuns.AsNoTracking().ToListAsync();
        runs.Should().ContainSingle();
        runs[0].FireTimeUtc.Should().Be(ExpectedFire);
        (await db.WorkflowStartRequests.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TryClaimFireAsync_duplicate_same_instant_is_prevented()
    {
        await using var db = NewDb();
        var repo = new SqlScheduleRepository(db);
        (await repo.TryClaimFireAsync("sched1", ExpectedFire, "a1", "host-a")).Should().BeTrue();
        (await repo.TryClaimFireAsync("sched1", ExpectedFire, "a1", "host-b")).Should().BeFalse();
        db.ScheduleRuns.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetRecoverableStaleCandidates_skips_fresh_in_progress_and_disabled_schedules()
    {
        await using var db = NewDb();
        SeedEveryMinute(db, LastAttemptedPreviousMinute, enabled: true);
        await db.SaveChangesAsync();

        var now = AsOfWithinMinute;
        db.ScheduleRuns.Add(new ScheduleRunEntity
        {
            ScheduleId = "sched1",
            AssignmentId = "a1",
            FireTimeUtc = ExpectedFire,
            StartedAtUtc = now.AddSeconds(-30), // fresh — within lease
            Outcome = "InProgress",
            ClaimedByHostId = "host-a"
        });
        await db.SaveChangesAsync();

        var repo = new SqlScheduleRepository(db);
        var staleCutoff = now.AddMinutes(-5);
        var stale = await repo.GetRecoverableStaleCandidatesAsync(new RuntimePlaneId("local"), staleCutoff);
        stale.Should().BeEmpty("fresh InProgress must not be recoverable");

        db.ScheduleDefinitions.Single().Enabled = false;
        db.ScheduleRuns.Single().StartedAtUtc = now.AddMinutes(-10);
        await db.SaveChangesAsync();

        stale = await repo.GetRecoverableStaleCandidatesAsync(new RuntimePlaneId("local"), staleCutoff);
        stale.Should().BeEmpty("disabled schedules must not be stale-recovered");
    }

    private static void SeedEveryMinute(
        AppertureDbContext db,
        DateTimeOffset? lastAttemptedUtc,
        bool enabled = true,
        bool withIntegration = false)
    {
        const string integrationId = "int_hw";
        db.ScheduleDefinitions.Add(new ScheduleDefinitionEntity
        {
            Id = "sched1",
            Name = "every-minute",
            RecurrenceExpression = "* * * * *",
            TimeZone = "UTC",
            Enabled = enabled,
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
        if (lastAttemptedUtc is not null)
        {
            db.ScheduleStates.Add(new ScheduleStateEntity
            {
                AssignmentId = "a1",
                LastAttemptedUtc = lastAttemptedUtc
            });
        }

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

    private static ServiceCollection CreateServices(AppertureDbContext db, string hostId)
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
        services.AddSingleton<ISchedulerClaimOptions>(new DefaultSchedulerClaimOptions(5));
        services.AddScoped<IScheduleDispatchRunner, ScheduleDispatchRunner>();
        return services;
    }
}
