using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.Notifications;

/// <summary>
/// Wires a real <see cref="NotificationDefinitionService"/> over the real
/// <see cref="SqlNotificationDefinitionStore"/> (EF Core InMemory), following the shape of
/// <see cref="Apperture.UnitTests.ResourceContexts.ManagementServiceTestHarness"/>: a fixed actor, a
/// configurable authorization double mirroring <see cref="NotificationDefinitionAuthorization"/>'s two
/// gates (instance-level <c>notification_definition.manage</c> and Integration-scoped
/// <c>integration.update</c>), and a recording <see cref="IAuditService"/>.
/// </summary>
public sealed class NotificationDefinitionTestHarness : IDisposable
{
    private readonly AppertureDbContext _db;
    private readonly SqlNotificationDefinitionStore _store;
    private readonly IIntegrationRepository _integrations;
    private int _nextSequence;

    public NotificationDefinitionTestHarness(bool denyIntegrationUpdate = false, bool denyInstanceManage = false)
    {
        _db = new AppertureDbContext(
            new DbContextOptionsBuilder<AppertureDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        _store = new SqlNotificationDefinitionStore(_db);
        _integrations = new SqlIntegrationRepository(_db);
        Audit = new RecordingAuditService();

        var authorization = new NotificationDefinitionAuthorization(
            new FixedPrincipalContext(new FixedActor()),
            new ConfigurableAuthorizationService(denyInstanceManage),
            new ConfigurableAuthorizationEvaluationService(denyIntegrationUpdate, () => IntegrationUpdateChecks++),
            _integrations);

        Definitions = new NotificationDefinitionService(
            _store,
            new NoOpRuleValidator(),
            new NoOpTemplateValidator(),
            new NoOpTemplateRenderer(),
            new NoOpRenderContextFactory(),
            authorization,
            new FixedPrincipalContext(new FixedActor()),
            _integrations,
            Audit);
    }

    public AppertureDbContext Db => _db;

    /// <summary>How many times the Integration-scoped <c>integration.update</c> gate has been evaluated.</summary>
    public int IntegrationUpdateChecks { get; private set; }

    public INotificationDefinitionService Definitions { get; }

    public RecordingAuditService Audit { get; }

    public async Task<DomainIntegration> CreateIntegrationAsync()
    {
        var integration = new DomainIntegration(
            new IntegrationId($"int_{NextSequence()}"),
            "Test Integration",
            "Test.Module",
            null,
            IntegrationState.Active,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        await _integrations.AddAsync(integration);
        return integration;
    }

    /// <summary>Seeds a user-created, Integration-scoped definition directly through the store, bypassing service-level authorization (so this helper works even when the harness is configured to deny create/update permissions for the delete-authorization test).</summary>
    public async Task<NotificationDefinition> CreateUserDefinitionAsync(string integrationId)
    {
        var sequence = NextSequence();
        return await _store.CreateUserDefinitionAsync(
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

    /// <summary>Seeds a built-in (Platform or Module) definition directly in the DbContext, since the store's create path only ever produces UserCreated definitions.</summary>
    public async Task<NotificationDefinitionEntity> SeedBuiltInDefinitionAsync(string origin)
    {
        var entity = new NotificationDefinitionEntity
        {
            Id = $"ndef_builtin_{NextSequence()}",
            DisplayName = "Built-in Definition",
            Description = null,
            SourceKind = NotificationSourceKinds.PlatformEvent,
            Origin = origin,
            Scope = NotificationDefinitionScopes.Platform,
            Mutability = NotificationDefinitionMutabilities.ReadOnly,
            ModuleId = null,
            IntegrationId = null,
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
        _db.NotificationDefinitions.Add(entity);
        await _db.SaveChangesAsync();
        return entity;
    }

    public async Task AddSubscriptionAsync(string definitionId, string integrationId)
    {
        _db.NotificationSubscriptions.Add(new NotificationSubscriptionEntity
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
        });
        await _db.SaveChangesAsync();
    }

    public async Task AddUserPreferenceAsync(string definitionId, string userId, string channel = NotificationChannels.Toast)
    {
        _db.NotificationUserPreferences.Add(new NotificationUserPreferenceEntity
        {
            Id = $"nupref_{NextSequence()}",
            UserId = userId,
            NotificationDefinitionId = definitionId,
            Channel = channel,
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedByActorId = "test-actor",
        });
        await _db.SaveChangesAsync();
    }

    public async Task AddUserOverrideAsync(string definitionId, string userId)
    {
        _db.NotificationSubscriptionUserOverrides.Add(new NotificationSubscriptionUserOverrideEntity
        {
            Id = $"nover_{NextSequence()}",
            UserId = userId,
            NotificationDefinitionId = definitionId,
            OperationalTeamId = null,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByActorId = "test-actor",
        });
        await _db.SaveChangesAsync();
    }

    public async Task AddNotificationInstanceAsync(string definitionId, string integrationId)
    {
        var sequence = NextSequence();
        _db.NotificationInstances.Add(new NotificationInstanceEntity
        {
            Id = $"ninst_{sequence}",
            NotificationDefinitionId = definitionId,
            NotificationKind = "activation",
            Status = "sent",
            PlatformEventId = null,
            AttentionIncidentId = null,
            IntegrationId = integrationId,
            ModuleId = null,
            RunId = null,
            ScheduleId = null,
            ResourceContextId = null,
            InstanceDedupeKey = $"dedupe-{sequence}",
            RenderedSubject = "Subject",
            RenderedSummary = "Summary",
            RenderedBodyText = "Body",
            RenderedBodyHtml = null,
            RenderedActionText = null,
            RenderedActionUrl = null,
            RenderedContentFieldsJson = null,
            TemplateVersionHash = "hash",
            RenderContextJson = "{}",
            RecipientSnapshotJson = null,
            SuppressionReason = null,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    private int NextSequence() => System.Threading.Interlocked.Increment(ref _nextSequence);

    public void Dispose() => _db.Dispose();

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

    /// <summary>
    /// Grants the instance-level (action, resourceType) permission check, unless configured to deny
    /// <c>notification.definition.manage</c> specifically (for tests proving the platform-defaults
    /// mutation path is actually gated by that permission, not merely wired to always-allow).
    /// </summary>
    private sealed class ConfigurableAuthorizationService : IAuthorizationService
    {
        private readonly bool _denyInstanceManage;

        public ConfigurableAuthorizationService(bool denyInstanceManage) =>
            _denyInstanceManage = denyInstanceManage;

        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null,
            CancellationToken cancellationToken = default)
        {
            if (_denyInstanceManage && string.Equals(permission.Action, ActionConstants.NotificationDefinitionManage, StringComparison.Ordinal))
                return ValueTask.FromResult(false);
            return ValueTask.FromResult(true);
        }
    }

    /// <summary>
    /// Mirrors <see cref="NotificationDefinitionAuthorization"/>'s Integration-scoped gate
    /// (<c>integration.update</c>): denies it when configured, grants everything else.
    /// </summary>
    private sealed class ConfigurableAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        private readonly bool _denyIntegrationUpdate;

        private readonly Action? _onIntegrationUpdate;

        public ConfigurableAuthorizationEvaluationService(bool denyIntegrationUpdate, Action? onIntegrationUpdate = null)
        {
            _denyIntegrationUpdate = denyIntegrationUpdate;
            _onIntegrationUpdate = onIntegrationUpdate;
        }

        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            if (string.Equals(context.Permission.Action, ActionConstants.IntegrationUpdate, StringComparison.Ordinal))
            {
                _onIntegrationUpdate?.Invoke();
                if (_denyIntegrationUpdate)
                    return ValueTask.FromResult(false);
            }

            return ValueTask.FromResult(true);
        }
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
            throw new NotSupportedException("Not exercised by NotificationDefinitionTestHarness.");

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by NotificationDefinitionTestHarness.");

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by NotificationDefinitionTestHarness.");
    }

    /// <summary>Never exercised by DeleteAsync's tests -- always succeeds so nothing here masks a real validation bug elsewhere.</summary>
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
            throw new NotSupportedException("Not exercised by NotificationDefinitionTestHarness.");
    }

    private sealed class NoOpRenderContextFactory : INotificationRenderContextFactory
    {
        public NotificationRenderContext FromSample(NotificationRenderSample sample) =>
            throw new NotSupportedException("Not exercised by NotificationDefinitionTestHarness.");
    }
}
