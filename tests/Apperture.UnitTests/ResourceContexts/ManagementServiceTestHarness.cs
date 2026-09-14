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

    private ManagementServiceTestHarness(string? grantedAction, string? grantedResourceType)
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

        var resourceContextRepository = new SqlResourceContextRepository(_db);
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

        // The actor is always authenticated; only the permission grant varies. This exercises the
        // "authenticated but not authorized" branch specifically (rather than short-circuiting on a
        // missing actor), and actually checks the (action, resourceType) pair the service requests
        // matches what the caller granted, rather than blindly allowing everything.
        Service = new ResourceContextManagementService(
            resourceContexts,
            resourceContextRepository,
            new FixedPrincipalContext(new FixedActor()),
            new PermissionMatchingAuthorizationEvaluationService(grantedAction, grantedResourceType),
            _auditService);
    }

    public IResourceContextManagementService Service { get; }

    public IReadOnlyList<AuditRecord> AuditRecords => _auditService.Records;

    /// <summary>Harness where the authenticated actor holds no permissions at all.</summary>
    public static ManagementServiceTestHarness WithNoPermissions() => new(grantedAction: null, grantedResourceType: null);

    /// <summary>
    /// Harness where the authenticated actor holds exactly the given (action, resourceType) permission.
    /// The management service under test only ever checks <c>resource_context.manage</c> on
    /// <c>ResourceContext</c>, so passing anything else here would not actually grant it access.
    /// </summary>
    public static ManagementServiceTestHarness WithPermission(string action, string resourceType) => new(action, resourceType);

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
}
