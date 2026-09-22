using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.Notifications.Inbox;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Notifications;
using Apperture.ControlPlane.Security;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.UnitTests.Notifications.Instances;

public sealed class NotificationInstanceCoordinatorTests
{
    public static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    public static async Task SeedBuiltInDefinitionAsync(AppertureDbContext db)
    {
        var builtin = ExpectedRunMissingNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);
        db.NotificationDefinitions.Add(new NotificationDefinitionEntity
        {
            Id = builtin.Id.Value,
            DisplayName = builtin.DisplayName,
            Description = builtin.Description,
            SourceKind = builtin.SourceKind,
            Origin = builtin.Origin,
            Scope = builtin.Scope,
            Mutability = builtin.Mutability,
            PrimaryEventType = builtin.PrimaryEventType,
            IsEnabled = builtin.IsEnabled,
            DefaultSeverity = builtin.DefaultSeverity,
            RuleJson = builtin.RuleJson,
            TemplateJson = builtin.TemplateJson,
            CreatedAtUtc = builtin.CreatedAtUtc,
            UpdatedAtUtc = builtin.UpdatedAtUtc,
        });
        await db.SaveChangesAsync();
    }

    public static NotificationInstanceCoordinator CreateCoordinator(
        AppertureDbContext db,
        IAttentionRecipientResolver? attentionResolver = null,
        IAttentionIncidentTracker? incidentTracker = null,
        NotificationLiveRenderHints? liveRenderHints = null)
    {
        var instanceStore = new SqlNotificationInstanceStore(db);
        var deliveryOutbox = new SqlNotificationDeliveryOutboxStore(db);
        var definitionStore = new SqlNotificationDefinitionStore(db);
        var subscriptionStore = new SqlNotificationSubscriptionStore(db);
        var teamExpander = new OperationalTeamRecipientExpander(db);
        var recipientResolver = new SqlNotificationRecipientResolver(
            db,
            subscriptionStore,
            definitionStore,
            teamExpander,
            new SqlNotificationUserPreferenceStore(db));

        return new NotificationInstanceCoordinator(
            definitionStore,
            new NotificationRenderContextBuilder(),
            new StubLiveRenderContextResolver(liveRenderHints),
            new NotificationTemplateRenderer(),
            recipientResolver,
            instanceStore,
            deliveryOutbox,
            attentionResolver ?? new StubAttentionRecipientResolver(),
            incidentTracker ?? new StubIncidentTracker(),
            new NoOpRedactionPolicy(),
            new NoOpUserInboxService(),
            Options.Create(new NotificationInstanceOptions { Enabled = true }),
            NullLogger<NotificationInstanceCoordinator>.Instance);
    }

    internal sealed class StubLiveRenderContextResolver : INotificationLiveRenderContextResolver
    {
        private readonly NotificationLiveRenderHints _hints;

        public StubLiveRenderContextResolver(NotificationLiveRenderHints? hints = null) =>
            _hints = hints ?? new NotificationLiveRenderHints(null, "");

        public ValueTask<NotificationLiveRenderHints> ResolveAsync(string? integrationId, CancellationToken cancellationToken = default) =>
            new(_hints);
    }

    internal sealed class NoOpUserInboxService : INotificationUserInboxService
    {
        public ValueTask EnsureInboxRowsForUserAsync(string principalUserId, CancellationToken cancellationToken = default) => default;
        public ValueTask MaterializeForInstanceAsync(string notificationInstanceId, string? recipientSnapshotJson, IReadOnlyList<NotificationOutboxRecipientRef> outboxRows, CancellationToken cancellationToken = default) => default;
        public ValueTask<IReadOnlyList<NotificationUserInboxItem>> ListForUserAsync(string principalUserId, PersonalNotificationListQuery query, CancellationToken cancellationToken = default) => new([]);
        public ValueTask<int> GetUnreadCountAsync(string principalUserId, CancellationToken cancellationToken = default) => new(0);
        public ValueTask<bool> MarkReadAsync(string principalUserId, string inboxItemId, CancellationToken cancellationToken = default) => new(false);
        public ValueTask<bool> MarkUnreadAsync(string principalUserId, string inboxItemId, CancellationToken cancellationToken = default) => new(false);
        public ValueTask<int> MarkAllReadAsync(string principalUserId, CancellationToken cancellationToken = default) => new(0);
        public ValueTask RecordMenuViewedAsync(string principalUserId, int take, CancellationToken cancellationToken = default) => default;
        public ValueTask RecordPageViewedAsync(string principalUserId, IReadOnlyList<string> inboxItemIds, CancellationToken cancellationToken = default) => default;
    }

    private static NotificationMatchResult Matched(string definitionId, string kind) =>
        new(definitionId, NotificationMatchStatus.Matched, kind, kind, null, new NotificationRenderContext([]));

    [Fact]
    public async Task Activation_creates_instance_once_per_incident()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        var sut = CreateCoordinator(db);
        var defId = NotificationDefinitionIds.PlatformScheduleExpectedRunMissing;
        var envelope = NotificationEvaluationTestHelpers.SampleMissingEvent("int-1", "inc-1");
        var context = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.PlatformEvent,
            envelope,
            null,
            envelope.RecordedAtUtc);

        await sut.TryCreateFromMatchAsync(Matched(defId, NotificationKinds.Activation), context);
        await sut.TryCreateFromMatchAsync(Matched(defId, NotificationKinds.Activation), context);

        db.NotificationInstances.Count().Should().Be(1);
        db.NotificationDeliveryOutbox.Count().Should().Be(0);
    }

    [Fact]
    public async Task Activation_renders_schedule_diagnostics_action_url_and_live_render_context()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        var hints = new NotificationLiveRenderHints("Campus Nexus Prod", "https://mgmt.example.com");
        var sut = CreateCoordinator(db, liveRenderHints: hints);
        var defId = NotificationDefinitionIds.PlatformScheduleExpectedRunMissing;
        var envelope = NotificationEvaluationTestHelpers.SampleMissingEvent("int-1", "inc-1");
        var context = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.PlatformEvent,
            envelope,
            null,
            envelope.RecordedAtUtc);

        await sut.TryCreateFromMatchAsync(Matched(defId, NotificationKinds.Activation), context);

        var instance = db.NotificationInstances.Single();
        instance.RenderedActionUrl.Should().Be("/integrations/int-1/schedule-diagnostics");
        instance.RenderedActionText.Should().Be("Review schedule diagnostics");
        instance.RenderedSummary.Should().Contain("Campus Nexus Prod");
        instance.RenderContextJson.Should().Contain("https://mgmt.example.com");
        instance.RenderContextJson.Should().Contain("Campus Nexus Prod");
    }

    [Fact]
    public async Task Resolved_creates_instance_once_per_incident()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        var sut = CreateCoordinator(db);
        var defId = NotificationDefinitionIds.PlatformScheduleExpectedRunMissing;
        var envelope = NotificationEvaluationTestHelpers.SampleClearedEvent("int-1", "inc-1");
        var context = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.PlatformEvent,
            envelope,
            null,
            envelope.RecordedAtUtc);

        await sut.TryCreateFromMatchAsync(Matched(defId, NotificationKinds.Resolved), context);
        await sut.TryCreateFromMatchAsync(Matched(defId, NotificationKinds.Resolved), context);

        db.NotificationInstances.Count().Should().Be(1);
    }

    [Fact]
    public async Task Reminder_suppressed_when_interval_not_elapsed()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        var interval = TimeSpan.FromHours(1);
        var attention = new StubAttentionRecipientResolver(interval);
        var sut = CreateCoordinator(db, attention);
        var defId = NotificationDefinitionIds.PlatformScheduleExpectedRunMissing;
        var now = DateTimeOffset.UtcNow;

        db.NotificationInstances.Add(new NotificationInstanceEntity
        {
            Id = "ninst_existing",
            NotificationDefinitionId = defId,
            NotificationKind = NotificationKinds.Activation,
            Status = NotificationInstanceStatuses.Pending,
            AttentionIncidentId = "inc-1",
            IntegrationId = "int-1",
            InstanceDedupeKey = "existing",
            RenderedSubject = "s",
            RenderedSummary = "s",
            RenderedBodyText = "b",
            TemplateVersionHash = "h",
            RenderContextJson = "{}",
            CreatedAtUtc = now.AddMinutes(-10),
            UpdatedAtUtc = now.AddMinutes(-10),
        });
        await db.SaveChangesAsync();

        var incidentContext = new IncidentEvaluationContext(
            "int-1",
            AttentionIncidentObserveTransition.ObservedStillActive,
            "inc-1",
            null,
            now);
        var evalContext = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.IncidentTransition,
            null,
            incidentContext,
            now);

        await sut.TryCreateFromMatchAsync(Matched(defId, NotificationKinds.Reminder), evalContext);

        db.NotificationInstances.Count(x => x.NotificationKind == NotificationKinds.Reminder).Should().Be(0);
    }

    [Fact]
    public async Task Reminder_suppressed_when_acknowledged()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        var interval = TimeSpan.FromMinutes(5);
        var attention = new StubAttentionRecipientResolver(interval, suppressWhenAck: true);
        var tracker = new StubIncidentTracker(acknowledged: true);
        var sut = CreateCoordinator(db, attention, tracker);
        var defId = NotificationDefinitionIds.PlatformScheduleExpectedRunMissing;
        var now = DateTimeOffset.UtcNow.AddHours(-2);

        var incidentContext = new IncidentEvaluationContext(
            "int-1",
            AttentionIncidentObserveTransition.ObservedStillActive,
            "inc-1",
            null,
            now);
        var evalContext = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.IncidentTransition,
            null,
            incidentContext,
            DateTimeOffset.UtcNow);

        await sut.TryCreateFromMatchAsync(Matched(defId, NotificationKinds.Reminder), evalContext);

        db.NotificationInstances.Should().BeEmpty();
    }

    [Fact]
    public async Task Creates_outbox_for_deliverable_principal_subscription()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        db.Users.Add(new UserEntity { Id = "user-1", DisplayName = "Owner", EmailAddress = "owner@test.com" });
        await db.SaveChangesAsync();

        var subStore = new SqlNotificationSubscriptionStore(db);
        await subStore.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));

        var sut = CreateCoordinator(db);
        var envelope = NotificationEvaluationTestHelpers.SampleMissingEvent("int-1", "inc-1");
        var context = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.PlatformEvent,
            envelope,
            null,
            envelope.RecordedAtUtc);

        await sut.TryCreateFromMatchAsync(
            Matched(NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, NotificationKinds.Activation),
            context);

        var instance = await db.NotificationInstances.SingleAsync();
        instance.RenderedSubject.Should().Contain("expected_run_missing");
        instance.RenderContextJson.Should().NotBeNullOrWhiteSpace();
        instance.RecipientSnapshotJson.Should().Contain("owner@test.com");

        var outbox = await db.NotificationDeliveryOutbox.ToListAsync();
        outbox.Should().ContainSingle(x => x.RecipientEmail == "owner@test.com" && x.Status == NotificationOutboxStatuses.Pending);
    }

    [Fact]
    public async Task No_outbox_when_user_globally_opted_out()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        db.Users.Add(new UserEntity { Id = "user-1", DisplayName = "Owner", EmailAddress = "owner@test.com" });
        await db.SaveChangesAsync();

        var subStore = new SqlNotificationSubscriptionStore(db);
        await subStore.AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.UserSelfSubscribed,
                true,
                "user-1"));
        await subStore.SetUserOptOutAsync(
            new NotificationSubscriptionUserOverrideCommand(
                "user-1",
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                "user-1"));

        var sut = CreateCoordinator(db);
        var envelope = NotificationEvaluationTestHelpers.SampleMissingEvent("int-1", "inc-1");
        var context = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.PlatformEvent,
            envelope,
            null,
            envelope.RecordedAtUtc);

        await sut.TryCreateFromMatchAsync(
            Matched(NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, NotificationKinds.Activation),
            context);

        db.NotificationDeliveryOutbox.Should().BeEmpty();
        var instance = await db.NotificationInstances.SingleAsync();
        instance.RecipientSnapshotJson.Should().Contain(NotificationRecipientSkipReasons.UserOptOut);
    }

    [Fact]
    public async Task Implicit_routing_recipient_in_snapshot_when_no_team_subscription()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        const string teamId = "team-route";
        db.OperationalTeams.Add(new OperationalTeamEntity
        {
            Id = teamId,
            Name = "Route",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        db.OperationalTeamMembers.Add(new OperationalTeamMemberEntity
        {
            Id = "m1",
            OperationalTeamId = teamId,
            MemberKind = "ExternalEmail",
            EmailAddress = "routing@test.com",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        db.InstanceAttentionRoutings.Add(new InstanceAttentionRoutingEntity
        {
            Id = InstanceAttentionRoutingDefaults.RowId,
            ConditionKey = AttentionConditionKeys.ExpectedRunMissing,
            IsEnabled = true,
            PrimaryOperationalTeamId = teamId,
        });
        db.IntegrationAttentionRoutings.Add(new IntegrationAttentionRoutingEntity
        {
            Id = "ir1",
            IntegrationId = "int-1",
            ConditionKey = AttentionConditionKeys.ExpectedRunMissing,
            IsEnabled = true,
            PrimaryOperationalTeamId = teamId,
        });
        await db.SaveChangesAsync();

        var sut = CreateCoordinator(db);
        var envelope = NotificationEvaluationTestHelpers.SampleMissingEvent("int-1", "inc-1");
        var context = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.PlatformEvent,
            envelope,
            null,
            envelope.RecordedAtUtc);

        await sut.TryCreateFromMatchAsync(
            Matched(NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, NotificationKinds.Activation),
            context);

        var instance = await db.NotificationInstances.SingleAsync();
        instance.RecipientSnapshotJson.Should().Contain(NotificationRecipientSources.AttentionRoutingIntegrationOverride);
        (await db.NotificationDeliveryOutbox.CountAsync()).Should().Be(1);
    }

    internal sealed class StubAttentionRecipientResolver : IAttentionRecipientResolver
    {
        private readonly TimeSpan? _interval;
        private readonly bool _suppressWhenAck;
        private readonly TimeSpan? _escalationAfter;
        private readonly IReadOnlyList<string> _escalationRecipients;
        private readonly IReadOnlyList<string> _primaryRecipients;
        private readonly bool _suppressEscalationWhenAck;
        private readonly bool _enabled;

        public StubAttentionRecipientResolver(
            TimeSpan? interval = null,
            bool suppressWhenAck = false,
            TimeSpan? escalationAfter = null,
            IReadOnlyList<string>? escalationRecipients = null,
            IReadOnlyList<string>? primaryRecipients = null,
            bool suppressEscalationWhenAck = false,
            bool enabled = true)
        {
            _interval = interval ?? TimeSpan.FromHours(1);
            _suppressWhenAck = suppressWhenAck;
            _escalationAfter = escalationAfter;
            _escalationRecipients = escalationRecipients ?? [];
            _primaryRecipients = primaryRecipients ?? [];
            _suppressEscalationWhenAck = suppressEscalationWhenAck;
            _enabled = enabled;
        }

        public ValueTask<AttentionRecipientResolution> ResolveInstanceDefaultsAsync(
            string conditionKey,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask<AttentionRecipientResolution> ResolveAsync(
            IntegrationId integrationId,
            string conditionKey,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                new AttentionRecipientResolution(
                    true,
                    _enabled,
                    _primaryRecipients,
                    AttentionRecipientSources.IntegrationOverride,
                    _escalationRecipients,
                    _escalationRecipients.Count > 0
                        ? AttentionRecipientSources.IntegrationOverride
                        : AttentionRecipientSources.None,
                    null,
                    null,
                    null,
                    null,
                    _primaryRecipients.Count > 0,
                    _escalationRecipients.Count > 0,
                    null,
                    _escalationRecipients.Count > 0 ? null : AttentionNoRouteReasons.NoTeamConfigured,
                    [],
                    [],
                    _primaryRecipients,
                    _escalationRecipients,
                    _escalationAfter,
                    _interval,
                    true,
                    _suppressWhenAck,
                    _suppressEscalationWhenAck,
                    null,
                    null));
    }

    internal sealed class StubIncidentTracker : IAttentionIncidentTracker
    {
        private readonly bool _acknowledged;

        public StubIncidentTracker(bool acknowledged = false) => _acknowledged = acknowledged;

        public ValueTask<AttentionIncidentObserveResult> ObserveScheduleExpectationAsync(
            IntegrationId integrationId,
            ScheduleExpectationSnapshot snapshot,
            DateTimeOffset observedAtUtc,
            string? resourceContextId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask<IReadOnlySet<string>> GetIntegrationIdsWithActiveConditionAsync(
            string conditionKey,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public ValueTask<IReadOnlyList<AttentionIncidentProjection>> ListRecentForIntegrationAsync(
            IntegrationId integrationId,
            string conditionKey,
            int maxCount,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<AttentionIncidentProjection>>([]);

        public ValueTask<bool> AcknowledgeAsync(
            IntegrationId integrationId,
            string incidentId,
            string acknowledgedBy,
            string? note,
            DateTimeOffset acknowledgedAtUtc,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);

        public ValueTask<AttentionIncidentProjection?> GetIncidentAsync(
            IntegrationId integrationId,
            string incidentId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AttentionIncidentProjection?>(
                new AttentionIncidentProjection(
                    incidentId,
                    integrationId.Value,
                    AttentionConditionKeys.ExpectedRunMissing,
                    "Active",
                    DateTimeOffset.UtcNow.AddHours(-1),
                    DateTimeOffset.UtcNow,
                    null,
                    null,
                    null,
                    null,
                    _acknowledged ? DateTimeOffset.UtcNow : null,
                    null,
                    null));
    }
}
