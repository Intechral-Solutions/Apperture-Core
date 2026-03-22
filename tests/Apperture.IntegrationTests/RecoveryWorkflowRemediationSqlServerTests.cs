using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Recovery;
using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Scheduling;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// Integration tests proving blocked workflow remediation flows through the trusted execution path.
/// Requires SQL Server (e.g. Docker on port 1433). Epic 31 corrective pass.
/// </summary>
public sealed class RecoveryWorkflowRemediationSqlServerTests
{
    private const string WorkflowId = "Sample.HelloWorld.HelloWorld";
    private const string ModuleId = "Sample.HelloWorld";

    [Fact]
    public async Task BlockedWorkflow_BecomesRetryableAfterModuleEnabled_DispatchesThroughTrustedPath()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is null)
        {
            throw new InvalidOperationException(
                "SQL Server not configured. Set MSSQL_SA_PASSWORD (e.g. in repo-root .env) and ensure SQL Server is running on localhost:1433.");
        }

        var services = new ServiceCollection();
        services.AddApperturePersistence(connectionString);
        await using var provider = services.BuildServiceProvider();

        var runId = "ep31-" + Guid.NewGuid().ToString("N")[..8];
        var instanceId = "wi-" + runId;

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            if (!await db.Database.CanConnectAsync())
            {
                throw new InvalidOperationException(
                    "SQL Server not reachable. Is the server running and is MSSQL_SA_PASSWORD correct?");
            }

            await db.Database.MigrateAsync();
            db.WorkflowInstances.Add(new WorkflowInstanceEntity
            {
                Id = instanceId,
                DefinitionId = WorkflowId,
                Status = WorkflowLifecycleStatusNames.Failed,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                UpdatedAt = DateTimeOffset.UtcNow,
                CorrelationKey = null,
                CurrentStepName = null,
                Version = 0
            });
            db.OperationRuns.Add(new OperationRunEntity
            {
                Id = runId,
                WorkflowInstanceId = instanceId,
                DefinitionId = WorkflowId,
                Status = WorkflowLifecycleStatusNames.Failed,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                FailureSummary = RunFailureCapture.DeferredWorkBlockedPrefix + "Workflow definition is not effectively active"
            });
            db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
            {
                RunId = runId,
                WorkflowInstanceId = instanceId,
                WorkflowDefinitionId = WorkflowId,
                Status = WorkflowLifecycleStatusNames.Failed,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                ParametersJson = "{}",
                CorrelationId = "corr-ep31"
            });
            await db.SaveChangesAsync();
        }

        var descriptor = new ModuleDescriptor(ModuleId, "HW", "1.0", null, null, null,
            [new WorkflowContributionDescriptor(WorkflowId, "HW")]);
        var catalog = new List<ModuleCatalogEntry>
        {
            new(descriptor, ModuleLifecycleState.InstalledDisabled, true, false, false, [])
        };
        var registry = new StubModuleRegistry(catalog, []);

        var moduleEntryDisabled = new RuntimeCompositionModuleEntry(
            ModuleId, "InstalledDisabled", true, false, false,
            [], AllowsNewStarts: false, HasInFlightWork: false, "module_disabled");
        var moduleEntryEnabled = new RuntimeCompositionModuleEntry(
            ModuleId, "InstalledEnabled", true, true, true,
            [], AllowsNewStarts: true, HasInFlightWork: false);
        var compositionDisabled = new HostEffectiveModuleSnapshot(
            DateTimeOffset.UtcNow, [moduleEntryDisabled], [],
            ExecutionSurfaceCatalog.ImmediatelyLifecycleGoverned,
            ExecutionSurfaceCatalog.RestartBoundSurfaceIds, []);
        var compositionEnabled = new HostEffectiveModuleSnapshot(
            DateTimeOffset.UtcNow, [moduleEntryEnabled], [ModuleId],
            ExecutionSurfaceCatalog.ImmediatelyLifecycleGoverned,
            ExecutionSurfaceCatalog.RestartBoundSurfaceIds, []);

        var toggleableComposition = new ToggleableCompositionService(compositionDisabled);
        var toggleableResolver = new ToggleableDefinitionResolver(
            new WorkflowDefinition(new WorkflowDefinitionId(WorkflowId), "HelloWorld", ModuleId, ["Step1"]));

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            var runKinds = new RunKindResolver(db);
            var health = new RuntimeHealthQueryService(
                toggleableComposition, registry,
                new StubScheduleManagement(Array.Empty<ScheduleDefinition>(), Array.Empty<ScheduleAssignment>()),
                new StubScheduleRepository(Array.Empty<ScheduleAssignment>()),
                runKinds, db);
            var recovery = new RecoveryQueryService(health);

            var blockedEntry = await recovery.GetBlockedWorkRemediationForRunAsync(runId);
            blockedEntry.Should().NotBeNull();
            blockedEntry!.RetryEligibleNow.Should().BeFalse("module is disabled");
            blockedEntry.RetryEligibleReason.Should().Contain(ModuleId);

            var dispatch = new WorkflowStartDispatchService(
                toggleableResolver, new SqlWorkflowPersistence(db), db);
            var retryService = new WorkflowRetryService(db, new AllowRetryAuthz(), dispatch);

            var resultBlocked = await retryService.RequestRetryAsync(new OperationRunId(runId));
            resultBlocked.Should().NotBeNull();
            resultBlocked!.IsBlocked.Should().BeTrue("definition resolver returns null when disabled");
            resultBlocked.RunId.Should().BeNull();
            (await db.OperationRuns.CountAsync()).Should().Be(1, "no new run when blocked");
        }

        toggleableComposition.SetSnapshot(compositionEnabled);
        toggleableResolver.SetEnabled(true);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            var recovery = new RecoveryQueryService(new StubHealthFromRecovery(
                await new RuntimeHealthQueryService(
                    toggleableComposition, registry,
                    new StubScheduleManagement(Array.Empty<ScheduleDefinition>(), Array.Empty<ScheduleAssignment>()),
                    new StubScheduleRepository(Array.Empty<ScheduleAssignment>()),
                    new RunKindResolver(db), db)
                .GetSnapshotAsync("local", 48, 48)));

            var blockedEntryNow = await recovery.GetBlockedWorkRemediationForRunAsync(runId);
            blockedEntryNow.Should().NotBeNull();
            blockedEntryNow!.RetryEligibleNow.Should().BeTrue("module is now executable");

            var dispatch = new WorkflowStartDispatchService(
                toggleableResolver, new SqlWorkflowPersistence(db), db);
            var retryService = new WorkflowRetryService(db, new AllowRetryAuthz(), dispatch);

            var resultDispatched = await retryService.RequestRetryAsync(new OperationRunId(runId));
            resultDispatched.Should().NotBeNull();
            resultDispatched!.IsBlocked.Should().BeFalse();
            resultDispatched.RunId.Should().NotBeNull();
            resultDispatched.OriginalRunId.Value.Should().Be(runId);

            var retryRun = await db.OperationRuns.SingleOrDefaultAsync(r => r.RetryOfRunId == runId);
            retryRun.Should().NotBeNull("trusted path creates new run with RetryOfRunId");
            retryRun!.Status.Should().Be(WorkflowLifecycleStatusNames.Requested);

            var retryStartRequest = await db.WorkflowStartRequests.SingleOrDefaultAsync(r => r.RunId == retryRun.Id);
            retryStartRequest.Should().NotBeNull("trusted path persists WorkflowStartRequest");

            var events = await db.WorkflowLifecycleEvents
                .Where(e => e.RunId == runId || e.RunId == retryRun.Id)
                .Select(e => e.EventType)
                .ToListAsync();
            events.Should().Contain("workflow.retry_requested");
            events.Should().Contain("workflow.dispatched");
        }
    }

    private sealed class ToggleableCompositionService : IRuntimeModuleCompositionQueryService
    {
        private HostEffectiveModuleSnapshot _snap;

        public ToggleableCompositionService(HostEffectiveModuleSnapshot initial) => _snap = initial;

        public void SetSnapshot(HostEffectiveModuleSnapshot snap) => _snap = snap;

        public ValueTask<HostEffectiveModuleSnapshot> GetSnapshotAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(_snap);
    }

    private sealed class ToggleableDefinitionResolver : IWorkflowDefinitionResolver
    {
        private readonly WorkflowDefinition _definition;
        private bool _enabled;

        public ToggleableDefinitionResolver(WorkflowDefinition definition) => _definition = definition;

        public void SetEnabled(bool enabled) => _enabled = enabled;

        public ValueTask<WorkflowDefinition?> GetByIdAsync(WorkflowDefinitionId id, CancellationToken ct = default) =>
            ValueTask.FromResult<WorkflowDefinition?>(
                _enabled && string.Equals(id.Value, _definition.Id.Value, StringComparison.Ordinal) ? _definition : null);
    }

    private sealed class StubHealthFromRecovery : IRuntimeHealthQueryService
    {
        private readonly RuntimeHealthSnapshot _snapshot;

        public StubHealthFromRecovery(RuntimeHealthSnapshot snapshot) => _snapshot = snapshot;

        public ValueTask<RuntimeHealthSnapshot> GetSnapshotAsync(
            string? runtimePlaneId = "local",
            int blockedDeferredWindowHours = 24,
            int scheduleFailureWindowHours = 24,
            CancellationToken ct = default) =>
            ValueTask.FromResult(_snapshot);
    }

    private sealed class AllowRetryAuthz : IWorkflowRunRetryAuthorizationService
    {
        public ValueTask EnsureCanRetryRunAsync(WorkflowDefinitionId id, CancellationToken ct = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class StubModuleRegistry : IModuleRegistryService
    {
        private readonly IReadOnlyList<ModuleCatalogEntry> _catalog;
        private readonly IReadOnlyList<ModuleDescriptor> _enabled;

        public StubModuleRegistry(IReadOnlyList<ModuleCatalogEntry> catalog, IReadOnlyList<ModuleDescriptor> enabled)
        {
            _catalog = catalog;
            _enabled = enabled;
        }

        public ValueTask<IReadOnlyList<ModuleCatalogEntry>> ListModulesAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(_catalog);
        public ValueTask<ModuleCatalogEntry?> GetModuleAsync(string moduleId, CancellationToken ct = default) =>
            ValueTask.FromResult(_catalog.FirstOrDefault(x => x.Descriptor.ModuleId == moduleId));
        public ValueTask<IReadOnlyList<ModuleDescriptor>> GetEnabledDescriptorsAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(_enabled);
        public ValueTask<ModuleLifecycleTransitionResult> DiscoverModulesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<ModuleLifecycleTransitionResult> InstallModuleAsync(string moduleId, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<ModuleLifecycleTransitionResult> EnableModuleAsync(string moduleId, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<ModuleLifecycleTransitionResult> DisableModuleAsync(string moduleId, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<ModuleLifecycleTransitionResult> UninstallModuleAsync(string moduleId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StubScheduleManagement : IScheduleManagementService
    {
        private readonly IReadOnlyList<ScheduleAssignment> _assignments;

        public StubScheduleManagement(IReadOnlyList<ScheduleDefinition> definitions, IReadOnlyList<ScheduleAssignment> assignments) =>
            _assignments = assignments;

        public ValueTask<ScheduleDefinition?> GetDefinitionAsync(ScheduleId id, CancellationToken ct = default) =>
            ValueTask.FromResult<ScheduleDefinition?>(null);
        public ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<ScheduleDefinition>>([]);
        public ValueTask<ScheduleDefinition> CreateScheduleAsync(ScheduleDefinition d, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask UpdateScheduleAsync(ScheduleDefinition d, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask EnableAsync(ScheduleId id, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask DisableAsync(ScheduleId id, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<ScheduleAssignment> AssignToRuntimePlaneAsync(ScheduleId id, RuntimePlaneId planeId, string? policy, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<ScheduleAssignment>> GetAssignmentsAsync(RuntimePlaneId planeId, CancellationToken ct = default) =>
            ValueTask.FromResult(_assignments);
        public ValueTask<ScheduleWithExecutionState?> GetScheduleWithExecutionStateAsync(ScheduleId id, RuntimePlaneId planeId, CancellationToken ct = default) =>
            ValueTask.FromResult<ScheduleWithExecutionState?>(null);
    }

    private sealed class StubScheduleRepository : IScheduleRepository
    {
        private readonly IReadOnlyList<ScheduleAssignment> _assignments;

        public StubScheduleRepository(IReadOnlyList<ScheduleAssignment> assignments) => _assignments = assignments;

        public ValueTask<ScheduleDefinition?> GetDefinitionAsync(ScheduleId id, CancellationToken ct = default) =>
            ValueTask.FromResult<ScheduleDefinition?>(null);
        public ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<ScheduleDefinition>>([]);
        public ValueTask AddDefinitionAsync(ScheduleDefinition d, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask UpdateDefinitionAsync(ScheduleDefinition d, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<ScheduleAssignment>> GetAssignmentsAsync(RuntimePlaneId planeId, CancellationToken ct = default) =>
            ValueTask.FromResult(_assignments);
        public ValueTask<ScheduleAssignment?> GetAssignmentAsync(ScheduleAssignmentId id, CancellationToken ct = default) =>
            ValueTask.FromResult(_assignments.FirstOrDefault(a => a.Id.Value == id.Value));
        public ValueTask AddAssignmentAsync(ScheduleAssignment a, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask UpdateAssignmentAsync(ScheduleAssignment a, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask<bool> RunExistsAsync(string scheduleId, DateTimeOffset fireTimeUtc, CancellationToken ct = default) => ValueTask.FromResult(false);
        public ValueTask<bool> TryClaimFireAsync(string scheduleId, DateTimeOffset fireTimeUtc, string assignmentId, CancellationToken ct = default) => ValueTask.FromResult(false);
        public ValueTask CompleteRunAsync(string scheduleId, DateTimeOffset fireTimeUtc, string outcome, string? reason, string? runId, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask UpdateStateAsync(string assignmentId, ScheduleStateUpdate state, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask<ScheduleState?> GetStateAsync(string assignmentId, CancellationToken ct = default) => ValueTask.FromResult<ScheduleState?>(null);
    }
}
