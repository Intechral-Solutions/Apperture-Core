using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;
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

public sealed class NotificationInstanceCoordinatorParityTests
{
    private static AppertureDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task Activation_suppressed_when_route_disabled()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        var attention = new ConfigurableAttentionResolver(enabled: false);
        var sut = NotificationInstanceCoordinatorTestsHelper.CreateCoordinator(db, attention);

        await sut.TryCreateFromMatchAsync(
            Matched(NotificationKinds.Activation),
            Context(NotificationEvaluationTestHelpers.SampleMissingEvent("int-1", "inc-1")));

        var instance = await db.NotificationInstances.SingleAsync();
        instance.Status.Should().Be(NotificationInstanceStatuses.Suppressed);
        instance.SuppressionReason.Should().Be(AttentionNotificationSuppressionReasons.PolicyDisabled);
        (await db.NotificationDeliveryOutbox.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Resolved_suppressed_when_SendResolvedNotification_false()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        var attention = new ConfigurableAttentionResolver(sendResolved: false);
        var sut = NotificationInstanceCoordinatorTestsHelper.CreateCoordinator(db, attention);

        await sut.TryCreateFromMatchAsync(
            Matched(NotificationKinds.Resolved),
            Context(NotificationEvaluationTestHelpers.SampleClearedEvent("int-1", "inc-1")));

        var instance = await db.NotificationInstances.SingleAsync();
        instance.SuppressionReason.Should().Be(AttentionNotificationSuppressionReasons.ResolvedNotificationDisabled);
    }

    [Fact]
    public async Task Resolved_suppressed_when_no_prior_successful_activation()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        var sut = NotificationInstanceCoordinatorTestsHelper.CreateCoordinator(db);

        await sut.TryCreateFromMatchAsync(
            Matched(NotificationKinds.Resolved),
            Context(NotificationEvaluationTestHelpers.SampleClearedEvent("int-1", "inc-1")));

        var instance = await db.NotificationInstances.SingleAsync();
        instance.SuppressionReason.Should().Be(AttentionNotificationSuppressionReasons.NoPriorActivationForResolved);
    }

    [Fact]
    public async Task Resolved_allowed_when_prior_activation_succeeded_in_outbox()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        db.Users.Add(new UserEntity { Id = "u1", DisplayName = "U", EmailAddress = "u@test.com" });
        await db.SaveChangesAsync();
        await new SqlNotificationSubscriptionStore(db).AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "u1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));

        var now = DateTimeOffset.UtcNow;
        var activationInstanceId = NotificationInstanceId.New().Value;
        db.NotificationInstances.Add(new NotificationInstanceEntity
        {
            Id = activationInstanceId,
            NotificationDefinitionId = NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            NotificationKind = NotificationKinds.Activation,
            Status = NotificationInstanceStatuses.Pending,
            AttentionIncidentId = "inc-1",
            IntegrationId = "int-1",
            InstanceDedupeKey = "act",
            RenderedSubject = "s",
            RenderedSummary = "s",
            RenderedBodyText = "b",
            TemplateVersionHash = "h",
            RenderContextJson = "{}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        db.NotificationDeliveryOutbox.Add(new NotificationDeliveryOutboxEntity
        {
            Id = NotificationDeliveryOutboxId.New().Value,
            NotificationInstanceId = activationInstanceId,
            RecipientEmail = "u@test.com",
            Channel = NotificationChannels.Email,
            Status = NotificationOutboxStatuses.Succeeded,
            SentAtUtc = now.AddHours(-1),
            AttemptCount = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();

        var sut = NotificationInstanceCoordinatorTestsHelper.CreateCoordinator(db);
        await sut.TryCreateFromMatchAsync(
            Matched(NotificationKinds.Resolved),
            Context(NotificationEvaluationTestHelpers.SampleClearedEvent("int-1", "inc-1")));

        (await db.NotificationInstances.CountAsync(x => x.NotificationKind == NotificationKinds.Resolved)).Should().Be(1);
        var resolved = await db.NotificationInstances.SingleAsync(x => x.NotificationKind == NotificationKinds.Resolved);
        resolved.Status.Should().Be(NotificationInstanceStatuses.Pending);
        resolved.SuppressionReason.Should().BeNull();
    }

    [Fact]
    public async Task Activation_suppressed_with_no_route_when_no_deliverable_recipients()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        var sut = NotificationInstanceCoordinatorTestsHelper.CreateCoordinator(db);

        await sut.TryCreateFromMatchAsync(
            Matched(NotificationKinds.Activation),
            Context(NotificationEvaluationTestHelpers.SampleMissingEvent("int-1", "inc-1")));

        var instance = await db.NotificationInstances.SingleAsync();
        instance.SuppressionReason.Should().Be(AttentionNotificationSuppressionReasons.NoRouteConfigured);
        (await db.NotificationDeliveryOutbox.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Activation_not_suppressed_when_InApp_eligible_recipient_exists_without_email()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        db.Users.Add(new UserEntity { Id = "user-1", DisplayName = "No Email", EmailAddress = null });
        await db.SaveChangesAsync();
        await new SqlNotificationSubscriptionStore(db).AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));

        var sut = NotificationInstanceCoordinatorTestsHelper.CreateCoordinator(db);

        await sut.TryCreateFromMatchAsync(
            Matched(NotificationKinds.Activation),
            Context(NotificationEvaluationTestHelpers.SampleMissingEvent("int-1", "inc-1")));

        var instance = await db.NotificationInstances.SingleAsync();
        instance.Status.Should().Be(NotificationInstanceStatuses.Pending);
        instance.SuppressionReason.Should().BeNull();
        (await db.NotificationDeliveryOutbox.CountAsync()).Should().Be(0);

        // End-to-end JSON round-trip: real resolver -> real coordinator serialization -> real extractor
        // deserialization. This proves the "inApp" property name and camelCase naming policy actually
        // line up between SerializeRecipientSnapshot and ExtractPrincipalRecipients, not just that each
        // stage behaves correctly in isolation against handcrafted JSON.
        instance.RecipientSnapshotJson.Should().Contain("\"inApp\"");
        var extracted = NotificationUserInboxRecipientExtractor.ExtractPrincipalRecipients(instance.RecipientSnapshotJson);
        extracted.Should().ContainSingle(r => r.UserId == "user-1");
    }

    [Fact]
    public async Task Reminder_requires_prior_successful_send_for_interval()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        var interval = TimeSpan.FromHours(1);
        var attention = new ConfigurableAttentionResolver(reminderInterval: interval);
        var sut = NotificationInstanceCoordinatorTestsHelper.CreateCoordinator(db, attention);
        var now = DateTimeOffset.UtcNow;

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

        await sut.TryCreateFromMatchAsync(Matched(NotificationKinds.Reminder), evalContext);

        (await db.NotificationInstances.CountAsync(x => x.NotificationKind == NotificationKinds.Reminder)).Should().Be(0);
    }

    [Fact]
    public async Task Reminder_allowed_when_interval_elapsed_since_last_successful_send()
    {
        await using var db = CreateDb();
        await SeedBuiltInDefinitionAsync(db);
        db.Users.Add(new UserEntity { Id = "u1", DisplayName = "U", EmailAddress = "u@test.com" });
        await db.SaveChangesAsync();
        await new SqlNotificationSubscriptionStore(db).AddAsync(
            new NotificationSubscriptionCreateCommand(
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                null,
                NotificationRecipientKinds.Principal,
                "u1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));

        var interval = TimeSpan.FromHours(1);
        var attention = new ConfigurableAttentionResolver(reminderInterval: interval);
        var now = DateTimeOffset.UtcNow;
        var activationInstanceId = NotificationInstanceId.New().Value;
        db.NotificationInstances.Add(new NotificationInstanceEntity
        {
            Id = activationInstanceId,
            NotificationDefinitionId = NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            NotificationKind = NotificationKinds.Activation,
            Status = NotificationInstanceStatuses.Pending,
            AttentionIncidentId = "inc-1",
            IntegrationId = "int-1",
            InstanceDedupeKey = "act",
            RenderedSubject = "s",
            RenderedSummary = "s",
            RenderedBodyText = "b",
            TemplateVersionHash = "h",
            RenderContextJson = "{}",
            CreatedAtUtc = now.AddHours(-2),
            UpdatedAtUtc = now.AddHours(-2),
        });
        db.NotificationDeliveryOutbox.Add(new NotificationDeliveryOutboxEntity
        {
            Id = NotificationDeliveryOutboxId.New().Value,
            NotificationInstanceId = activationInstanceId,
            RecipientEmail = "u@test.com",
            Channel = NotificationChannels.Email,
            Status = NotificationOutboxStatuses.Succeeded,
            SentAtUtc = now.AddHours(-2),
            AttemptCount = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();

        var sut = NotificationInstanceCoordinatorTestsHelper.CreateCoordinator(db, attention);
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

        await sut.TryCreateFromMatchAsync(Matched(NotificationKinds.Reminder), evalContext);

        (await db.NotificationInstances.CountAsync(x => x.NotificationKind == NotificationKinds.Reminder)).Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_outbox_insert_is_idempotent()
    {
        await using var db = CreateDb();
        var instanceId = NotificationInstanceId.New();
        var store = new SqlNotificationInstanceStore(db);
        var cmd = new NotificationDeliveryOutboxCreateCommand(
            "a@test.com",
            null,
            null,
            null,
            NotificationChannels.Email,
            NotificationOutboxStatuses.Pending);

        await store.AddOutboxRowsAsync(instanceId, [cmd]);
        var act = async () => await store.AddOutboxRowsAsync(instanceId, [cmd]);
        await act.Should().NotThrowAsync();
        (await db.NotificationDeliveryOutbox.CountAsync()).Should().Be(1);
    }

    private static NotificationMatchResult Matched(string kind) =>
        new(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            NotificationMatchStatus.Matched,
            kind,
            kind,
            null,
            new NotificationRenderContext([]));

    private static NotificationRuleEvaluationContext Context(PlatformEventEnvelope envelope) =>
        new(NotificationEvaluationSourceKind.PlatformEvent, envelope, null, envelope.RecordedAtUtc);

    private static async Task SeedBuiltInDefinitionAsync(AppertureDbContext db)
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

    private sealed class ConfigurableAttentionResolver : IAttentionRecipientResolver
    {
        private readonly bool _enabled;
        private readonly bool _sendResolved;
        private readonly TimeSpan? _reminderInterval;

        public ConfigurableAttentionResolver(
            bool enabled = true,
            bool sendResolved = true,
            TimeSpan? reminderInterval = null)
        {
            _enabled = enabled;
            _sendResolved = sendResolved;
            _reminderInterval = reminderInterval ?? TimeSpan.FromHours(1);
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
                    [],
                    AttentionRecipientSources.IntegrationOverride,
                    [],
                    AttentionRecipientSources.None,
                    null,
                    null,
                    null,
                    null,
                    false,
                    false,
                    null,
                    null,
                    [],
                    [],
                    [],
                    [],
                    null,
                    _reminderInterval,
                    _sendResolved,
                    true,
                    false,
                    null,
                    null));
    }
}

internal static class NotificationInstanceCoordinatorTestsHelper
{
    public static NotificationInstanceCoordinator CreateCoordinator(
        AppertureDbContext db,
        IAttentionRecipientResolver? attentionResolver = null,
        IAttentionIncidentTracker? incidentTracker = null)
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
            new NotificationInstanceCoordinatorTests.StubLiveRenderContextResolver(),
            new NotificationTemplateRenderer(),
            recipientResolver,
            instanceStore,
            deliveryOutbox,
            attentionResolver ?? new NotificationInstanceCoordinatorTests.StubAttentionRecipientResolver(),
            incidentTracker ?? new NotificationInstanceCoordinatorTests.StubIncidentTracker(),
            new NoOpRedactionPolicy(),
            new NotificationInstanceCoordinatorTests.NoOpUserInboxService(),
            Options.Create(new NotificationInstanceOptions { Enabled = true }),
            NullLogger<NotificationInstanceCoordinator>.Instance);
    }
}
