using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Moq;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// Wires a real <see cref="ResourceOwnershipMoveService"/> over real management services and
/// repositories (EF Core InMemory), following the shape of <see cref="ResourceContextOwnershipTestHarness"/>.
/// Authorization is always-allow by default (constructor defaults <c>grantedAction</c>/<c>grantedResourceType</c>
/// to <c>"any"</c>); pass <c>grantedAction: null</c> to exercise the denied path, or a specific
/// action/resource-type pair to exercise the "wrong permission granted" path (mirrors
/// <c>ManagementServiceTestHarness</c>).
/// </summary>
public sealed class ResourceOwnershipMoveTestHarness : IAsyncDisposable
{
    private readonly AppertureDbContext _db;
    private int _nextSequence;

    public ResourceOwnershipMoveTestHarness(string? grantedAction = "any", string? grantedResourceType = "any")
    {
        _db = new AppertureDbContext(
            new DbContextOptionsBuilder<AppertureDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options);

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
        ResourceContexts = new ResourceContextService(resourceContextRepository, TimeProvider.System);
        IntegrationRepository = new SqlIntegrationRepository(_db);
        ConnectionRepository = new SqlConnectionRepository(_db);
        BindingRepository = new SqlBindingRepository(_db);
        ScheduleRepository = new SqlScheduleRepository(_db);

        var authz = grantedAction is null
            ? (IAuthorizationEvaluationService)new DenyAllAuthorizationEvaluationService()
            : new PermissionMatchingAuthorizationEvaluationService(grantedAction, grantedResourceType);

        Integrations = new IntegrationManagementService(
            new FixedPrincipalContext(),
            new AlwaysAllowAuthorizationEvaluationService(),
            IntegrationRepository,
            ResourceContexts,
            _db);

        var connectorRegistry = new PermissiveConnectorDefinitionRegistry();
        Connections = new ConnectionManagementService(
            new FixedPrincipalContext(),
            new AlwaysAllowAuthorizationEvaluationService(),
            ConnectionRepository,
            connectorRegistry,
            new NoOpConnectionVerificationService(),
            new ConnectionSecretMaterialProvisioner(new Mock<ISecretStore>().Object),
            ResourceContexts);

        var ownershipGate = new SqlResourceOwnershipMutationGate(_db);

        Bindings = new BindingService(
            new FixedPrincipalContext(),
            new AlwaysAllowResourceAuthorizationService(),
            BindingRepository,
            ConnectionRepository,
            new BindingEligibilityService(ConnectionRepository, connectorRegistry),
            IntegrationRepository,
            _db,
            ownershipGate);

        Schedules = new SqlScheduleManagementService(
            ScheduleRepository,
            new StubClaimOptions(),
            TimeProvider.System,
            ResourceContexts,
            IntegrationRepository,
            _db,
            ownershipGate);

        Service = new ResourceOwnershipMoveService(
            new FixedPrincipalContext(),
            authz,
            IntegrationRepository,
            ConnectionRepository,
            ScheduleRepository,
            BindingRepository,
            ResourceContexts);
    }

    public IResourceOwnershipMoveService Service { get; }

    /// <summary>The harness's own <see cref="AppertureDbContext"/>, exposed so fixtures can seed state
    /// the management services deliberately refuse to write (pre-existing ownership drift, cross-context
    /// bindings) and so Plan 2 can pass the same instance into the service under test.</summary>
    public AppertureDbContext Db => _db;

    public IResourceContextService ResourceContexts { get; }
    public IIntegrationRepository IntegrationRepository { get; }
    public IConnectionRepository ConnectionRepository { get; }
    public IBindingRepository BindingRepository { get; }
    public IScheduleRepository ScheduleRepository { get; }
    public IIntegrationManagementService Integrations { get; }
    public IConnectionManagementService Connections { get; }
    public IBindingService Bindings { get; }
    public IScheduleManagementService Schedules { get; }

    /// <summary>Creates and returns a new Active Application ResourceContext.</summary>
    public async Task<ResourceContext> CreateApplicationAsync(string name, CancellationToken ct = default) =>
        await ResourceContexts.CreateApplicationContextAsync(name, cancellationToken: ct);

    public async Task<ResourceContext> ArchiveAsync(ResourceContextId id, CancellationToken ct = default) =>
        await ResourceContexts.ArchiveAsync(id, ct);

    public async Task<DomainIntegration> CreateIntegrationAsync(
        ResourceContextId contextId,
        IntegrationState state = IntegrationState.Active,
        CancellationToken ct = default)
    {
        var created = await Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest($"Integration {NextSequence()}", "Sample.Mod", ResourceContextId: contextId),
            ct);
        if (state != IntegrationState.Active)
            created = await Integrations.UpdateIntegrationAsync(created.Id, new UpdateIntegrationRequest(State: state), ct);
        return created;
    }

    public async Task<Connection> CreateConnectionAsync(
        ResourceContextId contextId,
        ConnectionState state = ConnectionState.Active,
        CancellationToken ct = default)
    {
        var created = await Connections.CreateConnectionAsync(
            new CreateConnectionRequest(
                new ConnectorDefinitionId($"test.connector.{NextSequence()}"),
                $"Connection {NextSequence()}",
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                ResourceContextId: contextId),
            ct);
        if (state == ConnectionState.Retired)
            await Connections.RetireConnectionAsync(created.Id, ct);
        return created;
    }

    /// <summary>
    /// Binds <paramref name="connectionId"/> into <paramref name="integrationId"/> at a fresh slot name,
    /// bypassing eligibility (test-only direct repository write) so cross-context fixtures can be built
    /// without fighting eligibility filtering.
    /// </summary>
    /// <param name="integrationId">
    /// Nullable: pass <c>null</c> to build the legacy/global unowned binding that
    /// <c>GetUnownedBySlotAsync</c> resolves, which is what the <c>unowned_binding_requires_shared</c>
    /// fixtures need. <see cref="Binding.IntegrationId"/> is genuinely <c>IntegrationId?</c> in the domain,
    /// so this parameter must be nullable too — never pass <c>null!</c> through a non-nullable parameter.
    /// </param>
    public async Task<Binding> AddDirectBindingAsync(
        IntegrationId? integrationId,
        string moduleId,
        ConnectionId? connectionId,
        CancellationToken ct = default)
    {
        var binding = new Binding(
            new BindingId($"b-{NextSequence()}"),
            integrationId,
            moduleId,
            $"slot-{NextSequence()}",
            "test.contract",
            connectionId,
            null,
            BindingState.Active);
        await BindingRepository.AddAsync(binding, ct);
        return binding;
    }

    public Task<ScheduleDefinition> CreateAttachedScheduleAsync(
        IntegrationId integrationId,
        ResourceContextId? explicitContextId = null,
        CancellationToken ct = default)
    {
        var definition = new ScheduleDefinition(
            new ScheduleId($"s-{NextSequence()}"),
            "Nightly",
            "0 0 * * *",
            "UTC",
            Enabled: true,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            ExecutionPolicyRef: null,
            Version: 1,
            integrationId) with
        { ResourceContextId = explicitContextId };
        return Schedules.CreateScheduleAsync(definition, ct).AsTask();
    }

    public Task<ScheduleDefinition> CreateStandaloneScheduleAsync(ResourceContextId contextId, CancellationToken ct = default)
    {
        var definition = new ScheduleDefinition(
            new ScheduleId($"s-{NextSequence()}"),
            "Standalone",
            "0 1 * * *",
            "UTC",
            Enabled: true,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            ExecutionPolicyRef: null,
            Version: 1,
            IntegrationId: null) with
        { ResourceContextId = contextId };
        return Schedules.CreateScheduleAsync(definition, ct).AsTask();
    }

    /// <summary>Directly rewrites a Schedule's persisted <c>ResourceContextId</c> without going through the management service, to seed pre-existing drift for <c>schedule_context_inconsistent</c> tests.</summary>
    public async Task SetScheduleResourceContextDirectlyAsync(ScheduleId id, ResourceContextId newContextId, CancellationToken ct = default)
    {
        var entity = await _db.ScheduleDefinitions.FindAsync([id.Value], ct)
            ?? throw new InvalidOperationException($"Schedule '{id.Value}' not found.");
        entity.ResourceContextId = newContextId.Value;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Test-only direct ownership rewrite for an Integration, standing in for CTX-3B's own Integration
    /// move until Plan 2 lands it for real. Used to build fixtures where a bound Integration is already
    /// in the Connection move's target context. Once Plan 2 exists, prefer the real
    /// <c>MoveIntegrationAsync</c> in new tests; this helper stays for Plan 1's preview-only fixtures.
    /// </summary>
    public async Task MoveIntegrationDirectlyForFixtureAsync(IntegrationId id, ResourceContextId newContextId, CancellationToken ct = default)
    {
        var entity = await _db.Integrations.FindAsync([id.Value], ct)
            ?? throw new InvalidOperationException($"Integration '{id.Value}' not found.");
        entity.ResourceContextId = newContextId.Value;
        await _db.SaveChangesAsync(ct);
    }

    private int NextSequence() => System.Threading.Interlocked.Increment(ref _nextSequence);

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

    private sealed class AlwaysAllowResourceAuthorizationService : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor, PermissionDescriptor permission, ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class DenyAllAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }

    /// <summary>Grants only when the requested permission's action and resource type exactly match what was configured.</summary>
    private sealed class PermissionMatchingAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        private readonly string _grantedAction;
        private readonly string? _grantedResourceType;

        public PermissionMatchingAuthorizationEvaluationService(string grantedAction, string? grantedResourceType)
        {
            _grantedAction = grantedAction;
            _grantedResourceType = grantedResourceType;
        }

        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            if (string.Equals(_grantedAction, "any", StringComparison.Ordinal))
                return ValueTask.FromResult(true);
            var matches = string.Equals(context.Permission.Action, _grantedAction, StringComparison.Ordinal)
                && string.Equals(context.Permission.Resource?.ResourceType, _grantedResourceType, StringComparison.Ordinal);
            return ValueTask.FromResult(matches);
        }
    }

    private sealed class NoOpConnectionVerificationService : IConnectionVerificationService
    {
        public ValueTask<ConnectionVerificationResult> VerifyAsync(Connection connection, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ConnectionVerificationResult(ConnectionVerificationStatus.NeverTested, DateTimeOffset.UnixEpoch, null));
    }

    private sealed class StubClaimOptions : ISchedulerClaimOptions
    {
        public int ClaimLeaseMinutes => 5;
    }

    private sealed class PermissiveConnectorDefinitionRegistry : IConnectorDefinitionRegistry
    {
        private readonly Dictionary<string, ConnectorDefinition> _definitions = new(StringComparer.Ordinal);
        public void Register(ConnectorDefinition definition) => _definitions[definition.Id.Value] = definition;
        public IReadOnlyList<ConnectorDefinition> GetAll(string? moduleId = null) => _definitions.Values.ToList();
        public ConnectorDefinition? Get(ConnectorDefinitionId id)
        {
            if (!_definitions.TryGetValue(id.Value, out var definition))
            {
                definition = new ConnectorDefinition(id, id.Value, null, new Dictionary<string, string>());
                _definitions[id.Value] = definition;
            }
            return definition;
        }
    }
}
