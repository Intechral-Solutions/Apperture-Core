using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Integration;

public sealed class AttentionIncidentNotificationOrchestratorTests
{
    private static readonly IntegrationId IntId = new("int-e69");

    private sealed class MutableUtcTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNowValue { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNowValue;
    }

    private static ScheduleExpectationSnapshot Snapshot(ScheduleExpectationEvaluationStatus status, DateTimeOffset? lastFire = null) =>
        new(
            new ScheduleExpectationEvaluation(
                ExpectsRecurringRuns: true,
                HasExpectedFireInWindow: true,
                ExpectedFireCountInWindow: 1,
                MostRecentExpectedFireUtc: lastFire,
                InGracePeriod: false,
                SatisfiedByAuthorizedScheduleOriginRun: false,
                LastSatisfyingScheduleOriginRunId: null,
                LastSatisfyingScheduleOriginRunAt: null,
                Status: status,
                OperatorSummaryLine: "test"),
            ReferenceScheduleId: "sch1",
            LatestScheduleOriginAuthorizedRun: null,
            EvidenceWindowStartUtc: DateTimeOffset.Parse("2025-01-01T00:00:00Z"));

    private static AttentionNotificationOptions SmtpOptionsStub() =>
        new()
        {
            Enabled = true,
            SmtpHost = "smtp.example",
            FromAddress = "from@example.com",
        };

    private static Mock<IAttentionRecipientResolver> Resolver(
        IReadOnlyList<string> primary,
        string primarySource = AttentionRecipientSources.GlobalDefault,
        bool enabled = true,
        TimeSpan? reminderInterval = null,
        TimeSpan? escalationAfter = null,
        IReadOnlyList<string>? escalation = null,
        string escalationSource = AttentionRecipientSources.GlobalDefault,
        bool sendResolved = true,
        bool suppressRemAck = true,
        bool suppressEscAck = true)
    {
        escalation ??= Array.Empty<string>();
        var hasPrimary = primary.Count > 0;
        var hasEsc = escalation.Count > 0;
        var hasRoute = hasPrimary || hasEsc;
        var m = new Mock<IAttentionRecipientResolver>();
        m.Setup(x => x.ResolveAsync(It.IsAny<IntegrationId>(), AttentionConditionKeys.ExpectedRunMissing, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new AttentionRecipientResolution(
                    HasRoute: hasRoute,
                    Enabled: enabled,
                    PrimaryRecipients: primary,
                    PrimarySource: primarySource,
                    EscalationRecipients: escalation,
                    EscalationSource: escalationSource,
                    PrimaryContactGroupId: hasPrimary ? "grp_primary" : null,
                    PrimaryContactGroupName: hasPrimary ? "Primary group" : null,
                    EscalationContactGroupId: hasEsc ? "grp_esc" : null,
                    EscalationContactGroupName: hasEsc ? "Escalation group" : null,
                    HasPrimaryRoute: hasPrimary,
                    HasEscalationRoute: hasEsc,
                    PrimaryNoRouteReason: hasPrimary ? null : AttentionNoRouteReasons.NoGroupConfigured,
                    EscalationNoRouteReason: hasEsc ? null : AttentionNoRouteReasons.NoGroupConfigured,
                    EscalationAfter: escalationAfter,
                    ReminderInterval: reminderInterval,
                    SendResolvedNotification: sendResolved,
                    SuppressRemindersWhenAcknowledged: suppressRemAck,
                    SuppressEscalationWhenAcknowledged: suppressEscAck,
                    IntegrationRowPrimaryContactGroupId: null,
                    IntegrationRowEscalationContactGroupId: null));
        return m;
    }

    private static void DefaultLedgerEscalation(Mock<IAttentionIncidentNotificationLedger> ledger)
    {
        ledger.Setup(x => x.HasNonSuppressedEscalationAttemptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    [Fact]
    public async Task Opened_sends_activation_per_recipient_on_success()
    {
        var t0 = DateTimeOffset.Parse("2025-03-01T10:00:00Z");
        var clock = new MutableUtcTimeProvider { UtcNowValue = t0 };
        var ledger = new Mock<IAttentionIncidentNotificationLedger>();
        DefaultLedgerEscalation(ledger);
        ledger.Setup(x => x.HasNonSuppressedActivationAttemptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sender = new Mock<IAttentionNotificationSender>();
        sender.Setup(x => x.SendEmailAsync(It.IsAny<AttentionNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttentionNotificationSendResult(true, null, null));
        var tracker = new Mock<IAttentionIncidentTracker>();
        var resolver = Resolver(new[] { "a@test.com", "b@test.com" });

        var sut = new AttentionIncidentNotificationOrchestrator(
            ledger.Object,
            sender.Object,
            tracker.Object,
            resolver.Object,
            Options.Create(SmtpOptionsStub()),
            clock);

        await sut.HandleObserveResultAsync(
            IntId,
            new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.Opened, "inc1"),
            Snapshot(ScheduleExpectationEvaluationStatus.MissingExpectedRun),
            CancellationToken.None);

        sender.Verify(
            x => x.SendEmailAsync(It.IsAny<AttentionNotificationSendRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        ledger.Verify(x => x.AppendAsync(It.IsAny<AttentionIncidentNotificationWrite>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ObservedStillActive_does_not_resend_activation()
    {
        var t0 = DateTimeOffset.Parse("2025-03-01T10:00:00Z");
        var clock = new MutableUtcTimeProvider { UtcNowValue = t0 };
        var ledger = new Mock<IAttentionIncidentNotificationLedger>();
        DefaultLedgerEscalation(ledger);
        ledger.Setup(x => x.HasNonSuppressedActivationAttemptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sender = new Mock<IAttentionNotificationSender>();
        sender.Setup(x => x.SendEmailAsync(It.IsAny<AttentionNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttentionNotificationSendResult(true, null, null));
        var tracker = new Mock<IAttentionIncidentTracker>();

        var resolver = Resolver(new[] { "a@test.com" });
        var sut = new AttentionIncidentNotificationOrchestrator(
            ledger.Object,
            sender.Object,
            tracker.Object,
            resolver.Object,
            Options.Create(SmtpOptionsStub()),
            clock);

        var snap = Snapshot(ScheduleExpectationEvaluationStatus.MissingExpectedRun);
        await sut.HandleObserveResultAsync(
            IntId,
            new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.Opened, "inc1"),
            snap,
            CancellationToken.None);

        ledger.Setup(x => x.HasNonSuppressedActivationAttemptAsync("inc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await sut.HandleObserveResultAsync(
            IntId,
            new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.ObservedStillActive, "inc1"),
            snap,
            CancellationToken.None);

        sender.Verify(
            x => x.SendEmailAsync(It.IsAny<AttentionNotificationSendRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Opened_no_recipients_persists_suppressed_without_sending()
    {
        var t0 = DateTimeOffset.Parse("2025-03-01T10:00:00Z");
        var clock = new MutableUtcTimeProvider { UtcNowValue = t0 };
        var ledger = new Mock<IAttentionIncidentNotificationLedger>();
        DefaultLedgerEscalation(ledger);
        ledger.Setup(x => x.HasNonSuppressedActivationAttemptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sender = new Mock<IAttentionNotificationSender>();
        var tracker = new Mock<IAttentionIncidentTracker>();

        var resolver = Resolver(Array.Empty<string>(), AttentionRecipientSources.None);

        var sut = new AttentionIncidentNotificationOrchestrator(
            ledger.Object,
            sender.Object,
            tracker.Object,
            resolver.Object,
            Options.Create(SmtpOptionsStub()),
            clock);

        await sut.HandleObserveResultAsync(
            IntId,
            new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.Opened, "inc1"),
            Snapshot(ScheduleExpectationEvaluationStatus.MissingExpectedRun),
            CancellationToken.None);

        sender.Verify(
            x => x.SendEmailAsync(It.IsAny<AttentionNotificationSendRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        ledger.Verify(
            x => x.AppendAsync(
                It.Is<AttentionIncidentNotificationWrite>(w =>
                    w.Status == AttentionNotificationStatuses.Suppressed
                    && w.SuppressionReason == AttentionNotificationSuppressionReasons.NoRouteConfigured),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Resolved_sends_after_succeeded_activation_when_configured()
    {
        var t0 = DateTimeOffset.Parse("2025-03-01T10:00:00Z");
        var clock = new MutableUtcTimeProvider { UtcNowValue = t0 };
        var ledger = new Mock<IAttentionIncidentNotificationLedger>();
        DefaultLedgerEscalation(ledger);
        ledger.Setup(x => x.HasNonSuppressedActivationAttemptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        ledger.Setup(x => x.HasSucceededActivationAsync("inc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sender = new Mock<IAttentionNotificationSender>();
        sender.Setup(x => x.SendEmailAsync(It.IsAny<AttentionNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttentionNotificationSendResult(true, null, null));
        var tracker = new Mock<IAttentionIncidentTracker>();
        var resolver = Resolver(new[] { "a@test.com" });

        var sut = new AttentionIncidentNotificationOrchestrator(
            ledger.Object,
            sender.Object,
            tracker.Object,
            resolver.Object,
            Options.Create(SmtpOptionsStub()),
            clock);

        await sut.HandleObserveResultAsync(
            IntId,
            new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.Resolved, "inc1"),
            Snapshot(ScheduleExpectationEvaluationStatus.Satisfied),
            CancellationToken.None);

        sender.Verify(
            x => x.SendEmailAsync(
                It.Is<AttentionNotificationSendRequest>(r => r.Recipient == "a@test.com"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Reminder_sends_once_when_interval_elapsed_and_not_acknowledged()
    {
        var t0 = DateTimeOffset.Parse("2025-03-01T10:00:00Z");
        var clock = new MutableUtcTimeProvider { UtcNowValue = t0 };
        var ledger = new Mock<IAttentionIncidentNotificationLedger>();
        DefaultLedgerEscalation(ledger);
        ledger.Setup(x => x.HasNonSuppressedActivationAttemptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        ledger.Setup(x => x.GetLatestSuccessfulActivationOrReminderSentUtcAsync("inc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(t0);
        var sender = new Mock<IAttentionNotificationSender>();
        sender.Setup(x => x.SendEmailAsync(It.IsAny<AttentionNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttentionNotificationSendResult(true, null, null));
        var tracker = new Mock<IAttentionIncidentTracker>();
        tracker.Setup(x => x.GetIncidentAsync(IntId, "inc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new AttentionIncidentProjection(
                    "inc1",
                    IntId.Value,
                    AttentionConditionKeys.ExpectedRunMissing,
                    AttentionIncidentStatuses.Active,
                    t0,
                    t0,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null));

        var resolver = Resolver(new[] { "a@test.com" }, reminderInterval: TimeSpan.FromHours(1));
        var sut = new AttentionIncidentNotificationOrchestrator(
            ledger.Object,
            sender.Object,
            tracker.Object,
            resolver.Object,
            Options.Create(SmtpOptionsStub()),
            clock);

        clock.UtcNowValue = t0.AddHours(2);
        await sut.HandleObserveResultAsync(
            IntId,
            new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.ObservedStillActive, "inc1"),
            Snapshot(ScheduleExpectationEvaluationStatus.MissingExpectedRun),
            CancellationToken.None);

        sender.Verify(
            x => x.SendEmailAsync(It.IsAny<AttentionNotificationSendRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Reminder_suppressed_when_incident_acknowledged()
    {
        var t0 = DateTimeOffset.Parse("2025-03-01T10:00:00Z");
        var clock = new MutableUtcTimeProvider { UtcNowValue = t0.AddHours(2) };
        var ledger = new Mock<IAttentionIncidentNotificationLedger>();
        DefaultLedgerEscalation(ledger);
        ledger.Setup(x => x.GetLatestSuccessfulActivationOrReminderSentUtcAsync("inc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(t0);
        var sender = new Mock<IAttentionNotificationSender>();
        var tracker = new Mock<IAttentionIncidentTracker>();
        tracker.Setup(x => x.GetIncidentAsync(IntId, "inc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new AttentionIncidentProjection(
                    "inc1",
                    IntId.Value,
                    AttentionConditionKeys.ExpectedRunMissing,
                    AttentionIncidentStatuses.Active,
                    t0,
                    t0,
                    null,
                    null,
                    null,
                    null,
                    t0.AddMinutes(5),
                    "actor",
                    "note"));

        var resolver = Resolver(new[] { "a@test.com" }, reminderInterval: TimeSpan.FromHours(1));
        var sut = new AttentionIncidentNotificationOrchestrator(
            ledger.Object,
            sender.Object,
            tracker.Object,
            resolver.Object,
            Options.Create(SmtpOptionsStub()),
            clock);

        await sut.HandleObserveResultAsync(
            IntId,
            new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.ObservedStillActive, "inc1"),
            Snapshot(ScheduleExpectationEvaluationStatus.MissingExpectedRun),
            CancellationToken.None);

        sender.Verify(
            x => x.SendEmailAsync(It.IsAny<AttentionNotificationSendRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Sender_failure_persists_failed_row()
    {
        var t0 = DateTimeOffset.Parse("2025-03-01T10:00:00Z");
        var clock = new MutableUtcTimeProvider { UtcNowValue = t0 };
        var ledger = new Mock<IAttentionIncidentNotificationLedger>();
        DefaultLedgerEscalation(ledger);
        ledger.Setup(x => x.HasNonSuppressedActivationAttemptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sender = new Mock<IAttentionNotificationSender>();
        sender.Setup(x => x.SendEmailAsync(It.IsAny<AttentionNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttentionNotificationSendResult(false, null, "boom"));
        var tracker = new Mock<IAttentionIncidentTracker>();
        var resolver = Resolver(new[] { "a@test.com" });

        var sut = new AttentionIncidentNotificationOrchestrator(
            ledger.Object,
            sender.Object,
            tracker.Object,
            resolver.Object,
            Options.Create(SmtpOptionsStub()),
            clock);

        await sut.HandleObserveResultAsync(
            IntId,
            new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.Opened, "inc1"),
            Snapshot(ScheduleExpectationEvaluationStatus.MissingExpectedRun),
            CancellationToken.None);

        ledger.Verify(
            x => x.AppendAsync(
                It.Is<AttentionIncidentNotificationWrite>(w =>
                    w.Status == AttentionNotificationStatuses.Failed && w.FailureSummary == "boom"
                    && w.ContactGroupId == "grp_primary"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
    [Fact]
    public async Task Escalation_sends_to_escalation_recipients_after_threshold_from_started_at()
    {
        var started = DateTimeOffset.Parse("2025-03-01T08:00:00Z");
        var now = DateTimeOffset.Parse("2025-03-01T10:30:00Z");
        var clock = new MutableUtcTimeProvider { UtcNowValue = now };
        var ledger = new Mock<IAttentionIncidentNotificationLedger>();
        DefaultLedgerEscalation(ledger);
        var sender = new Mock<IAttentionNotificationSender>();
        sender.Setup(x => x.SendEmailAsync(It.IsAny<AttentionNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttentionNotificationSendResult(true, null, null));
        var tracker = new Mock<IAttentionIncidentTracker>();
        tracker.Setup(x => x.GetIncidentAsync(IntId, "inc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new AttentionIncidentProjection(
                    "inc1",
                    IntId.Value,
                    AttentionConditionKeys.ExpectedRunMissing,
                    AttentionIncidentStatuses.Active,
                    started,
                    now,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null));

        var resolver = Resolver(
            new[] { "primary@test.com" },
            escalationAfter: TimeSpan.FromHours(2),
            escalation: new[] { "esc@test.com" });

        var sut = new AttentionIncidentNotificationOrchestrator(
            ledger.Object,
            sender.Object,
            tracker.Object,
            resolver.Object,
            Options.Create(SmtpOptionsStub()),
            clock);

        await sut.HandleObserveResultAsync(
            IntId,
            new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.ObservedStillActive, "inc1"),
            Snapshot(ScheduleExpectationEvaluationStatus.MissingExpectedRun),
            CancellationToken.None);

        sender.Verify(
            x => x.SendEmailAsync(
                It.Is<AttentionNotificationSendRequest>(r => r.Recipient == "esc@test.com"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        ledger.Verify(
            x => x.AppendAsync(
                It.Is<AttentionIncidentNotificationWrite>(w =>
                    w.WasEscalation && w.EscalationThresholdApplied && w.RecipientResolvedFrom == AttentionRecipientSources.GlobalDefault),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Escalation_skipped_when_acknowledged_and_policy_suppresses()
    {
        var started = DateTimeOffset.Parse("2025-03-01T08:00:00Z");
        var now = DateTimeOffset.Parse("2025-03-01T10:30:00Z");
        var clock = new MutableUtcTimeProvider { UtcNowValue = now };
        var ledger = new Mock<IAttentionIncidentNotificationLedger>();
        DefaultLedgerEscalation(ledger);
        var sender = new Mock<IAttentionNotificationSender>();
        var tracker = new Mock<IAttentionIncidentTracker>();
        tracker.Setup(x => x.GetIncidentAsync(IntId, "inc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new AttentionIncidentProjection(
                    "inc1",
                    IntId.Value,
                    AttentionConditionKeys.ExpectedRunMissing,
                    AttentionIncidentStatuses.Active,
                    started,
                    now,
                    null,
                    null,
                    null,
                    null,
                    started.AddMinutes(1),
                    "actor",
                    "note"));

        var resolver = Resolver(
            new[] { "primary@test.com" },
            escalationAfter: TimeSpan.FromHours(2),
            escalation: new[] { "esc@test.com" },
            suppressEscAck: true);

        var sut = new AttentionIncidentNotificationOrchestrator(
            ledger.Object,
            sender.Object,
            tracker.Object,
            resolver.Object,
            Options.Create(SmtpOptionsStub()),
            clock);

        await sut.HandleObserveResultAsync(
            IntId,
            new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.ObservedStillActive, "inc1"),
            Snapshot(ScheduleExpectationEvaluationStatus.MissingExpectedRun),
            CancellationToken.None);

        sender.Verify(
            x => x.SendEmailAsync(It.IsAny<AttentionNotificationSendRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
