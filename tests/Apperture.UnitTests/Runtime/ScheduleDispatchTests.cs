using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Scheduling;
using Apperture.ControlPlane.Security;
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

    private static void SeedScheduleWithWorkflowTarget(
        AppertureDbContext db,
        string scheduleId,
        string assignmentId,
        string? workflowDefinitionId,
        string cron,
        string timeZone,
        DateTimeOffset? lastAttemptedUtc = null)
    {
        var configJson = workflowDefinitionId is not null
            ? $"{{\"workflowDefinitionId\":\"{workflowDefinitionId}\"}}"
            : null;

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
                LastAttemptedUtc = lastAttemptedUtc
            });
        }
    }

    private static async Task<(int StartRequestCount, string RunOutcome)> RunSchedulerCycleAndGetResultsAsync(bool moduleEnabled, string? workflowDefinitionId)
    {
        var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        // Use every-minute cron and state so evaluator window yields exactly 1 due firing
        SeedScheduleWithWorkflowTarget(db, "s1", "a1", workflowDefinitionId, "* * * * *", "UTC", lastAttemptedUtc: now.AddSeconds(-90));
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddScoped<AppertureDbContext>(_ => db);

        services.AddScoped<IScheduleRepository, SqlScheduleRepository>();
        services.AddScoped<IScheduleEvaluator, CronScheduleEvaluator>();
        services.AddScoped<IAuditService, SqlAuditService>();

        var helloWorld = new HelloWorldModule();
        var filter = moduleEnabled
            ? (IModuleActivationFilter?)null
            : new DenyAllSchedulerActivationFilter();
        services.AddSingleton<IWorkflowDefinitionResolver>(_ =>
            new WorkflowDefinitionResolver([helloWorld], filter));

        services.AddScoped<ISchedulerLifecycleGuard, SchedulerLifecycleGuard>();
        services.AddScoped<IWorkflowPersistence, SqlWorkflowPersistence>();
        services.AddScoped<IWorkflowStartDispatchService, WorkflowStartDispatchService>();
        services.AddScoped<IScheduleDispatchRunner, ScheduleDispatchRunner>();

        await using (var scope = services.BuildServiceProvider().CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();
            await runner.RunCycleAsync(new RuntimePlaneId("local"), DateTimeOffset.UtcNow);

            var ctx = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            var startRequestCount = await ctx.WorkflowStartRequests.AsNoTracking().CountAsync();
            var run = await ctx.ScheduleRuns.AsNoTracking().FirstOrDefaultAsync();
            return (startRequestCount, run?.Outcome ?? "None");
        }
    }

    private sealed class DenyAllSchedulerActivationFilter : IModuleActivationFilter
    {
        public ValueTask<bool> IsEnabledAsync(string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }
}
