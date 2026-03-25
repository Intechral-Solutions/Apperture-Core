using Apperture.ControlPlane.Security;
using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
using Apperture.Core.Domain.Workflow;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// Integration tests proving blocked deferred work surfaces in runtime health diagnostics.
/// Requires SQL Server (e.g. Docker on port 1433). Epic 30 corrective pass.
/// </summary>
public sealed class RuntimeHealthBlockedDeferredSqlServerTests
{
    [Fact]
    public async Task GetSnapshotAsync_WhenBlockedDeferredWorkSeeded_SurfacesInDiagnostics()
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

        var runId = "bd-" + Guid.NewGuid().ToString("N")[..8];
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
                DefinitionId = "Sample.HelloWorld.HelloWorld",
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
                DefinitionId = "Sample.HelloWorld.HelloWorld",
                Status = WorkflowLifecycleStatusNames.Failed,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                FailureSummary = RunFailureCapture.DeferredWorkBlockedPrefix + "Workflow definition is not effectively active"
            });
            db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
            {
                RunId = runId,
                WorkflowInstanceId = instanceId,
                WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
                Status = WorkflowLifecycleStatusNames.Failed,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
            });
            await db.SaveChangesAsync();
        }

        var descriptor = new ModuleDescriptor("Sample.HelloWorld", "HW", "1.0", null, null, null,
            [new WorkflowContributionDescriptor("Sample.HelloWorld.HelloWorld", "HW")]);
        var catalog = new List<ModuleCatalogEntry>
        {
            new(descriptor, ModuleLifecycleState.InstalledEnabled, true, true, true, [])
        };
        var registry = new StubModuleRegistry(catalog, [descriptor]);
        var composition = new HostEffectiveModuleSnapshot(
            DateTimeOffset.UtcNow, [], ["Sample.HelloWorld"],
            ExecutionSurfaceCatalog.ImmediatelyLifecycleGoverned,
            ExecutionSurfaceCatalog.RestartBoundSurfaceIds, []);
        var compositionSvc = new StubCompositionService(composition);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            var runKinds = new RunKindResolver(db);
            var health = new RuntimeHealthQueryService(
                compositionSvc, registry, new StubScheduleManagement(Array.Empty<ScheduleDefinition>(), Array.Empty<ScheduleAssignment>()),
                new StubScheduleRepository(Array.Empty<ScheduleAssignment>()),
                new DefaultSchedulerClaimOptions(5), runKinds, db);

            var snapshot = await health.GetSnapshotAsync("local", blockedDeferredWindowHours: 48);

            snapshot.BlockedDeferredWork.Should().ContainSingle(b => b.RunId == runId && b.DefinitionId == "Sample.HelloWorld.HelloWorld");
            snapshot.OverallStatus.Should().Be(RuntimeHealthStatus.Degraded, "blocked deferred work should cause degradation");
        }
    }

    private sealed class StubCompositionService : IRuntimeModuleCompositionQueryService
    {
        private readonly HostEffectiveModuleSnapshot _snap;

        public StubCompositionService(HostEffectiveModuleSnapshot snap) => _snap = snap;

        public ValueTask<HostEffectiveModuleSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_snap);
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
        public ValueTask<ScheduleAssignment> AssignToRuntimePlaneAsync(ScheduleId id, RuntimePlaneId planeId, string? policy, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask EnableAsync(ScheduleId id, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask DisableAsync(ScheduleId id, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<ScheduleAssignment>> GetAssignmentsAsync(RuntimePlaneId planeId, CancellationToken ct = default) =>
            ValueTask.FromResult(_assignments);
        public ValueTask<ScheduleWithExecutionState?> GetScheduleWithExecutionStateAsync(ScheduleId id, RuntimePlaneId planeId, CancellationToken ct = default) =>
            ValueTask.FromResult<ScheduleWithExecutionState?>(null);
    }

    private sealed class StubScheduleRepository : IScheduleRepository
    {
        private readonly IReadOnlyList<ScheduleAssignment> _assignments;

        public StubScheduleRepository(IReadOnlyList<ScheduleAssignment> assignments) => _assignments = assignments;

        public ValueTask<ScheduleDefinition?> GetDefinitionAsync(ScheduleId id, CancellationToken ct = default) => ValueTask.FromResult<ScheduleDefinition?>(null);
        public ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(CancellationToken ct = default) => ValueTask.FromResult<IReadOnlyList<ScheduleDefinition>>([]);
        public ValueTask<IReadOnlyList<ScheduleDefinition>> GetByIntegrationIdAsync(IntegrationId integrationId, CancellationToken ct = default) =>
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
        public ValueTask<bool> TryClaimFireAsync(string scheduleId, DateTimeOffset fireTimeUtc, string assignmentId, string hostId, CancellationToken ct = default) => ValueTask.FromResult(false);
        public ValueTask<IReadOnlyList<ScheduleFireCandidate>> GetRecoverableStaleCandidatesAsync(RuntimePlaneId planeId, DateTimeOffset staleCutoffUtc, CancellationToken ct = default) => ValueTask.FromResult<IReadOnlyList<ScheduleFireCandidate>>([]);
        public ValueTask<bool> TryRecoverStaleClaimAsync(string scheduleId, DateTimeOffset fireTimeUtc, string assignmentId, string hostId, DateTimeOffset staleCutoffUtc, CancellationToken ct = default) => ValueTask.FromResult(false);
        public ValueTask<(string ClaimedByHostId, DateTimeOffset StartedAtUtc)?> GetInProgressRunClaimAsync(string assignmentId, CancellationToken ct = default) => ValueTask.FromResult<(string, DateTimeOffset)?>(null);
        public ValueTask CompleteRunAsync(string scheduleId, DateTimeOffset fireTimeUtc, string outcome, string? reason, string? runId, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask UpdateStateAsync(string assignmentId, ScheduleStateUpdate state, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask<ScheduleState?> GetStateAsync(string assignmentId, CancellationToken ct = default) => ValueTask.FromResult<ScheduleState?>(null);
    }
}
