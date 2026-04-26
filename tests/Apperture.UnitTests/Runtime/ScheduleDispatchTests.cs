using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
using Apperture.RuntimePlane.Dispatch.SqlServer;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sample.HelloWorld;
using Xunit;

namespace Apperture.UnitTests.Runtime;

public sealed class ScheduleDispatchTests
{
    /// <summary>
    /// Fixed clock so * * * * * always has exactly one tick in (lastAttempted+1s, asOf]: 12:01:00 ∈ (12:00:01, 12:01:30].
    /// Wall-clock-based seeds fail when UtcNow sits in the last ~34s of a minute (no minute boundary in the window).
    /// </summary>
    private static readonly DateTimeOffset DeterministicAsOfUtc = new(2025, 6, 15, 12, 1, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset DeterministicLastAttemptedUtc = new(2025, 6, 15, 12, 0, 0, 0, TimeSpan.Zero);

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    [Fact]
    public async Task RunCycleAsync_WhenDueScheduleAndModuleEnabled_DispatchesWorkflow()
    {
        var (startRequestCount, runOutcome) = await RunSchedulerCycleAndGetResultsAsync(moduleEnabled: true, workflowDefinitionId: "Sample.HelloWorld.HelloWorld");
        startRequestCount.Should().Be(1);
        runOutcome.Should().Be("Dispatched");
    }

    [Fact]
    public async Task RunCycleAsync_WhenModuleDisabled_BlocksDispatch_LeavesBlockedEvidence()
    {
        var (startRequestCount, runOutcome) = await RunSchedulerCycleAndGetResultsAsync(moduleEnabled: false, workflowDefinitionId: "Sample.HelloWorld.HelloWorld");
        startRequestCount.Should().Be(0, "disabled module must not create new work");
        runOutcome.Should().Be("BlockedByLifecycle");
    }

    [Fact]
    public async Task RunCycleAsync_WhenTargetInvalid_FailsWithReason()
    {
        var (startRequestCount, runOutcome) = await RunSchedulerCycleAndGetResultsAsync(moduleEnabled: true, workflowDefinitionId: null);
        startRequestCount.Should().Be(0);
        runOutcome.Should().Be("Failed");
    }

    [Fact]
    public async Task RunCycleAsync_WhenLegacyScheduleHasNoIntegration_AuditsCompatibilityWarning()
    {
        var db = NewDb();
        SeedScheduleWithWorkflowTarget(
            db,
            "s-legacy",
            "a-legacy",
            "Sample.HelloWorld.HelloWorld",
            "* * * * *",
            "UTC",
            lastAttemptedUtc: DeterministicLastAttemptedUtc,
            integrationId: null);
        await db.SaveChangesAsync();

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
        services.AddSingleton<ISchedulerHostIdentity>(new DefaultSchedulerHostIdentity("test"));
        services.AddSingleton<ISchedulerClaimOptions>(new DefaultSchedulerClaimOptions(5));
        services.AddScoped<IScheduleDispatchRunner, ScheduleDispatchRunner>();

        await using var scope = services.BuildServiceProvider().CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();
        await runner.RunCycleAsync(new RuntimePlaneId("local"), DeterministicAsOfUtc);

        var ctx = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var warningAuditCount = await ctx.AuditLogs
            .AsNoTracking()
            .CountAsync(x => x.Action == SchedulerAuditActionNames.LegacyScheduleWithoutIntegration);
        warningAuditCount.Should().Be(1);
    }

    [Fact]
    public async Task RunCycleAsync_WhenDueOperationScheduleAndModuleEnabled_DispatchesOperation()
    {
        var (opRequestCount, runOutcome) = await RunSchedulerCycleForOperationAsync(moduleEnabled: true, operationDefinitionId: "Sample.HelloWorld.DoWork");
        opRequestCount.Should().Be(1);
        runOutcome.Should().Be("Dispatched");
    }

    [Fact]
    public async Task RunCycleAsync_WhenDueOperationScheduleAndModuleDisabled_BlocksDispatch()
    {
        var (opRequestCount, runOutcome) = await RunSchedulerCycleForOperationAsync(moduleEnabled: false, operationDefinitionId: "Sample.HelloWorld.DoWork");
        opRequestCount.Should().Be(0);
        runOutcome.Should().Be("BlockedByLifecycle");
    }

    [Fact]
    public async Task RunCycleAsync_WhenOperationTargetInvalid_FailsWithReason()
    {
        var (opRequestCount, runOutcome) = await RunSchedulerCycleForOperationAsync(moduleEnabled: true, operationDefinitionId: null);
        opRequestCount.Should().Be(0);
        runOutcome.Should().Be("Failed");
    }

    [Fact]
    public async Task RunCycleAsync_WhenIntegrationInactive_BlocksDispatchWithOperationalReason()
    {
        var db = NewDb();
        SeedIntegration(db, "int1", "Sample.HelloWorld", IntegrationState.Inactive);
        SeedScheduleWithWorkflowTarget(
            db,
            "s1",
            "a1",
            "Sample.HelloWorld.HelloWorld",
            "* * * * *",
            "UTC",
            lastAttemptedUtc: DeterministicLastAttemptedUtc,
            integrationId: "int1");
        await db.SaveChangesAsync();

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
        services.AddSingleton<ISchedulerHostIdentity>(new DefaultSchedulerHostIdentity("test"));
        services.AddSingleton<ISchedulerClaimOptions>(new DefaultSchedulerClaimOptions(5));
        services.AddScoped<IScheduleDispatchRunner, ScheduleDispatchRunner>();

        await using var scope = services.BuildServiceProvider().CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();
        await runner.RunCycleAsync(new RuntimePlaneId("local"), DeterministicAsOfUtc);
        var ctx = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var startRequestCount = await ctx.WorkflowStartRequests.AsNoTracking().CountAsync();
        var run = await ctx.ScheduleRuns.AsNoTracking().FirstOrDefaultAsync();
        startRequestCount.Should().Be(0);
        run?.Outcome.Should().Be("BlockedByLifecycle");
        run?.OutcomeReason.Should().Contain("requires Active state");
    }

    /// <summary>
    /// First-run with no prior state uses bounded 5-minute lookback (not 24h).
    /// Hourly cron yields 0 or 1 firing in that window, preventing burst dispatch.
    /// </summary>
    [Fact]
    public async Task RunCycleAsync_FirstRun_WithNoPriorState_UsesBoundedLookback_NoBurstDispatch()
    {
        var db = NewDb();
        // Hourly cron, NO prior state - evaluator uses FirstRunLookbackMinutes (5) instead of 24h
        SeedScheduleWithWorkflowTarget(db, "s1", "a1", "Sample.HelloWorld.HelloWorld", "0 * * * *", "UTC", lastAttemptedUtc: null);
        await db.SaveChangesAsync();

        // 14:03Z → lookback from 13:58Z includes the 14:00 hourly tick (deterministic; avoids wall-clock flakiness).
        var asOfUtc = new DateTimeOffset(2025, 6, 15, 14, 3, 0, 0, TimeSpan.Zero);

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
        services.AddSingleton<ISchedulerHostIdentity>(new DefaultSchedulerHostIdentity("test"));
        services.AddSingleton<ISchedulerClaimOptions>(new DefaultSchedulerClaimOptions(5));
        services.AddScoped<IScheduleDispatchRunner, ScheduleDispatchRunner>();

        await using (var scope = services.BuildServiceProvider().CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();
            await runner.RunCycleAsync(new RuntimePlaneId("local"), asOfUtc);

            var ctx = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            var startRequestCount = await ctx.WorkflowStartRequests.AsNoTracking().CountAsync();
            var runCount = await ctx.ScheduleRuns.AsNoTracking().CountAsync();

            // With 5-min lookback and hourly cron: 0 or 1 firing. Never 24 (the old 24h behavior).
            startRequestCount.Should().BeLessThanOrEqualTo(1, "bounded first-run lookback prevents 24-run burst");
            runCount.Should().BeLessThanOrEqualTo(1);
        }
    }

    private static void SeedScheduleWithWorkflowTarget(
        AppertureDbContext db,
        string scheduleId,
        string assignmentId,
        string? workflowDefinitionId,
        string cron,
        string timeZone,
        DateTimeOffset? lastAttemptedUtc = null,
        string? integrationId = null)
    {
        var configJson = workflowDefinitionId is not null
            ? $"{{\"workflowDefinitionId\":\"{workflowDefinitionId}\"}}"
            : null;

        db.ScheduleDefinitions.Add(new ScheduleDefinitionEntity
        {
            Id = scheduleId,
            IntegrationId = integrationId,
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
                LastAttemptedUtc = lastAttemptedUtc
            });
        }
    }

    private static void SeedScheduleWithOperationTarget(
        AppertureDbContext db,
        string scheduleId,
        string assignmentId,
        string? operationDefinitionId,
        string cron,
        string timeZone,
        DateTimeOffset? lastAttemptedUtc = null,
        string? integrationId = null)
    {
        var configJson = operationDefinitionId is not null
            ? $"{{\"operationDefinitionId\":\"{operationDefinitionId}\"}}"
            : null;

        db.ScheduleDefinitions.Add(new ScheduleDefinitionEntity
        {
            Id = scheduleId,
            IntegrationId = integrationId,
            Name = "Test",
            RecurrenceExpression = cron,
            TimeZone = timeZone,
            Enabled = true,
            TargetJobType = (int)ScheduledJobType.ModuleOperationExecution,
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
                LastAttemptedUtc = lastAttemptedUtc
            });
        }
    }

    private static async Task<(int StartRequestCount, string RunOutcome)> RunSchedulerCycleAndGetResultsAsync(bool moduleEnabled, string? workflowDefinitionId)
    {
        var db = NewDb();
        // Every-minute cron + state: exactly one tick in (lastAttempted+1s, asOfUtc] (see Deterministic* constants).
        SeedScheduleWithWorkflowTarget(db, "s1", "a1", workflowDefinitionId, "* * * * *", "UTC", lastAttemptedUtc: DeterministicLastAttemptedUtc);
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton(db);

        services.AddScoped<IScheduleRepository, SqlScheduleRepository>();
        services.AddScoped<IIntegrationRepository, SqlIntegrationRepository>();
        services.AddScoped<IScheduleEvaluator, CronScheduleEvaluator>();
        services.AddScoped<IAuditService, SqlAuditService>();

        var helloWorld = new HelloWorldModule();
        var modules = new IModule[] { helloWorld };
        var filter = moduleEnabled
            ? (IModuleActivationFilter?)null
            : new DenyAllSchedulerActivationFilter();
        services.AddSingleton<IEnumerable<IModule>>(modules);
        services.AddSingleton<IWorkflowDefinitionResolver>(sp =>
            new WorkflowDefinitionResolver(sp.GetRequiredService<IEnumerable<IModule>>(), filter));
        services.AddSingleton<IOperationRegistryService>(sp =>
            new OperationRegistryService(sp.GetRequiredService<IEnumerable<IModule>>(), filter));
        services.AddScoped<ISchedulerLifecycleGuard, SchedulerLifecycleGuard>();
        services.AddScoped<IWorkflowPersistence, SqlWorkflowPersistence>();
        services.AddScoped<IWorkflowStartDispatchService, WorkflowStartDispatchService>();
        services.AddScoped<IOperationStartDispatchService, OperationStartDispatchService>();
        services.AddSingleton<ISchedulerHostIdentity>(new DefaultSchedulerHostIdentity("test"));
        services.AddSingleton<ISchedulerClaimOptions>(new DefaultSchedulerClaimOptions(5));
        services.AddScoped<IScheduleDispatchRunner, ScheduleDispatchRunner>();

        await using (var scope = services.BuildServiceProvider().CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();
            await runner.RunCycleAsync(new RuntimePlaneId("local"), DeterministicAsOfUtc);

            var ctx = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            var startRequestCount = await ctx.WorkflowStartRequests.AsNoTracking().CountAsync();
            var run = await ctx.ScheduleRuns.AsNoTracking().FirstOrDefaultAsync();
            return (startRequestCount, run?.Outcome ?? "None");
        }
    }

    private static async Task<(int OperationRequestCount, string RunOutcome)> RunSchedulerCycleForOperationAsync(bool moduleEnabled, string? operationDefinitionId)
    {
        var db = NewDb();
        SeedScheduleWithOperationTarget(db, "s1", "a1", operationDefinitionId, "* * * * *", "UTC", lastAttemptedUtc: DeterministicLastAttemptedUtc);
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddScoped<IScheduleRepository, SqlScheduleRepository>();
        services.AddScoped<IIntegrationRepository, SqlIntegrationRepository>();
        services.AddScoped<IScheduleEvaluator, CronScheduleEvaluator>();
        services.AddScoped<IAuditService, SqlAuditService>();

        var helloWorld = new HelloWorldModule();
        var modules = new IModule[] { helloWorld };
        var filter = moduleEnabled ? (IModuleActivationFilter?)null : new DenyAllSchedulerActivationFilter();
        services.AddSingleton<IEnumerable<IModule>>(modules);
        services.AddSingleton<IWorkflowDefinitionResolver>(sp =>
            new WorkflowDefinitionResolver(sp.GetRequiredService<IEnumerable<IModule>>(), filter));
        services.AddSingleton<IOperationRegistryService>(sp =>
            new OperationRegistryService(sp.GetRequiredService<IEnumerable<IModule>>(), filter));
        services.AddScoped<ISchedulerLifecycleGuard, SchedulerLifecycleGuard>();
        services.AddScoped<IWorkflowPersistence, SqlWorkflowPersistence>();
        services.AddScoped<IWorkflowStartDispatchService, WorkflowStartDispatchService>();
        services.AddScoped<IOperationStartDispatchService, OperationStartDispatchService>();
        services.AddSingleton<ISchedulerHostIdentity>(new DefaultSchedulerHostIdentity("test"));
        services.AddSingleton<ISchedulerClaimOptions>(new DefaultSchedulerClaimOptions(5));
        services.AddScoped<IScheduleDispatchRunner, ScheduleDispatchRunner>();

        await using (var scope = services.BuildServiceProvider().CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();
            await runner.RunCycleAsync(new RuntimePlaneId("local"), DeterministicAsOfUtc);

            var ctx = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            var opRequestCount = await ctx.OperationStartRequests.AsNoTracking().CountAsync();
            var run = await ctx.ScheduleRuns.AsNoTracking().FirstOrDefaultAsync();
            return (opRequestCount, run?.Outcome ?? "None");
        }
    }

    private sealed class DenyAllSchedulerActivationFilter : IModuleActivationFilter
    {
        public ValueTask<bool> IsEnabledAsync(string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }

    private static void SeedIntegration(AppertureDbContext db, string integrationId, string moduleId, IntegrationState state)
    {
        db.Integrations.Add(new IntegrationEntity
        {
            Id = integrationId,
            DisplayName = "Test integration",
            ModuleId = moduleId,
            State = (int)state,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }
}
