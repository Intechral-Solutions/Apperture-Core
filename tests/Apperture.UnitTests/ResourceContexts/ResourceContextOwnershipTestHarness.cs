using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using System.Linq.Expressions;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// Wires the real management services over an in-memory store so ownership tests exercise the
/// production ownership rules rather than mocks of them.
/// </summary>
/// <remarks>
/// Authorization is permitted throughout on purpose: CTX-1 changes no authorization semantics, and
/// these tests are about ownership. Authorization behavior is covered by its own suites.
/// </remarks>
public sealed class ResourceContextOwnershipTestHarness : IAsyncDisposable
{
    private readonly string _databaseName = Guid.NewGuid().ToString("N");

    // EF Core's InMemory store lives on the internal service provider, not merely on the database name;
    // two DbContexts with different configurations (e.g. different interceptors) get different internal
    // service providers and therefore different, isolated stores even with the same name unless they
    // share an explicit root like this one.
    private readonly InMemoryDatabaseRoot _databaseRoot = new();

    private readonly List<AppertureDbContext> _extraDbContexts = new();
    private int _nextResourceSequence;

    public ResourceContextOwnershipTestHarness()
    {
        Db = new AppertureDbContext(
            new DbContextOptionsBuilder<AppertureDbContext>()
                .UseInMemoryDatabase(_databaseName, _databaseRoot)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        // The migration seeds Shared before anything can own it; mirror that here.
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

        ResourceContextRepository = new SqlResourceContextRepository(Db);
        ResourceContexts = new ResourceContextService(ResourceContextRepository, TimeProvider.System);
        IntegrationRepository = new SqlIntegrationRepository(Db);
        ConnectionRepository = new SqlConnectionRepository(Db);
        BindingRepository = new SqlBindingRepository(Db);
        ScheduleRepository = new SqlScheduleRepository(Db);

        var ownershipGate = new SqlResourceOwnershipMutationGate(Db);

        Integrations = new IntegrationManagementService(
            new AlwaysActor(),
            new AlwaysAllow(),
            IntegrationRepository,
            ResourceContexts,
            Db,
            ownershipGate,
            new IntegrationManagementServiceTestDependencies.RecordingAuditService(),
            IntegrationManagementServiceTestDependencies.CreateSettingsService(Db, IntegrationRepository));

        ConnectorRegistry = new PermissiveConnectorDefinitionRegistry();
        Connections = new ConnectionManagementService(
            new AlwaysActor(),
            new AlwaysAllow(),
            ConnectionRepository,
            ConnectorRegistry,
            new NoOpConnectionVerificationService(),
            // Never exercised: these tests supply no secret values, which is itself the point --
            // ResourceContext ownership must not touch secret material.
            new ConnectionSecretMaterialProvisioner(SecretStore.Object),
            ResourceContexts,
            Db,
            ownershipGate,
            Mock.Of<IAuditService>());

        Eligibility = new BindingEligibilityService(ConnectionRepository, ConnectorRegistry);
        Bindings = new BindingService(
            new AlwaysActor(),
            new AlwaysAllowResource(),
            BindingRepository,
            ConnectionRepository,
            Eligibility,
            IntegrationRepository,
            Db,
            ownershipGate);

        Schedules = new SqlScheduleManagementService(
            ScheduleRepository,
            new StubClaimOptions(),
            TimeProvider.System,
            ResourceContexts,
            IntegrationRepository,
            Db,
            ownershipGate);

        // Task 5 (CTX-3A Plan 4, §5.3): RunQueryService over the same DbContext, so
        // RunHistoricalSnapshotFilteringTests exercises the production server-side filter
        // (RunQueryFilter.ResourceContextId) rather than a mock of it. Authorization stubs always
        // allow, matching this harness's ownership-focused scope elsewhere.
        RunQueryService = new RunQueryService(
            Db,
            new StubOperationRegistry(),
            new RunKindResolver(Db),
            new AllowWorkflowReadAuthz(),
            new AllowOperationReadAuthz(),
            new StubWorkflowDefinitionResolver(),
            new StubComposition());
    }

    /// <summary>Strict-free secret store double; assert on it to prove ownership never provisions secrets.</summary>
    public Mock<ISecretStore> SecretStore { get; } = new();

    public AppertureDbContext Db { get; }
    public IResourceContextRepository ResourceContextRepository { get; }
    public IResourceContextService ResourceContexts { get; }
    public IIntegrationRepository IntegrationRepository { get; }
    public IConnectionRepository ConnectionRepository { get; }
    public IBindingRepository BindingRepository { get; }
    public IScheduleRepository ScheduleRepository { get; }
    public IConnectorDefinitionRegistry ConnectorRegistry { get; }
    public IIntegrationManagementService Integrations { get; }
    public IConnectionManagementService Connections { get; }
    public IBindingEligibilityService Eligibility { get; }
    public IBindingService Bindings { get; }
    public IScheduleManagementService Schedules { get; }
    public IRunQueryService RunQueryService { get; }

    /// <summary>
    /// Builds a second <see cref="IResourceContextRepository"/> over the SAME underlying InMemory
    /// database as <see cref="ResourceContextRepository"/>, instrumented to count EF Core LINQ query
    /// compilations, plus a callback to read the running count.
    /// </summary>
    /// <remarks>
    /// EF Core's InMemory provider issues no ADO.NET commands, so a relational
    /// <c>IDbCommandInterceptor</c> cannot observe query activity; this counts
    /// <see cref="IQueryExpressionInterceptor.QueryCompilationStarting"/> events instead, which fire once
    /// per distinct query shape regardless of provider -- a proxy for "how many distinct queries were
    /// issued" rather than a literal per-execution count, since EF caches compiled plans and would not
    /// re-raise the event for a repeated call with the same shape.
    /// </remarks>
    /// <remarks>
    /// This is deliberately a separate <see cref="AppertureDbContext"/> rather than an interceptor added
    /// to <see cref="Db"/> directly: EF Core keys its internal service-provider cache on the exact set of
    /// configured interceptors, so giving every harness instance its own interceptor object would give
    /// every test in this suite a distinct configuration fingerprint and force EF to build a fresh
    /// internal service provider per test, tripping its own
    /// <c>CoreEventId.ManyServiceProvidersCreatedWarning</c> (which is an error by default) once the
    /// suite creates more than twenty. Building the counting context only when a test asks for one keeps
    /// that cost limited to the tests that actually need it.
    /// </remarks>
    public (IResourceContextRepository Repository, Func<int> QueryCount) CreateQueryCountingRepository()
    {
        var counter = new QueryCompilationCountInterceptor();
        var countingDb = new AppertureDbContext(
            new DbContextOptionsBuilder<AppertureDbContext>()
                .UseInMemoryDatabase(_databaseName, _databaseRoot)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .AddInterceptors(counter)
                .Options);
        _extraDbContexts.Add(countingDb);

        return (new SqlResourceContextRepository(countingDb), () => counter.Count);
    }

    /// <summary>Creates an Integration owned by <paramref name="contextId"/>, moving it to <paramref name="state"/> via the real management service.</summary>
    public async Task<DomainIntegration> CreateIntegrationAsync(
        ResourceContextId contextId,
        IntegrationState state = IntegrationState.Active,
        CancellationToken cancellationToken = default)
    {
        var created = await Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest($"Integration {NextSequence()}", "Sample.Mod", ResourceContextId: contextId),
            cancellationToken);

        if (state != IntegrationState.Active)
            created = await Integrations.UpdateIntegrationAsync(created.Id, new UpdateIntegrationRequest(State: state), cancellationToken);

        return created;
    }

    /// <summary>Creates a Connection owned by <paramref name="contextId"/>, retiring it via the real management service when <paramref name="state"/> is Retired.</summary>
    public async Task<Connection> CreateConnectionAsync(
        ResourceContextId contextId,
        ConnectionState state = ConnectionState.Active,
        CancellationToken cancellationToken = default)
    {
        var created = await Connections.CreateConnectionAsync(
            new CreateConnectionRequest(
                new ConnectorDefinitionId($"test.connector.{NextSequence()}"),
                $"Connection {NextSequence()}",
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                ResourceContextId: contextId),
            cancellationToken);

        if (state == ConnectionState.Retired)
            await Connections.RetireConnectionAsync(created.Id, cancellationToken);

        return created;
    }

    /// <summary>Creates a standalone (no owning Integration) ScheduleDefinition owned by <paramref name="contextId"/>.</summary>
    public Task<ScheduleDefinition> CreateStandaloneScheduleAsync(
        ResourceContextId contextId,
        bool enabled = true,
        CancellationToken cancellationToken = default)
    {
        var definition = new ScheduleDefinition(
            new ScheduleId($"s-{NextSequence()}"),
            "Nightly",
            "0 0 * * *",
            "UTC",
            Enabled: enabled,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            ExecutionPolicyRef: null,
            Version: 1,
            IntegrationId: null) with
        { ResourceContextId = contextId };

        return Schedules.CreateScheduleAsync(definition, cancellationToken).AsTask();
    }

    /// <summary>
    /// Seeds an OperationRun row (plus the matching OperationStartRequest RunKindResolver needs to
    /// classify it as an operation run) directly via the DbContext, carrying whatever
    /// <paramref name="resourceContextId"/> snapshot the caller supplies -- including null, for the
    /// pre-CTX-2 / legitimately-contextless case. This never goes through a dispatch pipeline: it
    /// exists purely to plant a Run row with a specific historical ResourceContextId snapshot,
    /// independent of the Integration's current ownership (see
    /// <see cref="SetIntegrationResourceContextDirectlyAsync"/>).
    /// </summary>
    public async Task<OperationRunEntity> SeedOperationRunAsync(
        IntegrationId? integrationId,
        ResourceContextId? resourceContextId,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var sequence = NextSequence();
        var runId = $"run-{sequence}";
        var definitionId = $"op-def-{sequence}";
        var instanceId = $"op-instance-{sequence}";

        var run = new OperationRunEntity
        {
            Id = runId,
            WorkflowInstanceId = instanceId,
            DefinitionId = definitionId,
            Status = status ?? WorkflowLifecycleStatusNames.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            IntegrationId = integrationId?.Value,
            ResourceContextId = resourceContextId?.Value
        };
        Db.OperationRuns.Add(run);
        Db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = runId,
            OperationInstanceId = instanceId,
            OperationDefinitionId = definitionId,
            IntegrationId = integrationId?.Value,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await Db.SaveChangesAsync(cancellationToken);
        return run;
    }

    /// <summary>
    /// Controlled test setup only: rewrites an Integration's <b>current</b> ResourceContext ownership
    /// directly via the DbContext, bypassing <see cref="IIntegrationManagementService"/> entirely.
    /// No supported API changes Integration ownership yet (that is CTX-3B); this exists so
    /// RunHistoricalSnapshotFilteringTests can prove the Run list filter is driven by each Run's own
    /// immutable ResourceContextId snapshot, never by the Integration's current column, even under a
    /// future/direct ownership change like this one.
    /// </summary>
    public async Task SetIntegrationResourceContextDirectlyAsync(
        IntegrationId integrationId,
        ResourceContextId newContextId,
        CancellationToken cancellationToken = default)
    {
        var entity = await Db.Integrations.FindAsync(new object?[] { integrationId.Value }, cancellationToken)
            ?? throw new InvalidOperationException($"Integration '{integrationId.Value}' not found.");
        entity.ResourceContextId = newContextId.Value;
        await Db.SaveChangesAsync(cancellationToken);
    }

    private int NextSequence() => System.Threading.Interlocked.Increment(ref _nextResourceSequence);

    public async ValueTask DisposeAsync()
    {
        foreach (var extra in _extraDbContexts)
            await extra.DisposeAsync();
        await Db.DisposeAsync();
    }

    /// <summary>
    /// Counts EF Core LINQ query compilations. Provider-agnostic (unlike <c>IDbCommandInterceptor</c>,
    /// which never fires for the InMemory provider since it issues no ADO.NET commands), so it works
    /// here where a relational command interceptor would not.
    /// </summary>
    private sealed class QueryCompilationCountInterceptor : IQueryExpressionInterceptor
    {
        public int Count { get; private set; }

        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        {
            Count++;
            return queryExpression;
        }
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

    /// <summary>Minimal RunQueryService dependency stubs (mirrors RunRouteFamilyParityTests' pattern) -- these tests are about ResourceContext scoping, not operation registry or authorization contracts.</summary>
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

    /// <summary>
    /// Accepts any connector definition id. These tests are about ownership, not connector contracts.
    /// </summary>
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
                    // One config and one secret field, so tests can prove that ownership leaves
                    // secret references untouched.
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
