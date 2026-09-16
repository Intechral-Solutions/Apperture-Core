using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// Wires a real <see cref="ResourceContextManagementService"/> over the real
/// <see cref="ResourceContextService"/>/<see cref="SqlResourceContextRepository"/> (EF Core InMemory),
/// following the exact shape of <see cref="ResourceContextOwnershipTestHarness"/> (Plan 1), but
/// additionally wiring a fake/stub <see cref="IAuthorizationEvaluationService"/> (grant/deny by
/// permission), a fake <see cref="IPrincipalContext"/> (one fixed actor), and a fake
/// <see cref="IAuditService"/> whose <c>AppendAsync</c> records into an in-memory list exposed as
/// <see cref="AuditRecords"/>.
/// </summary>
public sealed class ManagementServiceTestHarness : IAsyncDisposable
{
    private readonly AppertureDbContext _db;
    private readonly IIntegrationManagementService _integrations;
    private readonly RecordingAuditService _auditService;
    private int _nextSequence;

    private ManagementServiceTestHarness(string? grantedAction, string? grantedResourceType, bool recordingGate = false)
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

        var gateOrderTracker = new GateOrderTracker();
        IResourceContextRepository resourceContextRepository = recordingGate
            ? new RecordingResourceContextRepository(new SqlResourceContextRepository(_db), gateOrderTracker)
            : new SqlResourceContextRepository(_db);
        var resourceContexts = new ResourceContextService(resourceContextRepository, TimeProvider.System);
        var integrationRepository = new SqlIntegrationRepository(_db);

        // Integration creation (used only by SeedLiveIntegration to make ArchiveAsync's "still owns a
        // live resource" rule fire) is always permitted -- these tests are about the management
        // service's own authorization/audit wrapper, not Integration authorization.
        _integrations = new IntegrationManagementService(
            new FixedPrincipalContext(new FixedActor()),
            new AlwaysAllowAuthorizationEvaluationService(),
            integrationRepository,
            resourceContexts,
            _db);

        _auditService = new RecordingAuditService();

        // Real SqlResourceOwnershipMutationGate is a documented no-op on EF Core InMemory (no lock
        // manager, no transactions) -- the same gate BindingService/SqlScheduleManagementService harnesses
        // use. When recordingGate is set, a spy replaces it to pin call ORDER for a pure ordering
        // assertion; the real interleaving is proven against SQL Server elsewhere (Task 8).
        IResourceOwnershipMutationGate ownershipGate = recordingGate
            ? new RecordingResourceOwnershipMutationGate(gateOrderTracker)
            : new SqlResourceOwnershipMutationGate(_db);
        Gate = ownershipGate as RecordingResourceOwnershipMutationGate;

        // The actor is always authenticated; only the permission grant varies. This exercises the
        // "authenticated but not authorized" branch specifically (rather than short-circuiting on a
        // missing actor), and actually checks the (action, resourceType) pair the service requests
        // matches what the caller granted, rather than blindly allowing everything.
        Service = new ResourceContextManagementService(
            resourceContexts,
            resourceContextRepository,
            new FixedPrincipalContext(new FixedActor()),
            new PermissionMatchingAuthorizationEvaluationService(grantedAction, grantedResourceType),
            _auditService,
            _db,
            ownershipGate);
    }

    public IResourceContextManagementService Service { get; }

    public IReadOnlyList<AuditRecord> AuditRecords => _auditService.Records;

    /// <summary>
    /// The recording ownership-gate spy, non-null only when constructed via <see cref="WithRecordingGate"/>.
    /// </summary>
    public RecordingResourceOwnershipMutationGate? Gate { get; }

    /// <summary>Harness where the authenticated actor holds no permissions at all.</summary>
    public static ManagementServiceTestHarness WithNoPermissions() => new(grantedAction: null, grantedResourceType: null);

    /// <summary>
    /// Harness where the authenticated actor holds exactly the given (action, resourceType) permission.
    /// The management service under test only ever checks <c>resource_context.manage</c> on
    /// <c>ResourceContext</c>, so passing anything else here would not actually grant it access.
    /// </summary>
    public static ManagementServiceTestHarness WithPermission(string action, string resourceType) => new(action, resourceType);

    /// <summary>
    /// Harness granted <c>resource_context.manage</c> and wired with a recording ownership-gate spy
    /// (<see cref="Gate"/>) so a test can pin that <see cref="IResourceContextManagementService.ArchiveAsync"/>
    /// acquires the gate before its first ownership-sensitive read.
    /// </summary>
    public static ManagementServiceTestHarness WithRecordingGate() =>
        new(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext, recordingGate: true);

    /// <summary>Creates and activates an Integration owned by <paramref name="contextId"/>, so a subsequent ArchiveAsync is blocked by the "still owns a live resource" rule.</summary>
    public async Task SeedLiveIntegration(ResourceContextId contextId, CancellationToken cancellationToken = default)
    {
        await _integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest($"Integration {NextSequence()}", "Sample.Mod", ResourceContextId: contextId),
            cancellationToken);
    }

    private int NextSequence() => System.Threading.Interlocked.Increment(ref _nextSequence);

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private sealed class FixedActor : IActor
    {
        public ActorId Id => new("test-actor");
        public ActorKind Kind => ActorKind.Human;
        public string? DisplayName => "Test Actor";
    }

    private sealed class FixedPrincipalContext : IPrincipalContext
    {
        private readonly IActor? _actor;
        public FixedPrincipalContext(IActor? actor) => _actor = actor;

        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_actor);
    }

    private sealed class AlwaysAllowAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    /// <summary>Grants only when the requested permission's action and resource type exactly match what was configured; null grants nothing.</summary>
    private sealed class PermissionMatchingAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        private readonly string? _grantedAction;
        private readonly string? _grantedResourceType;

        public PermissionMatchingAuthorizationEvaluationService(string? grantedAction, string? grantedResourceType)
        {
            _grantedAction = grantedAction;
            _grantedResourceType = grantedResourceType;
        }

        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            var matches = _grantedAction is not null
                && string.Equals(context.Permission.Action, _grantedAction, StringComparison.Ordinal)
                && string.Equals(context.Permission.Resource?.ResourceType, _grantedResourceType, StringComparison.Ordinal);
            return ValueTask.FromResult(matches);
        }
    }

    /// <summary>Records every appended <see cref="AuditRecord"/> into an in-memory list. Query/count/get-by-id are unused by this suite.</summary>
    private sealed class RecordingAuditService : IAuditService
    {
        private readonly List<AuditRecord> _records = new();
        public IReadOnlyList<AuditRecord> Records => _records;

        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            _records.Add(record);
            return ValueTask.CompletedTask;
        }

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by ManagementServiceTestHarness.");

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by ManagementServiceTestHarness.");

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by ManagementServiceTestHarness.");
    }

    /// <summary>
    /// Shared ordering log for <see cref="RecordingResourceOwnershipMutationGate"/> and
    /// <see cref="RecordingResourceContextRepository"/>: single-threaded (xUnit runs this harness's tests
    /// sequentially within an instance), so a plain append-ordered list is sufficient to answer "did the
    /// gate acquire happen before the first ownership-sensitive repository read."
    /// </summary>
    internal sealed class GateOrderTracker
    {
        private readonly List<string> _events = new();

        public void RecordGateAcquired() => _events.Add("gate");

        public void RecordOwnershipRead() => _events.Add("read");

        public bool AcquiredBeforeFirstOwnershipRead
        {
            get
            {
                var gateIndex = _events.IndexOf("gate");
                var readIndex = _events.IndexOf("read");
                return gateIndex >= 0 && (readIndex < 0 || gateIndex < readIndex);
            }
        }
    }

    /// <summary>
    /// Spy double for <see cref="IResourceOwnershipMutationGate"/>: this is a pure ordering-assertion unit
    /// test, not a concurrency test, so recording that <c>AcquireAsync</c> ran (and when, relative to the
    /// first ownership read) is correct here -- unlike Task 3/4's harnesses, which use the real
    /// <see cref="SqlResourceOwnershipMutationGate"/> because those tests exercise business logic, not gate
    /// ordering. The real interleaving is proven only against SQL Server (Task 8).
    /// </summary>
    public sealed class RecordingResourceOwnershipMutationGate : IResourceOwnershipMutationGate
    {
        private readonly GateOrderTracker _tracker;

        internal RecordingResourceOwnershipMutationGate(GateOrderTracker tracker) => _tracker = tracker;

        public ValueTask AcquireAsync(CancellationToken cancellationToken = default)
        {
            _tracker.RecordGateAcquired();
            return ValueTask.CompletedTask;
        }

        /// <summary>True once <see cref="AcquireAsync"/> has run, and only if it ran strictly before the first ownership-sensitive repository read recorded by <see cref="RecordingResourceContextRepository"/>.</summary>
        public bool AcquiredBeforeFirstOwnershipRead => _tracker.AcquiredBeforeFirstOwnershipRead;
    }

    /// <summary>
    /// Decorates the real <see cref="SqlResourceContextRepository"/>, recording an ownership-read event on
    /// the two members <see cref="ResourceContextManagementService.ArchiveAsync"/>'s call chain actually
    /// touches (<see cref="GetByIdAsync"/> via <c>IResourceContextService.GetAsync</c>/<c>ArchiveAsync</c>'s
    /// internal <c>GetRequiredAsync</c>, and <see cref="GetOwnershipSummaryAsync"/> via the domain
    /// "nothing live" archive rule) before delegating. Every other member is a pure passthrough.
    /// </summary>
    private sealed class RecordingResourceContextRepository : IResourceContextRepository
    {
        private readonly IResourceContextRepository _inner;
        private readonly GateOrderTracker _tracker;

        public RecordingResourceContextRepository(IResourceContextRepository inner, GateOrderTracker tracker)
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

        public ValueTask<ResourceContextOwnershipSummary> GetOwnershipSummaryAsync(ResourceContextId id, CancellationToken cancellationToken = default)
        {
            _tracker.RecordOwnershipRead();
            return _inner.GetOwnershipSummaryAsync(id, cancellationToken);
        }

        public ValueTask<IReadOnlyDictionary<string, ResourceContextOwnershipSummary>> GetOwnershipSummariesAsync(CancellationToken cancellationToken = default) =>
            _inner.GetOwnershipSummariesAsync(cancellationToken);
    }
}
