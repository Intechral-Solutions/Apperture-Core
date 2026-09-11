using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications.Instances;

public sealed class ExpectedRunMissingPlatformLifecycleTests
{
    private static readonly DateTimeOffset Started = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Expected_run_missing_lifecycle_uses_platform_pipeline_only()
    {
        await using var db = NotificationInstanceCoordinatorTests.CreateDb();
        await NotificationInstanceCoordinatorTests.SeedBuiltInDefinitionAsync(db);
        db.Users.Add(new UserEntity { Id = "u1", DisplayName = "Primary", EmailAddress = "primary@test.com" });
        db.Users.Add(new UserEntity { Id = "u2", DisplayName = "Resolved-2", EmailAddress = "resolved-b@test.com" });
        await db.SaveChangesAsync();
        var subs = new SqlNotificationSubscriptionStore(db);
        await subs.AddAsync(PrincipalSub("u1"));
        await subs.AddAsync(PrincipalSub("u2"));

        var attention = new NotificationInstanceCoordinatorTests.StubAttentionRecipientResolver(
            interval: TimeSpan.FromHours(1),
            escalationAfter: TimeSpan.FromHours(4),
            escalationRecipients: ["esc@test.com"],
            primaryRecipients: ["primary@test.com"]);
        var tracker = new NotificationInstanceCoordinatorEscalationTests.TimedIncidentTracker(Started);
        var sut = NotificationInstanceCoordinatorTests.CreateCoordinator(db, attention, tracker);

        await sut.TryCreateFromMatchAsync(
            Matched(NotificationKinds.Activation),
            EventContext(NotificationEvaluationTestHelpers.SampleMissingEvent("int-1", "inc-1")));
        await sut.TryCreateFromMatchAsync(
            Matched(NotificationKinds.Activation),
            EventContext(NotificationEvaluationTestHelpers.SampleMissingEvent("int-1", "inc-1")));

        var activation = await db.NotificationInstances.SingleAsync(x => x.NotificationKind == NotificationKinds.Activation);
        activation.Status.Should().Be(NotificationInstanceStatuses.Pending);
        (await db.NotificationDeliveryOutbox.CountAsync()).Should().Be(2);

        foreach (var row in db.NotificationDeliveryOutbox)
        {
            row.Status = NotificationOutboxStatuses.Succeeded;
            row.SentAtUtc = Started.AddMinutes(5);
            row.AttemptCount = 1;
        }
        await db.SaveChangesAsync();

        await sut.TryCreateFromMatchAsync(Matched(NotificationKinds.Reminder), StillActive(Started.AddMinutes(30)));
        await sut.TryCreateFromMatchAsync(Matched(NotificationKinds.Escalation), StillActive(Started.AddHours(2)));
        db.NotificationInstances.Should().NotContain(x => x.NotificationKind == NotificationKinds.Reminder);
        db.NotificationInstances.Should().NotContain(x => x.NotificationKind == NotificationKinds.Escalation);

        await sut.TryCreateFromMatchAsync(Matched(NotificationKinds.Reminder), StillActive(Started.AddHours(2)));
        (await db.NotificationInstances.CountAsync(x => x.NotificationKind == NotificationKinds.Reminder)).Should().Be(1);

        await sut.TryCreateFromMatchAsync(Matched(NotificationKinds.Escalation), StillActive(Started.AddHours(5)));
        await sut.TryCreateFromMatchAsync(Matched(NotificationKinds.Escalation), StillActive(Started.AddHours(6)));
        var escalation = await db.NotificationInstances.SingleAsync(x => x.NotificationKind == NotificationKinds.Escalation);
        escalation.Status.Should().Be(NotificationInstanceStatuses.Pending);
        var escOutbox = await db.NotificationDeliveryOutbox
            .Where(x => x.NotificationInstanceId == escalation.Id)
            .ToListAsync();
        escOutbox.Should().ContainSingle();
        escOutbox[0].RecipientEmail.Should().Be("esc@test.com");

        escOutbox[0].Status = NotificationOutboxStatuses.Failed;
        escOutbox[0].AttemptCount = 1;
        await db.SaveChangesAsync();
        await sut.TryCreateFromMatchAsync(Matched(NotificationKinds.Escalation), StillActive(Started.AddHours(8)));
        (await db.NotificationInstances.CountAsync(x => x.NotificationKind == NotificationKinds.Escalation)).Should().Be(1);
        (await db.NotificationDeliveryOutbox.CountAsync(x => x.NotificationInstanceId == escalation.Id)).Should().Be(1);

        await sut.TryCreateFromMatchAsync(
            Matched(NotificationKinds.Resolved),
            EventContext(NotificationEvaluationTestHelpers.SampleClearedEvent("int-1", "inc-1")));
        var resolved = await db.NotificationInstances.SingleAsync(x => x.NotificationKind == NotificationKinds.Resolved);
        var resolvedOutbox = await db.NotificationDeliveryOutbox
            .Where(x => x.NotificationInstanceId == resolved.Id)
            .Select(x => x.RecipientEmail)
            .ToListAsync();
        resolvedOutbox.Should().BeEquivalentTo(["primary@test.com", "resolved-b@test.com"]);
    }

    [Fact]
    public async Task Acknowledgement_suppresses_escalation_in_lifecycle()
    {
        await using var db = NotificationInstanceCoordinatorTests.CreateDb();
        await NotificationInstanceCoordinatorTests.SeedBuiltInDefinitionAsync(db);
        var attention = new NotificationInstanceCoordinatorTests.StubAttentionRecipientResolver(
            interval: TimeSpan.FromHours(1),
            suppressWhenAck: true,
            escalationAfter: TimeSpan.FromHours(1),
            escalationRecipients: ["esc@test.com"],
            suppressEscalationWhenAck: true);
        var sut = NotificationInstanceCoordinatorTests.CreateCoordinator(
            db,
            attention,
            new NotificationInstanceCoordinatorEscalationTests.TimedIncidentTracker(Started, acknowledged: true));

        await sut.TryCreateFromMatchAsync(Matched(NotificationKinds.Escalation), StillActive(Started.AddHours(3)));

        var instance = await db.NotificationInstances.SingleAsync();
        instance.NotificationKind.Should().Be(NotificationKinds.Escalation);
        instance.Status.Should().Be(NotificationInstanceStatuses.Suppressed);
        instance.SuppressionReason.Should().Be(AttentionNotificationSuppressionReasons.EscalationSuppressedAcknowledged);
        db.NotificationDeliveryOutbox.Should().BeEmpty();
    }

    private static NotificationSubscriptionCreateCommand PrincipalSub(string userId) =>
        new(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            null,
            NotificationRecipientKinds.Principal,
            userId,
            NotificationDeliveryModes.Immediate,
            NotificationSubscriptionOrigins.AdminAssigned,
            true,
            null);

    private static NotificationMatchResult Matched(string kind) =>
        new(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            NotificationMatchStatus.Matched,
            kind,
            kind,
            null,
            new NotificationRenderContext([]));

    private static NotificationRuleEvaluationContext EventContext(PlatformEventEnvelope envelope) =>
        new(NotificationEvaluationSourceKind.PlatformEvent, envelope, null, envelope.RecordedAtUtc);

    private static NotificationRuleEvaluationContext StillActive(DateTimeOffset observedAt) =>
        new(
            NotificationEvaluationSourceKind.IncidentTransition,
            null,
            new IncidentEvaluationContext(
                "int-1",
                AttentionIncidentObserveTransition.ObservedStillActive,
                "inc-1",
                null,
                observedAt),
            observedAt);
}
