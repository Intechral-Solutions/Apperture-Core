using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.ModuleSdk.Abstractions;
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

    public ResourceOwnershipMoveTestHarness(string? grantedAction = "any", string? grantedResourceType = "any", IAuditService? audit = null)
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

        var ownershipGate = new SqlResourceOwnershipMutationGate(_db);

        Integrations = new IntegrationManagementService(
            new FixedPrincipalContext(),
            new AlwaysAllowAuthorizationEvaluationService(),
            IntegrationRepository,
            ResourceContexts,
            _db,
            ownershipGate,
            auditedUnitOfWork: new Apperture.UnitTests.Audit.RecordingAuditedUnitOfWork());

        var connectorRegistry = new PermissiveConnectorDefinitionRegistry();
        Connections = new ConnectionManagementService(
            new FixedPrincipalContext(),
            new AlwaysAllowAuthorizationEvaluationService(),
            ConnectionRepository,
            connectorRegistry,
            new NoOpConnectionVerificationService(),
            new ConnectionSecretMaterialProvisioner(new Mock<ISecretStore>().Object),
            ResourceContexts,
            _db,
            ownershipGate,
            Mock.Of<IAuditService>());

        Bindings = new BindingService(
            new FixedPrincipalContext(),
            new AlwaysAllowResourceAuthorizationService(),
            BindingRepository,
            ConnectionRepository,
            new BindingEligibilityService(ConnectionRepository, connectorRegistry),
            IntegrationRepository,
            _db,
            ownershipGate,
            new Apperture.UnitTests.Audit.RecordingAuditedUnitOfWork());

        Schedules = new SqlScheduleManagementService(
            ScheduleRepository,
            new StubClaimOptions(),
            TimeProvider.System,
            ResourceContexts,
            IntegrationRepository,
            _db,
            ownershipGate,
            new FixedPrincipalContext());

        Service = new ResourceOwnershipMoveService(
            new FixedPrincipalContext(),
            authz,
            IntegrationRepository,
            ConnectionRepository,
            ScheduleRepository,
            BindingRepository,
            ResourceContexts,
            _db,
            ownershipGate,
            audit ?? Audit);
    }

    public IResourceOwnershipMoveService Service { get; }

    /// <summary>
    /// The recording double most tests inspect via <c>.Records</c>. Always constructed, even when a test
    /// substitutes a different <see cref="IAuditService"/> (e.g. <see cref="ThrowingAuditService"/>) into
    /// the real <see cref="ResourceOwnershipMoveService"/> via the <c>audit:</c> constructor parameter --
    /// in that case this property simply stays empty, since it was never wired into the service under test.
    /// </summary>
    public RecordingAuditService Audit { get; } = new();

    public sealed class RecordingAuditService : IAuditService
    {
        private readonly List<AuditRecord> _records = new();
        public IReadOnlyList<AuditRecord> Records => _records;
        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            _records.Add(record);
            return ValueTask.CompletedTask;
        }
        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by ResourceOwnershipMoveTestHarness.");
        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by ResourceOwnershipMoveTestHarness.");
        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by ResourceOwnershipMoveTestHarness.");
    }

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

    /// <summary>
    /// Seeds one row per CTX-2 historical-provenance family (Task 7), all snapshotted to
    /// <paramref name="contextId"/> and (where the family carries the column) <paramref name="integrationId"/>.
    /// Every row uses a fresh id so repeated calls within one test accumulate rather than collide. No
    /// family here declares a foreign key to Integrations/ResourceContexts/Schedules (see the "CTX-2:
    /// ... No FK ..." comments on each entity in <c>AppertureDbContext.OnModelCreating</c>), so these can
    /// be seeded directly without satisfying any other row's existence.
    /// </summary>
    public async Task SeedHistoricalProvenanceAsync(IntegrationId integrationId, ResourceContextId contextId, CancellationToken ct = default)
    {
        var seq = NextSequence();
        var integrationValue = integrationId.Value;
        var contextValue = contextId.Value;
        var now = DateTimeOffset.UtcNow;

        var runId = $"hist-run-{seq}";
        var workflowInstanceId = $"hist-wi-{seq}";
        var operationInstanceId = $"hist-oi-{seq}";
        var operationStartRunId = $"hist-oprun-{seq}";
        var notificationDefinitionId = $"hist-nd-{seq}";

        _db.OperationRuns.Add(new OperationRunEntity
        {
            Id = runId,
            WorkflowInstanceId = workflowInstanceId,
            DefinitionId = "hist.def",
            Status = "succeeded",
            CreatedAt = now,
            IntegrationId = integrationValue,
            ResourceContextId = contextValue
        });

        _db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = workflowInstanceId,
            DefinitionId = "hist.def",
            Status = "succeeded",
            CreatedAt = now,
            UpdatedAt = now,
            IntegrationId = integrationValue,
            ResourceContextId = contextValue
        });

        _db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = runId,
            WorkflowInstanceId = workflowInstanceId,
            WorkflowDefinitionId = "hist.def",
            IntegrationId = integrationValue,
            ResourceContextId = contextValue,
            CreatedAt = now
        });

        _db.OperationInstances.Add(new OperationInstanceEntity
        {
            Id = operationInstanceId,
            DefinitionId = "hist.def",
            Status = "succeeded",
            CreatedAt = now,
            UpdatedAt = now,
            ResourceContextId = contextValue
        });

        _db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = operationStartRunId,
            OperationInstanceId = operationInstanceId,
            OperationDefinitionId = "hist.def",
            IntegrationId = integrationValue,
            ResourceContextId = contextValue,
            CreatedAt = now
        });

        _db.OperationGroups.Add(new OperationGroupEntity
        {
            Id = $"hist-og-{seq}",
            RunId = runId,
            IntegrationId = integrationValue,
            ModuleId = "hist.mod",
            OperationKind = "sync",
            Status = "succeeded",
            ResourceContextId = contextValue,
            StartedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        _db.PlatformEvents.Add(new PlatformEventEntity
        {
            Id = $"hist-pe-{seq}",
            Type = "hist.event",
            Source = "test",
            DataJson = "{}",
            OccurredAtUtc = now,
            RecordedAtUtc = now,
            Severity = "info",
            IntegrationId = integrationValue,
            ResourceContextId = contextValue
        });

        _db.NotificationInstances.Add(new NotificationInstanceEntity
        {
            Id = $"hist-ni-{seq}",
            NotificationDefinitionId = notificationDefinitionId,
            NotificationKind = "hist.kind",
            Status = "sent",
            IntegrationId = integrationValue,
            ResourceContextId = contextValue,
            InstanceDedupeKey = $"hist-dedupe-{seq}",
            RenderedSubject = "subject",
            RenderedSummary = "summary",
            RenderedBodyText = "body",
            TemplateVersionHash = "hash",
            RenderContextJson = "{}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        _db.AttentionIncidents.Add(new AttentionIncidentEntity
        {
            Id = $"hist-ai-{seq}",
            IntegrationId = integrationValue,
            ResourceContextId = contextValue,
            ConditionKey = "hist.condition",
            Status = "Active",
            StartedAtUtc = now,
            LastObservedAtUtc = now
        });

        _db.ScheduleRuns.Add(new ScheduleRunEntity
        {
            ScheduleId = $"hist-sched-{seq}",
            AssignmentId = $"hist-assign-{seq}",
            FireTimeUtc = now,
            StartedAtUtc = now,
            Outcome = "succeeded",
            ResourceContextId = contextValue
        });

        _db.AuditLogs.Add(new AuditLogEntity
        {
            At = now,
            Actor = "test-actor",
            Action = "hist.action",
            ResourceContextId = contextValue
        });

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Ordered, comparable projection of every CTX-2 historical-provenance family (Task 7) so a single
    /// <c>BeEquivalentTo</c> call can assert none of them were rewritten by a move. Adding a newly
    /// discovered family only needs one more <c>rows.AddRange</c> line here.
    /// </summary>
    public async Task<IReadOnlyList<HistoricalProvenanceRow>> CaptureHistoricalProvenanceAsync(CancellationToken ct = default)
    {
        var rows = new List<HistoricalProvenanceRow>();

        rows.AddRange((await _db.OperationRuns.AsNoTracking().ToListAsync(ct))
            .Select(x => new HistoricalProvenanceRow(nameof(OperationRunEntity), x.Id, x.IntegrationId, x.ResourceContextId)));
        rows.AddRange((await _db.WorkflowInstances.AsNoTracking().ToListAsync(ct))
            .Select(x => new HistoricalProvenanceRow(nameof(WorkflowInstanceEntity), x.Id, x.IntegrationId, x.ResourceContextId)));
        rows.AddRange((await _db.WorkflowStartRequests.AsNoTracking().ToListAsync(ct))
            .Select(x => new HistoricalProvenanceRow(nameof(WorkflowStartRequestEntity), x.RunId, x.IntegrationId, x.ResourceContextId)));
        rows.AddRange((await _db.OperationStartRequests.AsNoTracking().ToListAsync(ct))
            .Select(x => new HistoricalProvenanceRow(nameof(OperationStartRequestEntity), x.RunId, x.IntegrationId, x.ResourceContextId)));
        rows.AddRange((await _db.OperationInstances.AsNoTracking().ToListAsync(ct))
            .Select(x => new HistoricalProvenanceRow(nameof(OperationInstanceEntity), x.Id, null, x.ResourceContextId)));
        rows.AddRange((await _db.OperationGroups.AsNoTracking().ToListAsync(ct))
            .Select(x => new HistoricalProvenanceRow(nameof(OperationGroupEntity), x.Id, x.IntegrationId, x.ResourceContextId)));
        rows.AddRange((await _db.PlatformEvents.AsNoTracking().ToListAsync(ct))
            .Select(x => new HistoricalProvenanceRow(nameof(PlatformEventEntity), x.Id, x.IntegrationId, x.ResourceContextId)));
        rows.AddRange((await _db.NotificationInstances.AsNoTracking().ToListAsync(ct))
            .Select(x => new HistoricalProvenanceRow(nameof(NotificationInstanceEntity), x.Id, x.IntegrationId, x.ResourceContextId)));
        rows.AddRange((await _db.AttentionIncidents.AsNoTracking().ToListAsync(ct))
            .Select(x => new HistoricalProvenanceRow(nameof(AttentionIncidentEntity), x.Id, x.IntegrationId, x.ResourceContextId)));
        rows.AddRange((await _db.ScheduleRuns.AsNoTracking().ToListAsync(ct))
            .Select(x => new HistoricalProvenanceRow(nameof(ScheduleRunEntity), x.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), null, x.ResourceContextId)));
        rows.AddRange((await _db.AuditLogs.AsNoTracking().ToListAsync(ct))
            .Select(x => new HistoricalProvenanceRow(nameof(AuditLogEntity), x.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), null, x.ResourceContextId)));

        return rows
            .OrderBy(r => r.TableName, StringComparer.Ordinal)
            .ThenBy(r => r.RowId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Builds a real <see cref="IntegrationDispatchReadinessEvaluator"/> over this harness's own
    /// repositories, wired with the same permissive stand-in collaborators
    /// <c>BindingCrossContextReadinessTests</c> uses for its manually-constructed evaluator (empty slot
    /// registry, always-advisory verification policy, an empty SDK-6C configuration registry so every
    /// integration falls through to the legacy slot-binding branch, and inert evidence-builder
    /// dependencies that are consequently never invoked). One factory here means CTX-3B's move tests and
    /// the binding_cross_context backstop tests share one evaluator wiring instead of duplicating the
    /// ten-argument constructor.
    /// </summary>
    public IntegrationDispatchReadinessEvaluator BuildReadinessEvaluator()
    {
        var slots = new Mock<IModuleSlotRegistry>();
        slots.Setup(x => x.GetSlotsForModule(It.IsAny<string>())).Returns(Array.Empty<ICapabilitySlot>());

        var settingsResolver = new Mock<IIntegrationSettingsResolver>();
        settingsResolver.Setup(x => x.ListResolvedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlatformSettingResolvedValue>());
        var moduleRegistry = new Mock<IModuleRegistryService>();
        moduleRegistry.Setup(x => x.ListModulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ModuleCatalogEntry>());
        var eligibilityService = new BindingEligibilityService(ConnectionRepository, new PermissiveConnectorDefinitionRegistry());
        var configurationEvidence = new IntegrationConfigurationEvidenceBuilder(
            settingsResolver.Object,
            ConnectionRepository,
            eligibilityService,
            moduleRegistry.Object);

        return new IntegrationDispatchReadinessEvaluator(
            IntegrationRepository,
            BindingRepository,
            ScheduleRepository,
            slots.Object,
            ConnectionRepository,
            eligibilityService,
            new AdvisoryConnectionVerificationReadinessPolicy(),
            new IntegrationConfigurationRegistry(Array.Empty<ModuleIntegrationConfigurationBinding>()),
            configurationEvidence,
            TimeProvider.System);
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

/// <summary>
/// One row from a CTX-2 historical-provenance family, as captured by
/// <see cref="ResourceOwnershipMoveTestHarness.CaptureHistoricalProvenanceAsync"/>. <see cref="TableName"/>
/// plus <see cref="RowId"/> uniquely identify the row across every family in one flat, orderable list, so
/// a single <c>BeEquivalentTo</c> over the whole collection proves no move rewrote anything, anywhere.
/// </summary>
public sealed record HistoricalProvenanceRow(string TableName, string RowId, string? IntegrationId, string? ResourceContextId);
