using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.Audit;
using Apperture.ControlPlane.Security.Models;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Notifications;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Core.Domain.Workflow;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// CTX-3A Plan 4, Task 9: wires the real Inventory pipeline (InventoryQueryService plus the real
/// management/query services it composes) over an in-memory store, following
/// <see cref="ResourceContextOwnershipTestHarness"/>'s pattern rather than inventing another harness
/// shape. Modules are the one fake here (a real IModuleRegistryService would require actual module
/// deployment scanning, which is out of scope for Context classification); everything else -- Integrations,
/// Connections, Schedules, NotificationDefinitions, Runs, Audit, ResourceContexts -- is real, backed by
/// the same in-memory DbContext, so these tests exercise the production Context predicates and
/// classification rules.
/// </summary>
public sealed class InventoryTestHarness : IAsyncDisposable
{
    private readonly string _databaseName = Guid.NewGuid().ToString("N");
    private readonly InMemoryDatabaseRoot _databaseRoot = new();
    private int _nextSequence;
    private readonly List<ModuleCatalogEntry> _modules = new();
    private readonly IAuditService _auditService;
    private readonly IIntegrationManagementService _integrations;
    private readonly IConnectionManagementService _connections;
    private readonly IScheduleManagementService _schedules;

    public AppertureDbContext Db { get; }
    public IResourceContextService ResourceContexts { get; }
    public IInventoryQueryService Inventory { get; }
    public TimeProvider Clock { get; }

    public static Task<InventoryTestHarness> CreateAsync(
        bool canViewIntegrations = true,
        bool canViewConnections = true,
        bool canManageSchedules = true) =>
        Task.FromResult(new InventoryTestHarness(canViewIntegrations, canViewConnections, canManageSchedules));

    private InventoryTestHarness(bool canViewIntegrations, bool canViewConnections, bool canManageSchedules)
    {
        Db = new AppertureDbContext(
            new DbContextOptionsBuilder<AppertureDbContext>()
                .UseInMemoryDatabase(_databaseName, _databaseRoot)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        Db.ResourceContexts.Add(new ResourceContextEntity
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
        Db.SaveChanges();

        Clock = new ManualTimeProvider(DateTimeOffset.UtcNow);

        var resourceContextRepository = new SqlResourceContextRepository(Db);
        ResourceContexts = new ResourceContextService(resourceContextRepository, TimeProvider.System);

        var integrationRepository = new SqlIntegrationRepository(Db);
        var connectionRepository = new SqlConnectionRepository(Db);
        var scheduleRepository = new SqlScheduleRepository(Db);

        var ownershipGate = new SqlResourceOwnershipMutationGate(Db);

        _integrations = new IntegrationManagementService(
            new AlwaysActor(),
            new AlwaysAllow(),
            integrationRepository,
            ResourceContexts,
            Db,
            ownershipGate,
            new IntegrationManagementServiceTestDependencies.RecordingAuditService(),
            IntegrationManagementServiceTestDependencies.CreateSettingsService(Db, integrationRepository));

        _connections = new ConnectionManagementService(
            new AlwaysActor(),
            new AlwaysAllow(),
            connectionRepository,
            new PermissiveConnectorDefinitionRegistry(),
            new NoOpConnectionVerificationService(),
            new ConnectionSecretMaterialProvisioner(new Mock<ISecretStore>().Object),
            ResourceContexts,
            Db,
            ownershipGate,
            Mock.Of<IAuditService>());

        var schedules = new SqlScheduleManagementService(
            scheduleRepository,
            new StubClaimOptions(),
            TimeProvider.System,
            ResourceContexts,
            integrationRepository,
            Db,
            ownershipGate,
            new AlwaysActor());
        _schedules = schedules;

        _auditService = new SqlAuditService(Db);

        var notificationStore = new SqlNotificationDefinitionStore(Db);
        var notificationAuth = new NotificationDefinitionAuthorization(
            new AlwaysActor(),
            new AlwaysAllowResource(),
            new AlwaysAllow(),
            integrationRepository);
        var notificationService = new NotificationDefinitionService(
            notificationStore,
            Mock.Of<INotificationRuleValidator>(),
            Mock.Of<INotificationTemplateValidator>(),
            Mock.Of<INotificationTemplateRenderer>(),
            Mock.Of<INotificationRenderContextFactory>(),
            notificationAuth,
            new AlwaysActor(),
            integrationRepository,
            _auditService);

        var runQueryService = new RunQueryService(
            Db,
            new StubOperationRegistry(),
            new RunKindResolver(Db),
            new AllowWorkflowReadAuthz(),
            new AllowOperationReadAuthz(),
            new StubWorkflowDefinitionResolver(),
            new StubComposition());

        var auditTimeline = new AuditTimelineQueryService(_auditService);

        var modules = new Mock<IModuleRegistryService>();
        modules.Setup(x => x.ListModulesAsync(It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask<IReadOnlyList<ModuleCatalogEntry>>(_modules.ToList()));

        var capabilities = new Mock<IOperatorCapabilitiesQueryService>();
        capabilities.Setup(x => x.GetCapabilitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorCapabilitiesDto(
                CanManageSchedules: canManageSchedules,
                CanManageRecovery: false,
                CanViewAudit: true,
                CanViewRuntimeHealth: false,
                CanManageModules: true,
                CanViewConnections: canViewConnections,
                CanViewIntegrations: canViewIntegrations,
                CanViewNotificationDefinitions: true));

        Inventory = new InventoryQueryService(
            capabilities.Object,
            modules.Object,
            _integrations,
            _connections,
            schedules,
            notificationService,
            runQueryService,
            auditTimeline,
            Clock);
    }

    public Task<Apperture.Core.Domain.ResourceContexts.ResourceContext> CreateApplicationContextAsync(string name) =>
        ResourceContexts.CreateApplicationContextAsync(name).AsTask();

    public async Task<DomainIntegration> CreateIntegrationAsync(
        ResourceContextId contextId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        return await _integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest(displayName, "Sample.Mod", ResourceContextId: contextId),
            cancellationToken);
    }

    public async Task<Connection> CreateConnectionAsync(
        ResourceContextId contextId,
        string name,
        CancellationToken cancellationToken = default)
    {
        return await _connections.CreateConnectionAsync(
            new CreateConnectionRequest(
                new ConnectorDefinitionId($"test.connector.{NextSequence()}"),
                name,
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                ResourceContextId: contextId),
            cancellationToken);
    }

    public async Task<ScheduleDefinition> CreateScheduleAsync(
        ResourceContextId contextId,
        string name,
        bool enabled = true,
        CancellationToken cancellationToken = default)
    {
        var definition = new ScheduleDefinition(
            new ScheduleId($"s-{NextSequence()}"),
            name,
            "0 0 * * *",
            "UTC",
            Enabled: enabled,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            ExecutionPolicyRef: null,
            Version: 1,
            IntegrationId: null) with
        { ResourceContextId = contextId };
        return await _schedules.CreateScheduleAsync(definition, cancellationToken);
    }

    public async Task RetireIntegrationAsync(IntegrationId id, CancellationToken cancellationToken = default) =>
        await _integrations.RetireIntegrationAsync(id, cancellationToken);

    public async Task RetireConnectionAsync(ConnectionId id, CancellationToken cancellationToken = default) =>
        await _connections.RetireConnectionAsync(id, cancellationToken);

    /// <summary>Plants an Integration -> Connection binding row directly (the reference relationship, not ownership).</summary>
    public async Task BindConnectionAsync(IntegrationId integrationId, ConnectionId connectionId, CancellationToken cancellationToken = default)
    {
        Db.Bindings.Add(new BindingEntity
        {
            Id = $"b-{NextSequence()}",
            IntegrationId = integrationId.Value,
            ModuleId = "Sample.Mod",
            SlotName = "primary",
            ContractType = "test.contract",
            ConnectionId = connectionId.Value,
            State = 0
        });
        await Db.SaveChangesAsync(cancellationToken);
    }

    public Task CreateModuleAsync(string? moduleId = null)
    {
        var id = moduleId ?? $"module-{NextSequence()}";
        _modules.Add(new ModuleCatalogEntry(
            new ModuleDescriptor(id, id, "1.0", null, null, null, []),
            ModuleLifecycleState.InstalledEnabled,
            Installed: true,
            Enabled: true,
            EffectivelyActive: true,
            ValidationIssues: []));
        return Task.CompletedTask;
    }

    public async Task<NotificationDefinitionEntity> CreateNotificationDefinitionAsync(
        string displayName,
        string scope,
        IntegrationId? integrationId = null,
        string? moduleId = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new NotificationDefinitionEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = displayName,
            Description = null,
            SourceKind = NotificationSourceKinds.PlatformEvent,
            Origin = NotificationDefinitionOrigins.UserCreated,
            Scope = scope,
            Mutability = NotificationDefinitionMutabilities.FullyEditable,
            ModuleId = moduleId,
            IntegrationId = integrationId?.Value,
            PrimaryEventType = "test.event",
            IsEnabled = true,
            RuleJson = "{}",
            TemplateJson = "{}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        Db.NotificationDefinitions.Add(entity);
        await Db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <summary>Removes only the Integrations row, mirroring what DeleteIntegrationAsync does today (a known orphan condition -- design gate §13).</summary>
    public async Task HardDeleteIntegrationRowAsync(IntegrationId integrationId, CancellationToken cancellationToken = default)
    {
        var entity = await Db.Integrations.FindAsync(new object?[] { integrationId.Value }, cancellationToken);
        if (entity is not null)
        {
            Db.Integrations.Remove(entity);
            await Db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task SeedAuditRecordAsync(
        string action,
        string? resourceContextId,
        DateTimeOffset? at = null,
        string? summary = null,
        CancellationToken cancellationToken = default)
    {
        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId("test-actor"),
            action,
            Resource: null,
            // AuditTimelineEntry.Summary is derived downstream as "{action}: {outcome}" (or just the
            // action when outcome is empty) -- routing the caller-supplied "summary" here is how a seeded
            // row ends up findable via Name.Contains(...) once mapped through Inventory.
            Outcome: summary ?? "",
            at ?? Clock.GetUtcNow(),
            new CorrelationId(null),
            Details: null,
            ResourceContextId: resourceContextId);
        await _auditService.AppendAsync(record, cancellationToken);
    }

    public async Task<OperationRunEntity> SeedOperationRunAsync(
        string? resourceContextId,
        DateTimeOffset? createdAt = null,
        string? name = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var sequence = NextSequence();
        var runId = $"run-{sequence}";
        // MapRun's Name comes from ExecutionDefinitionId (falling back to RunId only when blank), so a
        // caller-supplied "name" must land on DefinitionId, not on some separate display field.
        var definitionId = name ?? $"op-def-{sequence}";
        var instanceId = $"op-instance-{sequence}";

        var run = new OperationRunEntity
        {
            Id = runId,
            WorkflowInstanceId = instanceId,
            DefinitionId = definitionId,
            Status = status ?? WorkflowLifecycleStatusNames.Completed,
            CreatedAt = createdAt ?? Clock.GetUtcNow(),
            ResourceContextId = resourceContextId
        };
        Db.OperationRuns.Add(run);
        Db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = runId,
            OperationInstanceId = instanceId,
            OperationDefinitionId = definitionId,
            CreatedAt = createdAt ?? Clock.GetUtcNow()
        });

        await Db.SaveChangesAsync(cancellationToken);
        return run;
    }

    private int NextSequence() => Interlocked.Increment(ref _nextSequence);

    public async ValueTask DisposeAsync() => await Db.DisposeAsync();

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AlwaysActor : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new Actor());

        private sealed class Actor : IActor
        {
            public ActorId Id => new("test-actor");
            public ActorKind Kind => ActorKind.Human;
            public string? DisplayName => "Test Actor";
        }
    }

    private sealed class AlwaysAllow : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class AlwaysAllowResource : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class NoOpConnectionVerificationService : IConnectionVerificationService
    {
        public ValueTask<ConnectionVerificationResult> VerifyAsync(Connection connection, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ConnectionVerificationResult(
                ConnectionVerificationStatus.NeverTested,
                DateTimeOffset.UnixEpoch,
                null));
    }

    private sealed class StubClaimOptions : ISchedulerClaimOptions
    {
        public int ClaimLeaseMinutes => 5;
    }

    private sealed class StubOperationRegistry : IOperationRegistryService
    {
        public ValueTask<IReadOnlyList<OperationDefinition>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<OperationDefinition>>(Array.Empty<OperationDefinition>());

        public ValueTask<OperationDefinition?> GetOperationAsync(OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<OperationDefinition?>(new OperationDefinition(operationId, operationId.Value, "mod", OperationCategory.Command, null, []));

        public ValueTask<IReadOnlyList<OperationDefinition>> GetByModuleAsync(string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<OperationDefinition>>(Array.Empty<OperationDefinition>());

        public ValueTask RegisterAsync(OperationDefinition definition, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class AllowWorkflowReadAuthz : IWorkflowRunReadAuthorizationService
    {
        public ValueTask EnsureCanReadRunsAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class AllowOperationReadAuthz : IOperationRunReadAuthorizationService
    {
        public ValueTask EnsureCanReadRunsAsync(OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class StubWorkflowDefinitionResolver : IWorkflowDefinitionResolver
    {
        public ValueTask<WorkflowDefinition?> GetByIdAsync(WorkflowDefinitionId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowDefinition?>(null);
    }

    private sealed class StubComposition : IRuntimeModuleCompositionQueryService
    {
        public ValueTask<HostEffectiveModuleSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HostEffectiveModuleSnapshot(
                DateTimeOffset.UtcNow,
                [],
                [],
                [],
                [],
                []));
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
                definition = new ConnectorDefinition(id, id.Value, null, new Dictionary<string, string>())
                {
                    ConnectionFields =
                    [
                        new ConnectionFieldDescriptor("Endpoint", "Endpoint", IsSecret: false),
                        new ConnectionFieldDescriptor("Token", "Token", IsSecret: true)
                    ]
                };
                _definitions[id.Value] = definition;
            }

            return definition;
        }
    }
}
