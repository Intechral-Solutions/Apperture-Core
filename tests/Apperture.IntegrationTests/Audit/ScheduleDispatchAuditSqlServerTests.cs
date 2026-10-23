using Apperture.Core.Application.Integration;
using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
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
using Microsoft.Extensions.Logging;
using Sample.HelloWorld;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The scheduler's dispatch audit against SQL Server, on locking READ COMMITTED and READ_COMMITTED_SNAPSHOT catalogs.
/// <c>AuditLogs.CorrelationId</c> is <c>nvarchar(64)</c> and SQL Server rejects an overlong value, which an in-memory database
/// does not: the scheduler used to build a 67-character correlation identifier, every <c>scheduler.dispatched</c> insert failed,
/// and the runner's best-effort catch swallowed it, so the run executed with no audit row.
/// <list type="bullet">
/// <item>A scheduled dispatch executes, and its <c>scheduler.dispatched</c> row is persisted, attributed to the scheduler, naming
/// the schedule and the run and nothing else, with a correlation identifier the column holds.</item>
/// <item>The stale-claim recovery of a fire records <c>scheduler.claim_recovered</c> and then the dispatch, under one identifier.</item>
/// <item>The next fire of a recurring schedule dispatches under its own identifier.</item>
/// <item>A refused dispatch (module disabled) records its row under the same identifier form.</item>
/// <item>When an audit row cannot be recorded the dispatch still succeeds and the failure is logged, naming the schedule.</item>
/// </list>
/// Fails, never skips, without SQL Server (<see cref="AuditSqlScratch"/>). Each case scopes its assertions to a schedule id of its
/// own, because the audit table is append-only.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ScheduleDispatchAuditSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private const string WorkflowId = "Sample.HelloWorld.HelloWorld";

    /// <summary>One minute past the last attempt, so the next due fire of <c>* * * * *</c> is exactly 12:01:00.</summary>
    private static readonly DateTimeOffset AsOfUtc = new(2025, 6, 15, 12, 1, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset LastAttemptedUtc = new(2025, 6, 15, 12, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset FirstFireUtc = new(2025, 6, 15, 12, 1, 0, 0, TimeSpan.Zero);

    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public ScheduleDispatchAuditSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    public static IEnumerable<object[]> Isolation() => [[false], [true]];

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_scheduled_dispatch_executes_and_persists_its_audit_row_under_a_correlation_the_column_holds(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        _output.WriteLine(scratch.ExecutionMarker);
        var scheduleId = NewScheduleId();
        await using var db = scratch.NewDb();
        await SeedWorkflowScheduleAsync(db, scheduleId, "* * * * *");

        var result = await RunCycleAsync(db, scheduleId, AsOfUtc);

        result.DispatchedCount.Should().Be(1);
        var run = await db.ScheduleRuns.AsNoTracking().SingleAsync(r => r.ScheduleId == scheduleId);
        run.Outcome.Should().Be("Dispatched");
        run.RunId.Should().NotBeNullOrEmpty("the scheduled dispatch executed");

        var row = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.ScheduleId == scheduleId && a.Action == SchedulerAuditActionNames.Dispatched);
        row.Actor.Should().Be("system:scheduler");
        row.RunId.Should().Be(run.RunId);
        row.Details.Should().Be($"schedule:{scheduleId};RunId={run.RunId}", "the row names the schedule and the run and carries no parameter values");
        row.CorrelationId.Should().Be(ScheduleFireCorrelation.ForAudit(scheduleId, FirstFireUtc));
        row.CorrelationId!.Length.Should().BeLessThanOrEqualTo(64);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_recovered_stale_claim_and_its_dispatch_are_recorded_under_one_identifier(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        _output.WriteLine(scratch.ExecutionMarker);
        var scheduleId = NewScheduleId();
        var staleFireUtc = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using var db = scratch.NewDb();
        // A yearly cron has no occurrence in the first-run lookback, so only the stale claim is a candidate.
        await SeedWorkflowScheduleAsync(db, scheduleId, "0 0 1 1 *", lastAttemptedUtc: null);
        db.ScheduleRuns.Add(new ScheduleRunEntity
        {
            ScheduleId = scheduleId,
            AssignmentId = AssignmentId(scheduleId),
            FireTimeUtc = staleFireUtc,
            StartedAtUtc = AsOfUtc.AddMinutes(-10),
            Outcome = "InProgress",
            ClaimedByHostId = "crashed-host",
        });
        await db.SaveChangesAsync();

        var result = await RunCycleAsync(db, scheduleId, new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero));

        result.DispatchedCount.Should().Be(1);
        var rows = await db.AuditLogs.AsNoTracking().Where(a => a.ScheduleId == scheduleId).OrderBy(a => a.Id).ToListAsync();
        // A schedule with no integration also records its legacy-compatibility warning for the fire: three rows, one identifier.
        rows.Select(r => r.Action).Should().Equal(
            SchedulerAuditActionNames.ClaimRecovered,
            SchedulerAuditActionNames.LegacyScheduleWithoutIntegration,
            SchedulerAuditActionNames.Dispatched);
        var expected = ScheduleFireCorrelation.ForAudit(scheduleId, staleFireUtc);
        rows.Select(r => r.CorrelationId).Should().AllBeEquivalentTo(expected, "every audit row of one fire is correlated by the fire");
        expected!.Length.Should().BeLessThanOrEqualTo(64);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task The_next_fire_of_a_recurring_schedule_dispatches_under_its_own_identifier(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        _output.WriteLine(scratch.ExecutionMarker);
        var scheduleId = NewScheduleId();
        await using var db = scratch.NewDb();
        await SeedWorkflowScheduleAsync(db, scheduleId, "* * * * *");

        (await RunCycleAsync(db, scheduleId, AsOfUtc)).DispatchedCount.Should().Be(1);
        // The first dispatch recorded the wall clock as its last attempt; look two minutes past it for the next fire.
        (await RunCycleAsync(db, scheduleId, DateTimeOffset.UtcNow.AddMinutes(2))).DispatchedCount.Should().Be(1);

        var fires = await db.ScheduleRuns.AsNoTracking().Where(r => r.ScheduleId == scheduleId).OrderBy(r => r.FireTimeUtc).ToListAsync();
        fires.Should().HaveCount(2);
        fires.Should().OnlyContain(r => r.Outcome == "Dispatched");
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.ScheduleId == scheduleId && a.Action == SchedulerAuditActionNames.Dispatched)
            .ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(r => r.CorrelationId).Should().BeEquivalentTo(
            fires.Select(f => ScheduleFireCorrelation.ForAudit(scheduleId, f.FireTimeUtc)));
        rows.Select(r => r.CorrelationId).Distinct().Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.CorrelationId!.Length <= 64);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_dispatch_the_module_state_refuses_is_recorded_under_the_same_identifier_form(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        _output.WriteLine(scratch.ExecutionMarker);
        var scheduleId = NewScheduleId();
        await using var db = scratch.NewDb();
        await SeedWorkflowScheduleAsync(db, scheduleId, "* * * * *");

        var result = await RunCycleAsync(db, scheduleId, AsOfUtc, moduleEnabled: false);

        result.DispatchedCount.Should().Be(0);
        var row = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.ScheduleId == scheduleId && a.Action == SchedulerAuditActionNames.BlockedByLifecycle);
        row.CorrelationId.Should().Be(ScheduleFireCorrelation.ForAudit(scheduleId, FirstFireUtc));
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task When_an_audit_row_cannot_be_recorded_the_dispatch_still_succeeds_and_the_failure_is_logged(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        _output.WriteLine(scratch.ExecutionMarker);
        var scheduleId = NewScheduleId();
        await using var db = scratch.NewDb();
        await SeedWorkflowScheduleAsync(db, scheduleId, "* * * * *");
        var log = new CapturingLogger<ScheduleDispatchRunner>();

        var result = await RunCycleAsync(db, scheduleId, AsOfUtc, auditOverride: new FailingAuditService(), logger: log);

        result.DispatchedCount.Should().Be(1, "the dispatch is not made to depend on its best-effort audit row");
        (await db.ScheduleRuns.AsNoTracking().SingleAsync(r => r.ScheduleId == scheduleId)).Outcome.Should().Be("Dispatched");
        (await db.AuditLogs.AsNoTracking().CountAsync(a => a.ScheduleId == scheduleId)).Should().Be(0);
        var warnings = log.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        // One warning for each row that could not be recorded (the legacy-compatibility warning row and the dispatch row), where the
        // runner used to swallow both silently.
        warnings.Should().HaveCount(2, "each unrecorded row is observable once");
        warnings.Should().OnlyContain(w => w.Message.Contains(scheduleId));
        warnings.Should().OnlyContain(w => w.Exception is InvalidOperationException);
    }

    private static string NewScheduleId() => Guid.NewGuid().ToString("N")[..24];

    private static string AssignmentId(string scheduleId) => "a" + scheduleId;

    /// <summary>A runtime plane of the case's own, so a cycle never picks up another case's schedule in the shared catalog.</summary>
    private static string PlaneId(string scheduleId) => "p" + scheduleId;

    private static async Task SeedWorkflowScheduleAsync(
        AppertureDbContext db,
        string scheduleId,
        string cron,
        DateTimeOffset? lastAttemptedUtc = default)
    {
        db.ScheduleDefinitions.Add(new ScheduleDefinitionEntity
        {
            Id = scheduleId,
            ResourceContextId = "shared",
            Name = "Dispatch audit " + scheduleId,
            RecurrenceExpression = cron,
            TimeZone = "UTC",
            Enabled = true,
            TargetJobType = (int)ScheduledJobType.WorkflowExecution,
            TargetJobConfigurationJson = $"{{\"workflowDefinitionId\":\"{WorkflowId}\"}}",
            Version = 1,
        });
        db.ScheduleAssignments.Add(new ScheduleAssignmentEntity
        {
            Id = AssignmentId(scheduleId),
            ScheduleId = scheduleId,
            RuntimePlaneId = PlaneId(scheduleId),
            DesiredVersion = 1,
            AppliedVersion = 1,
        });
        // The default seeds a last attempt so the next due fire is deterministic; null seeds none (first-run lookback).
        if (lastAttemptedUtc is null && cron == "* * * * *")
            lastAttemptedUtc = LastAttemptedUtc;
        if (lastAttemptedUtc is not null)
            db.ScheduleStates.Add(new ScheduleStateEntity { AssignmentId = AssignmentId(scheduleId), LastAttemptedUtc = lastAttemptedUtc });
        await db.SaveChangesAsync();
    }

    private static async Task<ScheduleDispatchCycleResult> RunCycleAsync(
        AppertureDbContext db,
        string scheduleId,
        DateTimeOffset asOfUtc,
        bool moduleEnabled = true,
        IAuditService? auditOverride = null,
        ILogger<ScheduleDispatchRunner>? logger = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddScoped<IScheduleRepository, SqlScheduleRepository>();
        services.AddScoped<IIntegrationRepository, SqlIntegrationRepository>();
        services.AddScoped<IScheduleEvaluator, CronScheduleEvaluator>();
        if (auditOverride is null)
            services.AddScoped<IAuditService, SqlAuditService>();
        else
            services.AddSingleton(auditOverride);

        var filter = moduleEnabled ? (IModuleActivationFilter?)null : new DenyAllActivationFilter();
        services.AddSingleton<IEnumerable<IModule>>([new HelloWorldModule()]);
        services.AddSingleton<IWorkflowDefinitionResolver>(sp => new WorkflowDefinitionResolver(sp.GetRequiredService<IEnumerable<IModule>>(), filter));
        services.AddSingleton<IOperationRegistryService>(sp => new OperationRegistryService(sp.GetRequiredService<IEnumerable<IModule>>(), filter));
        services.AddSingleton<IProcedureRegistryService>(sp => new ProcedureRegistryService(sp.GetRequiredService<IEnumerable<IModule>>(), filter));
        services.AddScoped<ISchedulerLifecycleGuard, SchedulerLifecycleGuard>();
        services.AddScoped<IWorkflowPersistence, SqlWorkflowPersistence>();
        services.AddScoped<IWorkflowStartDispatchService, WorkflowStartDispatchService>();
        services.AddScoped<IOperationStartDispatchService, OperationStartDispatchService>();
        services.AddSingleton<ISchedulerHostIdentity>(new DefaultSchedulerHostIdentity("test"));
        services.AddSingleton<ISchedulerClaimOptions>(new DefaultSchedulerClaimOptions(5));
        services.AddScoped<IIntegrationDispatchReadinessEvaluator, ReadyEvaluator>();
        if (logger is not null)
            services.AddSingleton(logger);
        services.AddScoped<IScheduleDispatchRunner, ScheduleDispatchRunner>();

        await using var scope = services.BuildServiceProvider().CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IScheduleDispatchRunner>();
        return await runner.RunCycleAsync(new RuntimePlaneId(PlaneId(scheduleId)), asOfUtc);
    }

    private sealed class DenyAllActivationFilter : IModuleActivationFilter
    {
        public ValueTask<bool> IsEnabledAsync(string moduleId, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    private sealed class ReadyEvaluator : IIntegrationDispatchReadinessEvaluator
    {
        public ValueTask<IntegrationReadinessProjection?> EvaluateForDispatchAsync(
            IntegrationId integrationId,
            string? procedureId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IntegrationReadinessProjection?>(
                new IntegrationReadinessProjection(integrationId.Value, "Sample.HelloWorld", true, 0, 0, Array.Empty<IntegrationReadinessIssue>()));
    }

    /// <summary>An audit service whose every append fails, the way an unwritable audit table would.</summary>
    private sealed class FailingAuditService : IAuditService
    {
        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("audit append failed (test)");

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
