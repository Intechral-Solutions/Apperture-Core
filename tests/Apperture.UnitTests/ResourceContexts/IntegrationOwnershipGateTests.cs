using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// CTX-3B follow-on to Plan 2 Task 5: <see cref="IntegrationManagementService"/> is a NEW
/// <c>IResourceOwnershipMutationGate</c> consumer, closing two races a follow-on audit found --
/// <see cref="IntegrationManagementService.CreateIntegrationAsync"/> (a new Active Integration entering
/// a context is archive-blocking) and <see cref="IntegrationManagementService.UpdateIntegrationAsync"/>'s
/// widened Retired -&gt; {Active, Inactive} branch (both transitions flip an Integration from
/// non-blocking to archive-blocking, per <see cref="ResourceContextOwnershipSummary.BlocksArchive"/>).
/// Both must acquire the gate before their first ownership-sensitive read, so a concurrent
/// <c>ResourceContextManagementService.ArchiveAsync</c> cannot interleave and read an empty ownership
/// summary.
/// </summary>
public sealed class IntegrationOwnershipGateTests
{
    /// <summary>
    /// Mirrors <c>ConnectionOwnershipGateTests.CreateConnection_reads_ownership_under_the_ownership_gate</c>:
    /// setup here (creating the Application context) does not itself touch the gate or the recording
    /// repository, so a single shared <see cref="ManagementServiceTestHarness"/> instance is safe --
    /// unlike the Update test below, no separate setup/under-test split is needed.
    /// </summary>
    [Fact]
    public async Task CreateIntegration_reads_ownership_under_the_ownership_gate()
    {
        await using var harness = ManagementServiceTestHarness.WithRecordingGate();
        var app = await harness.Service.CreateAsync("CampusNexus", slug: null, description: null, CancellationToken.None);

        await harness.Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest("Integration A", "Sample.Mod", ResourceContextId: app.Id),
            CancellationToken.None);

        harness.Gate!.AcquiredBeforeFirstOwnershipRead.Should().BeTrue();
    }
}

/// <summary>
/// Covers <see cref="IntegrationManagementService.UpdateIntegrationAsync"/>'s widened Retired -&gt;
/// {Active, Inactive} branch. Follows <c>ScheduleGateOrderingTests</c>'s dual-instance shape exactly:
/// setup (creating the context and the Retired Integration) runs through a SEPARATE
/// <see cref="IntegrationManagementService"/> instance wired with a plain, non-recording
/// <see cref="IResourceContextService"/> and the real (InMemory no-op) ownership gate. If setup ran
/// through the recording instance instead, <c>CreateIntegrationAsync</c>'s own gate acquisition and
/// ownership read (also gated as of this same follow-on) would record events before the method under
/// test ever runs, poisoning the ordering assertion -- <see cref="ManagementServiceTestHarness.GateOrderTracker.AcquiredBeforeFirstOwnershipRead"/>
/// only ever looks at the FIRST recorded occurrence of each event. Both instances share the same
/// <see cref="AppertureDbContext"/>, so data created by the setup instance is visible to the instance
/// under test.
/// </summary>
public sealed class IntegrationUpdateGateOrderingTests : IAsyncDisposable
{
    private readonly AppertureDbContext _db;
    private readonly IIntegrationManagementService _setupIntegrations;
    private readonly IResourceContextService _setupResourceContexts;
    private readonly ManagementServiceTestHarness.GateOrderTracker _tracker = new();
    private readonly IIntegrationManagementService _integrationsUnderTest;

    public IntegrationUpdateGateOrderingTests()
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

        _setupResourceContexts = new ResourceContextService(new SqlResourceContextRepository(_db), TimeProvider.System);
        _setupIntegrations = new IntegrationManagementService(
            new FixedPrincipalContext(),
            new AlwaysAllowAuthorizationEvaluationService(),
            integrationRepository,
            _setupResourceContexts,
            _db,
            new SqlResourceOwnershipMutationGate(_db));

        var recordingResourceContextRepository = new RecordingResourceContextRepository(new SqlResourceContextRepository(_db), _tracker);
        var recordingResourceContexts = new ResourceContextService(recordingResourceContextRepository, TimeProvider.System);
        Gate = new ManagementServiceTestHarness.RecordingResourceOwnershipMutationGate(_tracker);

        _integrationsUnderTest = new IntegrationManagementService(
            new FixedPrincipalContext(),
            new AlwaysAllowAuthorizationEvaluationService(),
            integrationRepository,
            recordingResourceContexts,
            _db,
            Gate);
    }

    private ManagementServiceTestHarness.RecordingResourceOwnershipMutationGate Gate { get; }

    [Fact]
    public async Task UpdateIntegrationAsync_RetiredToActive_AcquiresGateBeforeReactivationRead()
    {
        var context = await _setupResourceContexts.CreateApplicationContextAsync("CampusNexus");
        var integration = await CreateRetiredIntegrationAsync(context.Id);

        await _integrationsUnderTest.UpdateIntegrationAsync(integration.Id, new UpdateIntegrationRequest(State: IntegrationState.Active));

        Gate.AcquiredBeforeFirstOwnershipRead.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateIntegrationAsync_RetiredToInactive_AcquiresGateBeforeReactivationRead()
    {
        var context = await _setupResourceContexts.CreateApplicationContextAsync("CampusNexus");
        var integration = await CreateRetiredIntegrationAsync(context.Id);

        await _integrationsUnderTest.UpdateIntegrationAsync(integration.Id, new UpdateIntegrationRequest(State: IntegrationState.Inactive));

        Gate.AcquiredBeforeFirstOwnershipRead.Should().BeTrue();
    }

    private async Task<Apperture.Core.Domain.Integration.Integration> CreateRetiredIntegrationAsync(ResourceContextId contextId)
    {
        var created = await _setupIntegrations.CreateIntegrationAsync(
            new CreateIntegrationRequest("Integration A", "Sample.Mod", ResourceContextId: contextId));
        return await _setupIntegrations.RetireIntegrationAsync(created.Id);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private sealed class FixedPrincipalContext : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new FixedActor());

        private sealed class FixedActor : IActor
        {
            public ActorId Id => new("test-actor");
            public ActorKind Kind => ActorKind.Human;
            public string? DisplayName => "Test Actor";
        }
    }

    private sealed class AlwaysAllowAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    /// <summary>
    /// Decorates the real <see cref="SqlResourceContextRepository"/>, recording a "read" event on
    /// <see cref="GetByIdAsync"/> -- the only member this suite's call chain touches
    /// (<c>ResourceContextReactivationGuard.EnsureOwningContextIsActiveAsync</c> via
    /// <c>IResourceContextService.GetAsync</c>) -- before delegating. Every other member is a pure
    /// passthrough. Mirrors <c>ManagementServiceTestHarness.RecordingResourceContextRepository</c> and
    /// <c>ScheduleGateOrderingTests.RecordingResourceContextRepository</c>, both private to their own
    /// classes and therefore not reusable here.
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
