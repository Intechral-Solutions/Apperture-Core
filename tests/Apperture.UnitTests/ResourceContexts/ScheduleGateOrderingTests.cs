using Apperture.ControlPlane.Security;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// CTX-3B follow-on to Plan 2 Task 5: <see cref="SqlScheduleManagementService.EnableAsync"/> and
/// <see cref="SqlScheduleManagementService.UpdateScheduleAsync"/>'s guarded (Disabled -&gt; Enabled)
/// transition must acquire the ownership gate BEFORE reading the owning ResourceContext for the
/// reactivation guard, so a concurrent Archive cannot interleave between the two. This mirrors
/// <c>ManagementServiceTestHarness.WithRecordingGate()</c>'s ordering assertion for
/// <c>ResourceContextManagementService.ArchiveAsync</c>
/// (<see cref="ResourceContextManagementServiceTests.Archive_reads_ownership_under_the_ownership_gate"/>),
/// reusing its <see cref="ManagementServiceTestHarness.GateOrderTracker"/> and
/// <see cref="ManagementServiceTestHarness.RecordingResourceOwnershipMutationGate"/> spies (both
/// internal, so visible within this test assembly) rather than duplicating them.
/// </summary>
/// <remarks>
/// Setup (creating the context and the schedule) deliberately runs through a SEPARATE
/// <see cref="SqlScheduleManagementService"/> instance wired with a plain, non-recording
/// <see cref="IResourceContextService"/> and the real (InMemory no-op) ownership gate. If setup ran
/// through the recording instance instead, standalone schedule creation's own
/// <c>ResolveForNewOwnershipAsync</c> call would record a "read" event before the method under test
/// ever runs, poisoning the ordering assertion. Both instances share the same
/// <see cref="AppertureDbContext"/>, so data created by the setup instance is visible to the instance
/// under test.
/// </remarks>
public sealed class ScheduleGateOrderingTests : IAsyncDisposable
{
    private readonly AppertureDbContext _db;
    private readonly IScheduleManagementService _setupSchedules;
    private readonly IResourceContextService _setupResourceContexts;
    private readonly ManagementServiceTestHarness.GateOrderTracker _tracker = new();
    private readonly IScheduleManagementService _schedulesUnderTest;

    public ScheduleGateOrderingTests()
    {
        _db = new AppertureDbContext(
            new DbContextOptionsBuilder<AppertureDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        // The migration seeds Shared before anything can own it; mirror that here.
        _db.ResourceContexts.Add(new ResourceContextEntity
        {
            Id = ResourceContextIds.SharedValue,
            Name = "Shared",
            Slug = "shared",
            Kind = ResourceContextKinds.Shared,
            State = ResourceContextStates.Active,
            IsReserved = true,
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            UpdatedAtUtc = DateTimeOffset.UnixEpoch
        });
        _db.SaveChanges();

        var integrationRepository = new SqlIntegrationRepository(_db);
        var scheduleRepository = new SqlScheduleRepository(_db);

        _setupResourceContexts = new ResourceContextService(new SqlResourceContextRepository(_db), TimeProvider.System);
        _setupSchedules = new SqlScheduleManagementService(
            scheduleRepository,
            new StubClaimOptions(),
            TimeProvider.System,
            _setupResourceContexts,
            integrationRepository,
            _db,
            new SqlResourceOwnershipMutationGate(_db),
            new NullPrincipalContext());

        var recordingResourceContextRepository = new RecordingResourceContextRepository(new SqlResourceContextRepository(_db), _tracker);
        var recordingResourceContexts = new ResourceContextService(recordingResourceContextRepository, TimeProvider.System);
        Gate = new ManagementServiceTestHarness.RecordingResourceOwnershipMutationGate(_tracker);

        _schedulesUnderTest = new SqlScheduleManagementService(
            scheduleRepository,
            new StubClaimOptions(),
            TimeProvider.System,
            recordingResourceContexts,
            integrationRepository,
            _db,
            Gate,
            new NullPrincipalContext());
    }

    private ManagementServiceTestHarness.RecordingResourceOwnershipMutationGate Gate { get; }

    [Fact]
    public async Task EnableAsync_AcquiresGateBeforeReactivationRead()
    {
        var context = await _setupResourceContexts.CreateApplicationContextAsync("CampusNexus");
        var schedule = await CreateStandaloneScheduleAsync(context.Id, enabled: false);

        await _schedulesUnderTest.EnableAsync(schedule.Id);

        Gate.AcquiredBeforeFirstOwnershipRead.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateScheduleAsync_GuardedTransition_AcquiresGateBeforeReactivationRead()
    {
        var context = await _setupResourceContexts.CreateApplicationContextAsync("CampusNexus");
        var schedule = await CreateStandaloneScheduleAsync(context.Id, enabled: false);

        await _schedulesUnderTest.UpdateScheduleAsync(schedule with { Enabled = true });

        Gate.AcquiredBeforeFirstOwnershipRead.Should().BeTrue();
    }

    private Task<ScheduleDefinition> CreateStandaloneScheduleAsync(ResourceContextId contextId, bool enabled)
    {
        var definition = new ScheduleDefinition(
            new ScheduleId($"s-{Guid.NewGuid():N}"),
            "Nightly",
            "0 0 * * *",
            "UTC",
            Enabled: enabled,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            ExecutionPolicyRef: null,
            Version: 1,
            IntegrationId: null) with
        { ResourceContextId = contextId };

        return _setupSchedules.CreateScheduleAsync(definition).AsTask();
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private sealed class StubClaimOptions : ISchedulerClaimOptions
    {
        public int ClaimLeaseMinutes => 5;
    }

    private sealed class NullPrincipalContext : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(null);
    }

    private sealed class NullAuditService : IAuditService
    {
        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>
    /// Decorates the real <see cref="SqlResourceContextRepository"/>, recording a "read" event on
    /// <see cref="GetByIdAsync"/> -- the only member this suite's call chain touches
    /// (<c>ResourceContextReactivationGuard.EnsureOwningContextIsActiveAsync</c> via
    /// <c>IResourceContextService.GetAsync</c>) -- before delegating. Every other member is a pure
    /// passthrough. Mirrors <c>ManagementServiceTestHarness.RecordingResourceContextRepository</c>,
    /// which is private to that class and therefore not reusable here.
    /// </summary>
    private sealed class RecordingResourceContextRepository : IResourceContextRepository
    {
        private readonly IResourceContextRepository _inner;
        private readonly ManagementServiceTestHarness.GateOrderTracker _tracker;

        public RecordingResourceContextRepository(IResourceContextRepository inner, ManagementServiceTestHarness.GateOrderTracker tracker)
        {
            _inner = inner;
            _tracker = tracker;
        }

        public ValueTask<ResourceContext?> GetByIdAsync(ResourceContextId id, CancellationToken cancellationToken = default)
        {
            _tracker.RecordOwnershipRead();
            return _inner.GetByIdAsync(id, cancellationToken);
        }

        public ValueTask<ResourceContext?> GetBySlugAsync(string normalizedSlug, CancellationToken cancellationToken = default) =>
            _inner.GetBySlugAsync(normalizedSlug, cancellationToken);

        public ValueTask<IReadOnlyList<ResourceContext>> ListAsync(bool includeArchived = true, CancellationToken cancellationToken = default) =>
            _inner.ListAsync(includeArchived, cancellationToken);

        public ValueTask AddAsync(ResourceContext resourceContext, CancellationToken cancellationToken = default) =>
            _inner.AddAsync(resourceContext, cancellationToken);

        public ValueTask UpdateAsync(ResourceContext resourceContext, CancellationToken cancellationToken = default) =>
            _inner.UpdateAsync(resourceContext, cancellationToken);

        public ValueTask<ResourceContextOwnershipSummary> GetOwnershipSummaryAsync(ResourceContextId id, CancellationToken cancellationToken = default) =>
            _inner.GetOwnershipSummaryAsync(id, cancellationToken);

        public ValueTask<IReadOnlyDictionary<string, ResourceContextOwnershipSummary>> GetOwnershipSummariesAsync(CancellationToken cancellationToken = default) =>
            _inner.GetOwnershipSummariesAsync(cancellationToken);
    }
}
