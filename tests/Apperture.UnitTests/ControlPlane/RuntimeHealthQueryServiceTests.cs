using Apperture.ControlPlane.Security;
using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class RuntimeHealthQueryServiceTests
{
    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    [Fact]
    public async Task GetSnapshotAsync_WhenNoDegradation_ReturnsHealthy()
    {
        var db = NewDb();
        var composition = new HostEffectiveModuleSnapshot(
            DateTimeOffset.UtcNow,
            [],
            [],
            ExecutionSurfaceCatalog.ImmediatelyLifecycleGoverned,
            ExecutionSurfaceCatalog.RestartBoundSurfaceIds,
            ExecutionSurfaceCatalog.NotYetImplementedSurfaceIds);

        var services = BuildServices(db, composition, [], []);
        await using var scope = services.BuildServiceProvider().CreateAsyncScope();
        var health = scope.ServiceProvider.GetRequiredService<IRuntimeHealthQueryService>();

        var snapshot = await health.GetSnapshotAsync("local", 24, 24);

        snapshot.OverallStatus.Should().Be(RuntimeHealthStatus.Healthy);
        snapshot.DegradationReasons.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSnapshotAsync_WhenModuleDegraded_ReturnsDegraded()
    {
        var db = NewDb();
        var degradedEntry = new RuntimeCompositionModuleEntry(
            "m1", "InstalledDisabled", Installed: true, PersistedEnabled: true,
            EffectivelyExecutable: false, AllowsNewStarts: false, HasInFlightWork: false,
            ValidationIssueCodes: ["invalid_persisted_state"],
            ExecutionDegradedReason: "invalid_persisted_state");
        var composition = new HostEffectiveModuleSnapshot(
            DateTimeOffset.UtcNow,
            [degradedEntry],
            [],
            ExecutionSurfaceCatalog.ImmediatelyLifecycleGoverned,
            ExecutionSurfaceCatalog.RestartBoundSurfaceIds,
            []);

        var catalog = new List<ModuleCatalogEntry>
        {
            new(new ModuleDescriptor("m1", "M1", "1.0", null, null, null,
                [new WorkflowContributionDescriptor("w1", "W")]),
                ModuleLifecycleState.InstalledDisabled, true, true, false,
                [new ModuleLifecycleBlocker("invalid_persisted_state", "Invalid")])
        };
        var registry = new StubModuleRegistry(catalog, []);
        var compositionSvc = new StubCompositionService(composition);

        var services = BuildServicesWithStubs(db, compositionSvc, registry, new StubScheduleManagement([], []), []);
        await using var scope = services.BuildServiceProvider().CreateAsyncScope();
        var health = scope.ServiceProvider.GetRequiredService<IRuntimeHealthQueryService>();

        var snapshot = await health.GetSnapshotAsync("local", 24, 24);

        snapshot.OverallStatus.Should().Be(RuntimeHealthStatus.Degraded);
        snapshot.DegradationReasons.Should().NotBeEmpty();
        snapshot.ModuleDiagnostics.Should().ContainSingle(m => m.ModuleId == "m1" && m.ExecutionDegradedReason == "invalid_persisted_state");
    }

    [Fact]
    public async Task GetSnapshotAsync_ReturnsCoherentSnapshot()
    {
        var db = NewDb();
        var composition = new HostEffectiveModuleSnapshot(
            DateTimeOffset.UtcNow, [], [],
            ExecutionSurfaceCatalog.ImmediatelyLifecycleGoverned,
            ExecutionSurfaceCatalog.RestartBoundSurfaceIds, []);
        var catalog = new List<ModuleCatalogEntry>();
        var registry = new StubModuleRegistry(catalog, []);

        var services = BuildServicesWithStubs(db, new StubCompositionService(composition), registry, new StubScheduleManagement([], []), []);
        await using var scope = services.BuildServiceProvider().CreateAsyncScope();
        var health = scope.ServiceProvider.GetRequiredService<IRuntimeHealthQueryService>();

        var snapshot = await health.GetSnapshotAsync("local");

        snapshot.CapturedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        snapshot.RestartBoundSurfaces.Should().NotBeEmpty();
        snapshot.NotYetImplementedSurfaces.Should().NotBeNull();
    }

    private static IServiceCollection BuildServices(
        AppertureDbContext db,
        HostEffectiveModuleSnapshot composition,
        IReadOnlyList<ScheduleDefinition> definitions,
        IReadOnlyList<ScheduleAssignment> assignments)
    {
        var catalog = new List<ModuleCatalogEntry>();
        var registry = new StubModuleRegistry(catalog, []);
        var compositionSvc = new StubCompositionService(composition);
        var scheduleMgmt = new StubScheduleManagement(definitions, assignments);
        var assignList = assignments.ToList();
        return BuildServicesWithStubs(db, compositionSvc, registry, scheduleMgmt, assignList);
    }

    private static IServiceCollection BuildServicesWithStubs(
        AppertureDbContext db,
        IRuntimeModuleCompositionQueryService compositionSvc,
        IModuleRegistryService registry,
        IScheduleManagementService? scheduleMgmt,
        IReadOnlyList<ScheduleAssignment> assignments)
    {
        var services = new ServiceCollection();
        services.AddScoped<AppertureDbContext>(_ => db);
        services.AddScoped<IRuntimeModuleCompositionQueryService>(_ => compositionSvc);
        services.AddScoped(_ => registry);
        services.AddScoped<IScheduleManagementService>(_ => scheduleMgmt ?? new StubScheduleManagement(Array.Empty<ScheduleDefinition>(), assignments));
        services.AddScoped<IScheduleRepository>(_ => new StubScheduleRepository(assignments));
        services.AddSingleton<ISchedulerClaimOptions>(new DefaultSchedulerClaimOptions(5));
        services.AddScoped<IRunKindResolver, RunKindResolver>();
        services.AddScoped<IRuntimeHealthQueryService, RuntimeHealthQueryService>();
        return services;
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
        private readonly IReadOnlyList<ScheduleDefinition> _definitions;
        private readonly IReadOnlyList<ScheduleAssignment> _assignments;

        public StubScheduleManagement(IReadOnlyList<ScheduleDefinition> definitions, IReadOnlyList<ScheduleAssignment> assignments)
        {
            _definitions = definitions;
            _assignments = assignments;
        }

        public ValueTask<ScheduleDefinition?> GetDefinitionAsync(ScheduleId id, CancellationToken ct = default) =>
            ValueTask.FromResult(_definitions.FirstOrDefault(d => d.Id.Value == id.Value));
        public ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(_definitions);
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
        public ValueTask<(string ClaimedByHostId, DateTimeOffset StartedAtUtc, string? ResourceContextId)?> GetInProgressRunClaimAsync(string assignmentId, CancellationToken ct = default) => ValueTask.FromResult<(string, DateTimeOffset, string?)?>(null);
        public ValueTask CompleteRunAsync(string scheduleId, DateTimeOffset fireTimeUtc, string outcome, string? reason, string? runId, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask UpdateStateAsync(string assignmentId, ScheduleStateUpdate state, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask<ScheduleState?> GetStateAsync(string assignmentId, CancellationToken ct = default) => ValueTask.FromResult<ScheduleState?>(null);
        public ValueTask<ScheduleDefinition?> GetByIdForUpdateAsync(ScheduleId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<ScheduleDefinition?>(null);
        public ValueTask SetResourceContextIdAsync(ScheduleId id, ResourceContextId resourceContextId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
