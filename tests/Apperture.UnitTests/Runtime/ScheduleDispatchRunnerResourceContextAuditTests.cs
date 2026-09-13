using Apperture.Core.Application.Integration;
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

/// <summary>
/// CTX-2 completion (Task 5): every <see cref="ScheduleDispatchRunner"/> audit call site must carry
/// <c>ResourceContextId</c> sourced from the firing Schedule's own <see cref="ScheduleDefinition.ResourceContextId"/>
/// (or, for the stale-claim recovery branch, the definition fetched at recovery time).
/// </summary>
public sealed class ScheduleDispatchRunnerResourceContextAuditTests
{
    private const string ContextId = "app-a";

    /// <summary>See ScheduleDispatchTests: wall-clock * * * * * seeds miss a minute tick in the last ~34s of a minute.</summary>
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
    public async Task WorkflowBlockedByLifecycle_CarriesScheduleResourceContextId()
    {
        var db = NewDb();
        SeedScheduleWithWorkflowTarget(db, "s1", "a1", "Sample.HelloWorld.HelloWorld", "* * * * *", "UTC", DeterministicLastAttemptedUtc);
        await db.SaveChangesAsync();

        await RunCycleAsync(db, [new HelloWorldModule()], moduleEnabled: false);

        var record = await SingleAuditAsync(db, SchedulerAuditActionNames.BlockedByLifecycle);
        record.ResourceContextId.Should().Be(ContextId);
    }

    [Fact]
    public async Task WorkflowDispatched_CarriesScheduleResourceContextId()
    {
        var db = NewDb();
        SeedScheduleWithWorkflowTarget(db, "s1", "a1", "Sample.HelloWorld.HelloWorld", "* * * * *", "UTC", DeterministicLastAttemptedUtc);
        await db.SaveChangesAsync();

        await RunCycleAsync(db, [new HelloWorldModule()], moduleEnabled: true);

        var record = await SingleAuditAsync(db, SchedulerAuditActionNames.Dispatched);
        record.ResourceContextId.Should().Be(ContextId);
    }

    [Fact]
    public async Task WorkflowFailed_CarriesScheduleResourceContextId()
    {
        var db = NewDb();
        SeedScheduleWithWorkflowTarget(db, "s1", "a1", "Sample.HelloWorld.HelloWorld", "* * * * *", "UTC", DeterministicLastAttemptedUtc);
        await db.SaveChangesAsync();

        await RunCycleAsync(
            db,
            [new HelloWorldModule()],
            moduleEnabled: true,
            workflowDispatchStub: new NullReturningWorkflowDispatchService());

        var record = await SingleAuditAsync(db, SchedulerAuditActionNames.Failed);
        record.ResourceContextId.Should().Be(ContextId);
    }

    [Fact]
    public async Task OperationBlockedByLifecycle_CarriesScheduleResourceContextId()
    {
        var db = NewDb();
        SeedScheduleWithOperationTarget(db, "s1", "a1", "Sample.HelloWorld.DoWork", "* * * * *", "UTC", DeterministicLastAttemptedUtc);
        await db.SaveChangesAsync();

        await RunCycleAsync(db, [new HelloWorldModule()], moduleEnabled: false);

        var record = await SingleAuditAsync(db, SchedulerAuditActionNames.BlockedByLifecycle);
        record.ResourceContextId.Should().Be(ContextId);
    }

    [Fact]
    public async Task OperationDispatched_CarriesScheduleResourceContextId()
    {
        var db = NewDb();
        SeedScheduleWithOperationTarget(db, "s1", "a1", "Sample.HelloWorld.DoWork", "* * * * *", "UTC", DeterministicLastAttemptedUtc);
        await db.SaveChangesAsync();

        await RunCycleAsync(db, [new HelloWorldModule()], moduleEnabled: true);

        var record = await SingleAuditAsync(db, SchedulerAuditActionNames.Dispatched);
        record.ResourceContextId.Should().Be(ContextId);
    }

    [Fact]
    public async Task OperationFailed_CarriesScheduleResourceContextId()
    {
        var db = NewDb();
        SeedScheduleWithOperationTarget(db, "s1", "a1", "Sample.HelloWorld.DoWork", "* * * * *", "UTC", DeterministicLastAttemptedUtc);
        await db.SaveChangesAsync();

        await RunCycleAsync(
            db,
            [new HelloWorldModule()],
            moduleEnabled: true,
            operationDispatchStub: new NullReturningOperationDispatchService());

        var record = await SingleAuditAsync(db, SchedulerAuditActionNames.Failed);
        record.ResourceContextId.Should().Be(ContextId);
    }

    [Fact]
    public async Task ClaimRecovered_CarriesScheduleResourceContextId()
    {
        var db = NewDb();
        var asOfUtc = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var fireUtc = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // Yearly cron: no occurrence in the first-run 5-minute lookback, so only stale recovery runs
        // (avoids TryClaimFireAsync inserting a second row for a different cron instant).
        SeedScheduleWithWorkflowTarget(db, "s1", "a1", "Sample.HelloWorld.HelloWorld", "0 0 1 1 *", "UTC", lastAttemptedUtc: null);
        await db.SaveChangesAsync();

        db.ScheduleRuns.Add(new ScheduleRunEntity
        {
            ScheduleId = "s1",
            AssignmentId = "a1",
            FireTimeUtc = fireUtc,
            StartedAtUtc = asOfUtc.AddMinutes(-10),
            Outcome = "InProgress",
            ClaimedByHostId = "crashed-host"
        });
        await db.SaveChangesAsync();

        var services = BuildServices(db, [new HelloWorldModule()], moduleEnabled: true);
        await using (var scope = services.BuildServiceProvider().CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();
            await runner.RunCycleAsync(new RuntimePlaneId("local"), asOfUtc);
        }

        var record = await SingleAuditAsync(db, SchedulerAuditActionNames.ClaimRecovered);
        record.ResourceContextId.Should().Be(ContextId);
    }

    [Fact]
    public async Task BlockedByReadiness_CarriesScheduleResourceContextId()
    {
        var db = NewDb();
        SeedIntegration(db, "int1", "Sample.HelloWorld", IntegrationState.Active);
        SeedScheduleWithWorkflowTarget(
            db, "s1", "a1", "Sample.HelloWorld.HelloWorld", "* * * * *", "UTC",
            lastAttemptedUtc: DeterministicLastAttemptedUtc, integrationId: "int1");
        await db.SaveChangesAsync();

        var services = BuildServices(db, [new HelloWorldModule()], moduleEnabled: true);
        await using (var scope = services.BuildServiceProvider().CreateAsyncScope())
        {
            var readiness = (TestDispatchReadinessEvaluator)scope.ServiceProvider.GetRequiredService<IIntegrationDispatchReadinessEvaluator>();
            readiness.NextResult = new IntegrationReadinessProjection(
                "int1",
                "Sample.HelloWorld",
                false,
                1,
                0,
                [new IntegrationReadinessIssue("missing_slot_binding", "Required connection 'DirectoryApi' has no binding.", "DirectoryApi", null, null)]);

            var runner = scope.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();
            await runner.RunCycleAsync(new RuntimePlaneId("local"), DeterministicAsOfUtc);
        }

        var record = await SingleAuditAsync(db, SchedulerAuditActionNames.BlockedByReadiness);
        record.ResourceContextId.Should().Be(ContextId);
    }

    [Fact]
    public async Task IntegrationNotActive_BlockedByLifecycle_CarriesScheduleResourceContextId()
    {
        var db = NewDb();
        SeedIntegration(db, "int1", "Sample.HelloWorld", IntegrationState.Inactive);
        SeedScheduleWithWorkflowTarget(
            db, "s1", "a1", "Sample.HelloWorld.HelloWorld", "* * * * *", "UTC",
            lastAttemptedUtc: DeterministicLastAttemptedUtc, integrationId: "int1");
        await db.SaveChangesAsync();

        await RunCycleAsync(db, [new HelloWorldModule()], moduleEnabled: true);

        var record = await SingleAuditAsync(db, SchedulerAuditActionNames.BlockedByLifecycle);
        record.ResourceContextId.Should().Be(ContextId);
    }

    [Fact]
    public async Task LegacyScheduleWithoutIntegration_CarriesScheduleResourceContextId()
    {
        var db = NewDb();
        SeedScheduleWithWorkflowTarget(
            db, "s-legacy", "a-legacy", "Sample.HelloWorld.HelloWorld", "* * * * *", "UTC",
            lastAttemptedUtc: DeterministicLastAttemptedUtc, integrationId: null);
        await db.SaveChangesAsync();

        await RunCycleAsync(db, [new HelloWorldModule()], moduleEnabled: true);

        var record = await SingleAuditAsync(db, SchedulerAuditActionNames.LegacyScheduleWithoutIntegration);
        record.ResourceContextId.Should().Be(ContextId);
    }

    private static async Task RunCycleAsync(
        AppertureDbContext db,
        IModule[] modules,
        bool moduleEnabled,
        IWorkflowStartDispatchService? workflowDispatchStub = null,
        IOperationStartDispatchService? operationDispatchStub = null)
    {
        var services = BuildServices(db, modules, moduleEnabled, workflowDispatchStub, operationDispatchStub);
        await using var scope = services.BuildServiceProvider().CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();
        await runner.RunCycleAsync(new RuntimePlaneId("local"), DeterministicAsOfUtc);
    }

    private static async Task<AuditLogEntity> SingleAuditAsync(AppertureDbContext db, string action) =>
        await db.AuditLogs.AsNoTracking().SingleAsync(x => x.Action == action);

    private static ServiceCollection BuildServices(
        AppertureDbContext db,
        IModule[] modules,
        bool moduleEnabled,
        IWorkflowStartDispatchService? workflowDispatchStub = null,
        IOperationStartDispatchService? operationDispatchStub = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddScoped<IScheduleRepository, SqlScheduleRepository>();
        services.AddScoped<IIntegrationRepository, SqlIntegrationRepository>();
        services.AddScoped<IScheduleEvaluator, CronScheduleEvaluator>();
        services.AddScoped<IAuditService, SqlAuditService>();

        var filter = moduleEnabled ? (IModuleActivationFilter?)null : new DenyAllSchedulerActivationFilter();
        services.AddSingleton<IEnumerable<IModule>>(modules);
        services.AddSingleton<IWorkflowDefinitionResolver>(sp =>
            new WorkflowDefinitionResolver(sp.GetRequiredService<IEnumerable<IModule>>(), filter));
        services.AddSingleton<IOperationRegistryService>(sp =>
            new OperationRegistryService(sp.GetRequiredService<IEnumerable<IModule>>(), filter));
        services.AddSingleton<IProcedureRegistryService>(sp =>
            new ProcedureRegistryService(sp.GetRequiredService<IEnumerable<IModule>>(), filter));
        services.AddScoped<ISchedulerLifecycleGuard, SchedulerLifecycleGuard>();
        services.AddScoped<IWorkflowPersistence, SqlWorkflowPersistence>();

        if (workflowDispatchStub is not null)
            services.AddSingleton(workflowDispatchStub);
        else
            services.AddScoped<IWorkflowStartDispatchService, WorkflowStartDispatchService>();

        if (operationDispatchStub is not null)
            services.AddSingleton(operationDispatchStub);
        else
            services.AddScoped<IOperationStartDispatchService, OperationStartDispatchService>();

        services.AddSingleton<ISchedulerHostIdentity>(new DefaultSchedulerHostIdentity("test"));
        services.AddSingleton<ISchedulerClaimOptions>(new DefaultSchedulerClaimOptions(5));
        services.AddScoped<IIntegrationDispatchReadinessEvaluator, TestDispatchReadinessEvaluator>();
        services.AddScoped<IScheduleDispatchRunner, ScheduleDispatchRunner>();
        return services;
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
            ResourceContextId = ContextId,
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
            ResourceContextId = ContextId,
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

    private sealed class DenyAllSchedulerActivationFilter : IModuleActivationFilter
    {
        public ValueTask<bool> IsEnabledAsync(string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }

    private sealed class TestDispatchReadinessEvaluator : IIntegrationDispatchReadinessEvaluator
    {
        public IntegrationReadinessProjection? NextResult { get; set; }

        public ValueTask<IntegrationReadinessProjection?> EvaluateForDispatchAsync(
            IntegrationId integrationId,
            string? procedureId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(NextResult ?? new IntegrationReadinessProjection(
                integrationId.Value,
                "Sample.HelloWorld",
                true,
                0,
                0,
                Array.Empty<IntegrationReadinessIssue>()));
    }

    private sealed class NullReturningWorkflowDispatchService : IWorkflowStartDispatchService
    {
        public ValueTask<WorkflowStartDispatchResult?> StartAsync(StartWorkflowRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowStartDispatchResult?>(null);
    }

    private sealed class NullReturningOperationDispatchService : IOperationStartDispatchService
    {
        public ValueTask<OperationStartDispatchResult?> StartAsync(StartOperationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<OperationStartDispatchResult?>(null);
    }
}
