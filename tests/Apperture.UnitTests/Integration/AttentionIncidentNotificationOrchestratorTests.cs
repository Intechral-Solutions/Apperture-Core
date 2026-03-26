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

    private static AttentionNotificationOptions EnabledWithRecipients(params string[] emails) =>
        new()
        {
            Enabled = true,
            Recipients = emails.ToList(),
            SendResolvedNotification = true,
            SmtpHost = "smtp.example",
            FromAddress = "from@example.com",
        };

    [Fact]
    public async Task Opened_sends_activation_per_recipient_on_success()
    {
        var t0 = DateTimeOffset.Parse("2025-03-01T10:00:00Z");
        var clock = new MutableUtcTimeProvider { UtcNowValue = t0 };
        var ledger = new Mock<IAttentionIncidentNotificationLedger>();
        ledger.Setup(x => x.HasNonSuppressedActivationAttemptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sender = new Mock<IAttentionNotificationSender>();
        sender.Setup(x => x.SendEmailAsync(It.IsAny<AttentionNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttentionNotificationSendResult(true, null, null));
        var tracker = new Mock<IAttentionIncidentTracker>();

        var sut = new AttentionIncidentNotificationOrchestrator(
            ledger.Object,
            sender.Object,
            tracker.Object,
            Options.Create(EnabledWithRecipients("a@test.com", "b@test.com")),
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
        ledger.Setup(x => x.HasNonSuppressedActivationAttemptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sender = new Mock<IAttentionNotificationSender>();
        sender.Setup(x => x.SendEmailAsync(It.IsAny<AttentionNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttentionNotificationSendResult(true, null, null));
        var tracker = new Mock<IAttentionIncidentTracker>();

        var opts = EnabledWithRecipients("a@test.com");
        var sut = new AttentionIncidentNotificationOrchestrator(
            ledger.Object,
            sender.Object,
            tracker.Object,
            Options.Create(opts),
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
        ledger.Setup(x => x.HasNonSuppressedActivationAttemptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sender = new Mock<IAttentionNotificationSender>();
        var tracker = new Mock<IAttentionIncidentTracker>();

        var opts = EnabledWithRecipients();
        opts.Recipients.Clear();

        var sut = new AttentionIncidentNotificationOrchestrator(
            ledger.Object,
            sender.Object,
            tracker.Object,
            Options.Create(opts),
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
                    && w.SuppressionReason == AttentionNotificationSuppressionReasons.NoRecipientsConfigured),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Resolved_sends_after_succeeded_activation_when_configured()
    {
        var t0 = DateTimeOffset.Parse("2025-03-01T10:00:00Z");
        var clock = new MutableUtcTimeProvider { UtcNowValue = t0 };
        var ledger = new Mock<IAttentionIncidentNotificationLedger>();
        ledger.Setup(x => x.HasNonSuppressedActivationAttemptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        ledger.Setup(x => x.HasSucceededActivationAsync("inc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sender = new Mock<IAttentionNotificationSender>();
        sender.Setup(x => x.SendEmailAsync(It.IsAny<AttentionNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttentionNotificationSendResult(true, null, null));
        var tracker = new Mock<IAttentionIncidentTracker>();

        var sut = new AttentionIncidentNotificationOrchestrator(
            ledger.Object,
            sender.Object,
            tracker.Object,
            Options.Create(EnabledWithRecipients("a@test.com")),
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

        var opts = EnabledWithRecipients("a@test.com");
        opts.ReminderInterval = TimeSpan.FromHours(1);
        var sut = new AttentionIncidentNotificationOrchestrator(
            ledger.Object,
            sender.Object,
            tracker.Object,
            Options.Create(opts),
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

        var opts = EnabledWithRecipients("a@test.com");
        opts.ReminderInterval = TimeSpan.FromHours(1);
        var sut = new AttentionIncidentNotificationOrchestrator(
            ledger.Object,
            sender.Object,
            tracker.Object,
            Options.Create(opts),
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
        ledger.Setup(x => x.HasNonSuppressedActivationAttemptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sender = new Mock<IAttentionNotificationSender>();
        sender.Setup(x => x.SendEmailAsync(It.IsAny<AttentionNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttentionNotificationSendResult(false, null, "boom"));
        var tracker = new Mock<IAttentionIncidentTracker>();

        var sut = new AttentionIncidentNotificationOrchestrator(
            ledger.Object,
            sender.Object,
            tracker.Object,
            Options.Create(EnabledWithRecipients("a@test.com")),
            clock);

        await sut.HandleObserveResultAsync(
            IntId,
            new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.Opened, "inc1"),
            Snapshot(ScheduleExpectationEvaluationStatus.MissingExpectedRun),
            CancellationToken.None);

        ledger.Verify(
            x => x.AppendAsync(
                It.Is<AttentionIncidentNotificationWrite>(w =>
                    w.Status == AttentionNotificationStatuses.Failed && w.FailureSummary == "boom"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

