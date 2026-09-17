using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Notifications;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.PlatformSettings;
using Microsoft.EntityFrameworkCore;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.Integration;

/// <summary>
/// Wires a real <see cref="IntegrationManagementService"/> together with every real operator-facing
/// service that can clear one of the blockers CTX-3B Plan 6a adds to Integration hard delete, over a
/// single EF Core InMemory <see cref="AppertureDbContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// Follows <see cref="Apperture.UnitTests.ResourceContexts.ManagementServiceTestHarness"/>'s shape: one
/// fixed actor, an always-allow authorization evaluator (these tests are about business logic, not
/// authorization), and a recording <see cref="IAuditService"/>. The same recorder is shared by the
/// Integration, notification-definition and settings services, so an assertion on
/// <see cref="Audit"/> sees everything the delete path caused, not a filtered view of it.
/// </para>
/// <para>
/// <see cref="Settings"/> is the real <see cref="IntegrationSettingsService"/> over the same DbContext and
/// the same <see cref="SecretStore"/> the assertions read, never a mock. The whole point of Task 5 is that
/// a sensitive setting's row holds only an opaque secret reference, so a mocked secret store would make the
/// one assertion that matters vacuously pass.
/// </para>
/// </remarks>
internal sealed class IntegrationLifecycleTestHarness : IDisposable
{
    private readonly SqlNotificationDefinitionStore _definitionStore;
    private readonly IIntegrationRepository _integrationRepository;
    private int _nextSequence;

    public IntegrationLifecycleTestHarness()
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

        Audit = new RecordingAuditService();
        SecretStore = new InMemorySecretStore();

        _integrationRepository = new SqlIntegrationRepository(Db);
        _definitionStore = new SqlNotificationDefinitionStore(Db);

        var principalContext = new FixedPrincipalContext(new FixedActor());
        var authorizationEvaluation = new AlwaysAllowAuthorizationEvaluationService();
        var resourceContexts = new ResourceContextService(new SqlResourceContextRepository(Db), TimeProvider.System);

        // Real settings service over the same DbContext and secret store, so `Db.PlatformSettingValues`
        // assertions see real rows and `SecretStore` assertions see real secret state transitions.
        var settingRegistry = new LifecycleIntegrationSettingDefinitionRegistry();
        var settingRepository = new SqlPlatformSettingValueRepository(Db);
        var optionProviderCoordinator = new PlatformSettingOptionProviderCoordinator(
            new PlatformSettingOptionProviderRegistry([new TestIntegrationOptionsOptionProvider()]),
            new PlatformSettingOptionProviderAuthorization(principalContext, authorizationEvaluation, _integrationRepository),
            new IntegrationSettingOptionProviderAuthorization(principalContext, authorizationEvaluation, _integrationRepository));

        Settings = new IntegrationSettingsService(
            settingRegistry,
            settingRepository,
            new IntegrationSettingsResolver(settingRegistry, settingRepository),
            optionProviderCoordinator,
            new PlatformSettingSecretMaterialProvisioner(SecretStore),
            principalContext,
            Audit,
            _integrationRepository);

        Integrations = new IntegrationManagementService(
            principalContext,
            authorizationEvaluation,
            _integrationRepository,
            resourceContexts,
            Db,
            // Real SqlResourceOwnershipMutationGate is a documented no-op on EF Core InMemory; these tests
            // are about referential lifecycle, not gate ordering.
            new SqlResourceOwnershipMutationGate(Db),
            Audit,
            Settings);

        Definitions = new NotificationDefinitionService(
            _definitionStore,
            new NoOpRuleValidator(),
            new NoOpTemplateValidator(),
            new NoOpTemplateRenderer(),
            new NoOpRenderContextFactory(),
            new NotificationDefinitionAuthorization(
                principalContext,
                new AlwaysAllowAuthorizationService(),
                authorizationEvaluation,
                _integrationRepository),
            principalContext,
            _integrationRepository,
            Audit);

        Subscriptions = new SqlNotificationSubscriptionStore(Db);

        Routing = new AttentionRoutingManagementService(
            Db,
            _integrationRepository,
            new SqlAttentionRecipientResolver(Db, new OperationalTeamRecipientExpander(Db)));
    }

    public AppertureDbContext Db { get; }

    public IIntegrationManagementService Integrations { get; }

    public INotificationDefinitionService Definitions { get; }

    public INotificationSubscriptionStore Subscriptions { get; }

    public IAttentionRoutingManagementService Routing { get; }

    public IIntegrationSettingsService Settings { get; }

    public InMemorySecretStore SecretStore { get; }

    public RecordingAuditService Audit { get; }

    /// <summary>An ordinary (non-sensitive, non-provider-backed) Integration-scoped setting key.</summary>
    public string SettingKey => PlatformSettingTestDefinitions.IntegrationScopedTextKey;

    /// <summary>A sensitive Integration-scoped setting key: its stored row holds only an opaque secret reference.</summary>
    public string SensitiveSettingKey => PlatformSettingTestDefinitions.IntegrationScopedSecretKey;

    /// <summary>
    /// The <see cref="SecretId"/> currently referenced by the sensitive setting's stored row. Read from the
    /// persisted row rather than remembered from the write, so the assertions target the reference the
    /// system would actually have to dispose of -- exactly what a raw row delete would strand.
    /// </summary>
    public SecretId LastProvisionedSecretId
    {
        get
        {
            var row = Db.PlatformSettingValues.AsNoTracking().Single(
                v => v.Key == SensitiveSettingKey && v.ScopeKind == PlatformSettingScopeConstants.IntegrationKind);
            var reference = PlatformSettingsValueCodec.ParseFromStoredJson(
                PlatformSettingTestDefinitions.IntegrationScopedSecretSample, row.ValueJson) as string;
            return new SecretId(Guid.Parse(reference!));
        }
    }

    public async Task<DomainIntegration> CreateIntegrationAsync(CancellationToken cancellationToken = default)
    {
        var integration = new DomainIntegration(
            new IntegrationId($"int_{NextSequence()}"),
            "Sample Integration",
            "Sample.Mod",
            null,
            IntegrationState.Active,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        await _integrationRepository.AddAsync(integration, cancellationToken).ConfigureAwait(false);
        return integration;
    }

    /// <summary>Seeds a user-created, Integration-scoped definition through the real store (the only path that produces UserCreated definitions).</summary>
    public async Task<NotificationDefinition> AddUserNotificationDefinitionAsync(string integrationId)
    {
        var sequence = NextSequence();
        return await _definitionStore.CreateUserDefinitionAsync(
            new NotificationDefinitionCreateCommand(
                $"Test Definition {sequence}",
                "Description",
                integrationId,
                RuleJson: """{"schemaVersion":"apperture.notification.rule/v1"}""",
                TemplateJson: """{"schemaVersion":"apperture.notification.template/v1"}""",
                IsEnabled: true,
                DefaultSeverity: "warning",
                CreatedByActorId: "test-actor"));
    }

    /// <summary>Seeds a Platform-scoped, platform-built-in definition carrying no IntegrationId at all.</summary>
    public async Task<NotificationDefinitionEntity> AddPlatformScopedDefinitionAsync(string id)
    {
        var entity = BuildDefinitionEntity(id, NotificationDefinitionOrigins.PlatformBuiltIn, integrationId: null);
        Db.NotificationDefinitions.Add(entity);
        await Db.SaveChangesAsync();
        return entity;
    }

    /// <summary>
    /// Inserts every real built-in NotificationDefinition seed the platform and the shipped modules define,
    /// exactly as their <c>BuiltIn(...)</c> factories produce them.
    /// </summary>
    /// <remarks>
    /// These four factories are the entire "future seed" surface -- built-in definitions reach a database
    /// only through migrations that call them, so asserting over their output is asserting over every
    /// built-in row that can ever exist. This is what makes
    /// <c>No_undeletable_definition_can_ever_block_integration_delete</c> a real tripwire: if a seed ever
    /// starts attaching an IntegrationId, it becomes an Integration-delete blocker that Task 2's
    /// user-created-only delete operation cannot clear, and the guard fails here rather than in production.
    /// </remarks>
    public async Task SeedAllBuiltInDefinitionsAsync()
    {
        var seededAt = DateTimeOffset.UtcNow;
        NotificationDefinition[] seeds =
        [
            ExpectedRunMissingNotificationDefinitionSeed.BuiltIn(seededAt),
            HelloWorldDemoNotificationDefinitionSeed.BuiltIn(seededAt),
            FileImportPartialFailureNotificationDefinitionSeed.BuiltIn(seededAt),
            ApiPollingPartialFailureNotificationDefinitionSeed.BuiltIn(seededAt)
        ];

        foreach (var seed in seeds)
        {
            Db.NotificationDefinitions.Add(new NotificationDefinitionEntity
            {
                Id = seed.Id.Value,
                DisplayName = seed.DisplayName,
                Description = seed.Description,
                SourceKind = seed.SourceKind,
                Origin = seed.Origin,
                Scope = seed.Scope,
                Mutability = seed.Mutability,
                ModuleId = seed.ModuleId,
                IntegrationId = seed.IntegrationId,
                PrimaryEventType = seed.PrimaryEventType,
                IsEnabled = seed.IsEnabled,
                DefaultSeverity = seed.DefaultSeverity,
                RuleJson = seed.RuleJson,
                TemplateJson = seed.TemplateJson,
                CreatedAtUtc = seed.CreatedAtUtc,
                UpdatedAtUtc = seed.UpdatedAtUtc,
                CreatedByActorId = seed.CreatedByActorId,
                UpdatedByActorId = seed.UpdatedByActorId,
            });
        }

        await Db.SaveChangesAsync();
    }

    /// <summary>Seeds a subscription whose own nullable IntegrationId is set independently of its definition's scope.</summary>
    public async Task<NotificationSubscriptionEntity> AddSubscriptionAsync(string definitionId, string? integrationId)
    {
        var entity = new NotificationSubscriptionEntity
        {
            Id = $"nsub_{NextSequence()}",
            NotificationDefinitionId = definitionId,
            IntegrationId = integrationId,
            RecipientKind = NotificationRecipientKinds.Principal,
            RecipientRef = "user-1",
            DeliveryMode = NotificationDeliveryModes.Immediate,
            Origin = NotificationDefinitionOrigins.UserCreated,
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByActorId = "test-actor",
            UpdatedByActorId = "test-actor",
        };
        Db.NotificationSubscriptions.Add(entity);
        await Db.SaveChangesAsync();
        return entity;
    }

    /// <summary>Seeds an Integration attention-routing override through the real management service.</summary>
    public async Task AddAttentionRoutingAsync(string integrationId, string conditionKey = AttentionConditionKeys.ExpectedRunMissing)
    {
        await Routing.UpsertIntegrationRoutingAsync(
            new IntegrationId(integrationId),
            conditionKey,
            new AttentionRoutingUpsertCommand(
                IsEnabled: true,
                SendResolvedNotification: true,
                SuppressRemindersWhenAcknowledged: false,
                SuppressEscalationWhenAcknowledged: false,
                EscalationAfter: TimeSpan.FromMinutes(30),
                ReminderInterval: TimeSpan.FromMinutes(10),
                PrimaryOperationalTeamId: null,
                EscalationOperationalTeamId: null));
    }

    private static NotificationDefinitionEntity BuildDefinitionEntity(string id, string origin, string? integrationId) =>
        new()
        {
            Id = id,
            DisplayName = "Built-in Definition",
            Description = null,
            SourceKind = NotificationSourceKinds.PlatformEvent,
            Origin = origin,
            Scope = NotificationDefinitionScopes.Platform,
            Mutability = NotificationDefinitionMutabilities.ReadOnly,
            ModuleId = null,
            IntegrationId = integrationId,
            PrimaryEventType = null,
            IsEnabled = true,
            DefaultSeverity = "warning",
            RuleJson = """{"schemaVersion":"apperture.notification.rule/v1"}""",
            TemplateJson = """{"schemaVersion":"apperture.notification.template/v1"}""",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByActorId = null,
            UpdatedByActorId = null,
        };

    private int NextSequence() => System.Threading.Interlocked.Increment(ref _nextSequence);

    public void Dispose() => Db.Dispose();

    /// <summary>Serves both the ordinary and the sensitive Integration-scoped test definitions.</summary>
    private sealed class LifecycleIntegrationSettingDefinitionRegistry : IIntegrationSettingDefinitionRegistry
    {
        private static readonly IReadOnlyList<PlatformSettingDefinition> All =
        [
            PlatformSettingTestDefinitions.IntegrationScopedTextSample,
            PlatformSettingTestDefinitions.IntegrationScopedSecretSample,
            PlatformSettingTestDefinitions.IntegrationScopedSelectSample
        ];

        private static readonly Dictionary<string, PlatformSettingDefinition> ByKey =
            All.ToDictionary(d => d.Key, StringComparer.Ordinal);

        public IReadOnlyList<PlatformSettingDefinition> GetAll() => All;

        public PlatformSettingDefinition? TryGet(string key) => ByKey.GetValueOrDefault(key);
    }

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

    private sealed class AlwaysAllowAuthorizationService : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    /// <summary>Records every appended <see cref="AuditRecord"/> into an in-memory list.</summary>
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
            throw new NotSupportedException("Not exercised by IntegrationLifecycleTestHarness.");

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by IntegrationLifecycleTestHarness.");

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by IntegrationLifecycleTestHarness.");
    }

    private sealed class NoOpRuleValidator : INotificationRuleValidator
    {
        public NotificationValidationResult Validate(string ruleJson, NotificationRuleValidationContext context) =>
            NotificationValidationResult.Success();
    }

    private sealed class NoOpTemplateValidator : INotificationTemplateValidator
    {
        public NotificationValidationResult Validate(string templateJson, NotificationTemplateValidationContext context) =>
            NotificationValidationResult.Success();
    }

    private sealed class NoOpTemplateRenderer : INotificationTemplateRenderer
    {
        public RenderedNotificationSnapshot Render(
            NotificationTemplateDocument template,
            NotificationRenderContext context,
            string? variantKey = null) =>
            throw new NotSupportedException("Not exercised by IntegrationLifecycleTestHarness.");
    }

    private sealed class NoOpRenderContextFactory : INotificationRenderContextFactory
    {
        public NotificationRenderContext FromSample(NotificationRenderSample sample) =>
            throw new NotSupportedException("Not exercised by IntegrationLifecycleTestHarness.");
    }
}
