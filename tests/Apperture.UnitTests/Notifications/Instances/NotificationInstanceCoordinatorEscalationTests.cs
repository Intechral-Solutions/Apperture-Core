using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications.Instances;

public sealed class NotificationInstanceCoordinatorEscalationTests
{
    private static readonly DateTimeOffset Started = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Escalation_creates_one_instance_and_outbox_per_escalation_recipient()
    {
        await using var db = NotificationInstanceCoordinatorTests.CreateDb();
        await NotificationInstanceCoordinatorTests.SeedBuiltInDefinitionAsync(db);
        var attention = EscalationRoute(
            after: TimeSpan.FromHours(2),
            escalationEmails: ["esc-a@test.com", "esc-b@test.com"],
            primaryEmails: ["primary@test.com"]);
        var tracker = new TimedIncidentTracker(Started);
        var sut = NotificationInstanceCoordinatorTests.CreateCoordinator(db, attention, tracker);
        var observed = Started.AddHours(3);

        await sut.TryCreateFromMatchAsync(Matched(NotificationKinds.Escalation), StillActive(observed));
        await sut.TryCreateFromMatchAsync(Matched(NotificationKinds.Escalation), StillActive(observed.AddHours(1)));

        var instance = await db.NotificationInstances.SingleAsync();
        instance.NotificationKind.Should().Be(NotificationKinds.Escalation);
        instance.Status.Should().Be(NotificationInstanceStatuses.Pending);
        instance.RenderedSubject.Should().Contain("Escalation");
        instance.InstanceDedupeKey.Should().EndWith("|escalation");

        var outbox = await db.NotificationDeliveryOutbox.ToListAsync();
        outbox.Select(x => x.RecipientEmail).Should().BeEquivalentTo(["esc-a@test.com", "esc-b@test.com"]);
        outbox.Should().NotContain(x => x.RecipientEmail == "primary@test.com");
        outbox.Should().OnlyContain(x => x.Status == NotificationOutboxStatuses.Pending);
    }

    [Fact]
    public async Task Escalation_skips_when_interval_not_elapsed()
    {
        await using var db = NotificationInstanceCoordinatorTests.CreateDb();
        await NotificationInstanceCoordinatorTests.SeedBuiltInDefinitionAsync(db);
        var attention = EscalationRoute(after: TimeSpan.FromHours(4), escalationEmails: ["esc@test.com"]);
        var sut = NotificationInstanceCoordinatorTests.CreateCoordinator(
            db, attention, new TimedIncidentTracker(Started));

        await sut.TryCreateFromMatchAsync(Matched(NotificationKinds.Escalation), StillActive(Started.AddHours(1)));

        db.NotificationInstances.Should().BeEmpty();
        db.NotificationDeliveryOutbox.Should().BeEmpty();
    }

    [Fact]
    public async Task Escalation_suppressed_when_acknowledged()
    {
        await using var db = NotificationInstanceCoordinatorTests.CreateDb();
        await NotificationInstanceCoordinatorTests.SeedBuiltInDefinitionAsync(db);
        var attention = EscalationRoute(
            after: TimeSpan.FromHours(1),
            escalationEmails: ["esc@test.com"],
            suppressWhenAck: true);
        var sut = NotificationInstanceCoordinatorTests.CreateCoordinator(
            db, attention, new TimedIncidentTracker(Started, acknowledged: true));

        await sut.TryCreateFromMatchAsync(Matched(NotificationKinds.Escalation), StillActive(Started.AddHours(3)));

        var instance = await db.NotificationInstances.SingleAsync();
        instance.Status.Should().Be(NotificationInstanceStatuses.Suppressed);
        instance.SuppressionReason.Should().Be(AttentionNotificationSuppressionReasons.EscalationSuppressedAcknowledged);
        db.NotificationDeliveryOutbox.Should().BeEmpty();
    }

    [Fact]
    public async Task Escalation_suppressed_when_due_and_no_escalation_recipients()
    {
        await using var db = NotificationInstanceCoordinatorTests.CreateDb();
        await NotificationInstanceCoordinatorTests.SeedBuiltInDefinitionAsync(db);
        var attention = EscalationRoute(after: TimeSpan.FromHours(1), escalationEmails: []);
        var sut = NotificationInstanceCoordinatorTests.CreateCoordinator(
            db, attention, new TimedIncidentTracker(Started));

        await sut.TryCreateFromMatchAsync(Matched(NotificationKinds.Escalation), StillActive(Started.AddHours(3)));

        var instance = await db.NotificationInstances.SingleAsync();
        instance.Status.Should().Be(NotificationInstanceStatuses.Suppressed);
        instance.SuppressionReason.Should().Be(AttentionNotificationSuppressionReasons.NoRouteConfigured);
        db.NotificationDeliveryOutbox.Should().BeEmpty();
    }

    [Fact]
    public async Task Escalation_skips_when_routing_disabled()
    {
        await using var db = NotificationInstanceCoordinatorTests.CreateDb();
        await NotificationInstanceCoordinatorTests.SeedBuiltInDefinitionAsync(db);
        var attention = new NotificationInstanceCoordinatorTests.StubAttentionRecipientResolver(
            escalationAfter: TimeSpan.FromHours(1),
            escalationRecipients: ["esc@test.com"],
            enabled: false);
        var sut = NotificationInstanceCoordinatorTests.CreateCoordinator(
            db, attention, new TimedIncidentTracker(Started));

        await sut.TryCreateFromMatchAsync(Matched(NotificationKinds.Escalation), StillActive(Started.AddHours(3)));

        db.NotificationInstances.Should().BeEmpty();
        db.NotificationDeliveryOutbox.Should().BeEmpty();
    }

    [Fact]
    public async Task Escalation_skips_when_interval_not_configured()
    {
        await using var db = NotificationInstanceCoordinatorTests.CreateDb();
        await NotificationInstanceCoordinatorTests.SeedBuiltInDefinitionAsync(db);
        var attention = EscalationRoute(after: TimeSpan.Zero, escalationEmails: ["esc@test.com"]);
        var sut = NotificationInstanceCoordinatorTests.CreateCoordinator(
            db, attention, new TimedIncidentTracker(Started));

        await sut.TryCreateFromMatchAsync(Matched(NotificationKinds.Escalation), StillActive(Started.AddHours(3)));

        db.NotificationInstances.Should().BeEmpty();
        db.NotificationDeliveryOutbox.Should().BeEmpty();
    }

    [Fact]
    public async Task Failed_outbox_does_not_create_another_escalation_instance()
    {
        await using var db = NotificationInstanceCoordinatorTests.CreateDb();
        await NotificationInstanceCoordinatorTests.SeedBuiltInDefinitionAsync(db);
        var attention = EscalationRoute(after: TimeSpan.FromHours(1), escalationEmails: ["esc@test.com"]);
        var sut = NotificationInstanceCoordinatorTests.CreateCoordinator(
            db, attention, new TimedIncidentTracker(Started));
        var observed = Started.AddHours(3);

        await sut.TryCreateFromMatchAsync(Matched(NotificationKinds.Escalation), StillActive(observed));
        var row = await db.NotificationDeliveryOutbox.SingleAsync();
        row.Status = NotificationOutboxStatuses.Failed;
        row.AttemptCount = 1;
        await db.SaveChangesAsync();

        await sut.TryCreateFromMatchAsync(Matched(NotificationKinds.Escalation), StillActive(observed.AddHours(2)));

        (await db.NotificationInstances.CountAsync()).Should().Be(1);
        (await db.NotificationDeliveryOutbox.CountAsync()).Should().Be(1);
        (await db.NotificationDeliveryOutbox.SingleAsync()).Status.Should().Be(NotificationOutboxStatuses.Failed);
    }

    private static NotificationMatchResult Matched(string kind) =>
        new(
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            NotificationMatchStatus.Matched,
            kind,
            kind,
            null,
            new NotificationRenderContext([]));

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

    private static NotificationInstanceCoordinatorTests.StubAttentionRecipientResolver EscalationRoute(
        TimeSpan after,
        IReadOnlyList<string> escalationEmails,
        IReadOnlyList<string>? primaryEmails = null,
        bool suppressWhenAck = true) =>
        new(
            interval: TimeSpan.FromHours(1),
            suppressWhenAck: suppressWhenAck,
            escalationAfter: after,
            escalationRecipients: escalationEmails,
            primaryRecipients: primaryEmails ?? [],
            suppressEscalationWhenAck: suppressWhenAck);

    internal sealed class TimedIncidentTracker : IAttentionIncidentTracker
    {
        private readonly DateTimeOffset _startedAt;
        private readonly bool _acknowledged;

        public TimedIncidentTracker(DateTimeOffset startedAt, bool acknowledged = false)
        {
            _startedAt = startedAt;
            _acknowledged = acknowledged;
        }

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
                    _startedAt,
                    _startedAt,
                    null,
                    null,
                    null,
                    null,
                    _acknowledged ? _startedAt.AddMinutes(5) : null,
                    null,
                    null));
    }
}
