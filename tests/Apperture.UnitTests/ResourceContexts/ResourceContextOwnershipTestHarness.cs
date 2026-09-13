using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Moq;

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
    public ResourceContextOwnershipTestHarness()
    {
        Db = new AppertureDbContext(
            new DbContextOptionsBuilder<AppertureDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
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

        Integrations = new IntegrationManagementService(
            new AlwaysActor(),
            new AlwaysAllow(),
            IntegrationRepository,
            ResourceContexts,
            Db);

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
            ResourceContexts);

        Eligibility = new BindingEligibilityService(ConnectionRepository, ConnectorRegistry);
        Bindings = new BindingService(
            new AlwaysActor(),
            new AlwaysAllowResource(),
            BindingRepository,
            ConnectionRepository,
            Eligibility,
            IntegrationRepository);

        Schedules = new SqlScheduleManagementService(
            ScheduleRepository,
            new StubClaimOptions(),
            TimeProvider.System,
            ResourceContexts,
            IntegrationRepository);
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

    public ValueTask DisposeAsync() => Db.DisposeAsync();

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
