using System.Text.Json;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Notifications;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.ControlPlane.Security;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.Notifications.Diagnostics;

public sealed class NotificationPipelineTestServiceTests
{
    [Fact]
    public async Task Smtp_test_succeeds_when_channel_sends()
    {
        var channel = new Mock<IEmailNotificationChannel>();
        channel.Setup(c => c.SendAsync(It.IsAny<EmailNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailNotificationSendResult(true, "msg-1", null));

        var sut = CreateSut(emailChannel: channel.Object);
        var result = await sut.SendSmtpTestEmailAsync(new NotificationSmtpTestRequest("ops@example.com"));

        result.Succeeded.Should().BeTrue();
        channel.Verify(
            c => c.SendAsync(
                It.Is<EmailNotificationSendRequest>(r => r.Recipient == "ops@example.com" && r.HtmlBody != null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Smtp_test_surfaces_channel_failure()
    {
        var channel = new Mock<IEmailNotificationChannel>();
        channel.Setup(c => c.SendAsync(It.IsAny<EmailNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailNotificationSendResult(false, null, "smtp_not_configured"));

        var sut = CreateSut(emailChannel: channel.Object);
        var result = await sut.SendSmtpTestEmailAsync(new NotificationSmtpTestRequest("ops@example.com"));

        result.Succeeded.Should().BeFalse();
        result.FailureSummary.Should().Be("smtp_not_configured");
    }

    [Fact]
    public async Task Platform_pipeline_dry_run_evaluates_without_emitting_event()
    {
        var emitter = new Mock<IPlatformEventEmitter>();
        var definition = ExpectedRunMissingNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);
        var definitions = new Mock<INotificationDefinitionStore>();
        definitions.Setup(d => d.GetAsync(It.IsAny<NotificationDefinitionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);

        var evaluator = new Mock<INotificationRuleEvaluator>();
        evaluator.Setup(e => e.EvaluateDefinitionAsync(
                definition,
                It.IsAny<NotificationRuleEvaluationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationRuleEvaluationResult(
                definition.Id.Value,
                NotificationMatchStatus.Matched,
                NotificationKinds.Activation,
                NotificationKinds.Activation,
                null,
                []));

        var recipients = new Mock<INotificationRecipientResolver>();
        recipients.Setup(r => r.ResolvePreviewAsync(
                definition.Id.Value,
                "int_test",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationRecipientResolutionPreview(
                [new ResolvedNotificationRecipient(
                    "ops@example.com",
                    "Ops",
                    "user_1",
                    null,
                    NotificationUsabilityStatusValues.Deliverable,
                    NotificationRecipientSources.DirectSubscription,
                    "sub_1",
                    NotificationRecipientKinds.Principal)],
                [],
                []));

        var sut = CreateSut(
            definitions: definitions.Object,
            evaluator: evaluator.Object,
            recipients: recipients.Object,
            emitter: emitter.Object);

        var result = await sut.RunPlatformPipelineTestAsync(
            new PlatformNotificationPipelineTestRequest("int_test", DryRun: true));

        result.DryRun.Should().BeTrue();
        result.Stages.Should().Contain(s =>
            s.Stage == NotificationPipelineTestStages.RuleEvaluation && s.Status == NotificationPipelineTestStageStatuses.Passed);
        result.Stages.Should().Contain(s =>
            s.Stage == NotificationPipelineTestStages.PlatformEventEmit && s.Status == NotificationPipelineTestStageStatuses.Skipped);
        result.DeliverableRecipients.Should().HaveCount(1);
        emitter.Verify(e => e.EmitAsync(It.IsAny<PlatformEventEmitRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Platform_pipeline_live_emits_event_and_reports_blocked_recipients()
    {
        var emitter = new Mock<IPlatformEventEmitter>();
        var eventId = PlatformEventId.New();
        emitter.Setup(e => e.EmitAsync(It.IsAny<PlatformEventEmitRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformEventEnvelope(
                eventId,
                PlatformEventTypes.ScheduleExpectedRunMissing,
                PlatformEventSources.PlatformReadiness,
                "1.0",
                "application/json",
                "{}",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                PlatformEventSeverities.Warning,
                null,
                null,
                "int_test",
                null,
                null,
                null,
                null,
                null,
                null,
                null));

        var definition = ExpectedRunMissingNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);
        var definitions = new Mock<INotificationDefinitionStore>();
        definitions.Setup(d => d.GetAsync(It.IsAny<NotificationDefinitionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);

        var evaluator = new Mock<INotificationRuleEvaluator>();
        evaluator.Setup(e => e.EvaluateDefinitionAsync(
                definition,
                It.IsAny<NotificationRuleEvaluationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationRuleEvaluationResult(
                definition.Id.Value,
                NotificationMatchStatus.Matched,
                NotificationKinds.Activation,
                NotificationKinds.Activation,
                null,
                []));

        var recipients = new Mock<INotificationRecipientResolver>();
        recipients.Setup(r => r.ResolvePreviewAsync(
                definition.Id.Value,
                "int_test",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationRecipientResolutionPreview([], [], []));

        var instances = new Mock<INotificationInstanceStore>();
        instances.Setup(i => i.ListAsync(It.IsAny<NotificationInstanceListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateSut(
            definitions: definitions.Object,
            evaluator: evaluator.Object,
            recipients: recipients.Object,
            emitter: emitter.Object,
            instances: instances.Object);

        var result = await sut.RunPlatformPipelineTestAsync(
            new PlatformNotificationPipelineTestRequest("int_test", DryRun: false));

        result.PlatformEventId.Should().Be(eventId.Value);
        result.Stages.Should().Contain(s =>
            s.Stage == NotificationPipelineTestStages.RecipientResolution
            && s.Status == NotificationPipelineTestStageStatuses.Blocked);
        result.Stages.Should().Contain(s =>
            s.Stage == NotificationPipelineTestStages.InstanceCreation
            && s.Status == NotificationPipelineTestStageStatuses.Failed);
    }

    [Fact]
    public async Task Platform_pipeline_fails_configuration_when_instances_disabled()
    {
        var sut = CreateSut(instancesEnabled: false);
        var result = await sut.RunPlatformPipelineTestAsync(
            new PlatformNotificationPipelineTestRequest("int_test", DryRun: true));

        result.Stages.Should().Contain(s =>
            s.Stage == NotificationPipelineTestStages.Configuration
            && s.Status == NotificationPipelineTestStageStatuses.Failed);
    }

    private static NotificationPipelineTestService CreateSut(
        IEmailNotificationChannel? emailChannel = null,
        INotificationDefinitionStore? definitions = null,
        INotificationRuleEvaluator? evaluator = null,
        INotificationRecipientResolver? recipients = null,
        IPlatformEventEmitter? emitter = null,
        INotificationInstanceStore? instances = null,
        bool instancesEnabled = true,
        RecordingAuditEventWriter? events = null,
        INotificationDeliveryOutboxStore? outbox = null,
        bool deliveryEnabled = true,
        bool emailEnabled = true)
    {
        var auth = new NotificationDefinitionAuthorization(
            new FixedPrincipalContext(new StubActor("user:admin", "Admin")),
            new AllowAuth(),
            new AllowAuthEval(),
            new StubIntegrationRepo());

        var dispatch = new NotificationDispatchProcessor(
            outbox ?? Mock.Of<INotificationDeliveryOutboxStore>(),
            instances ?? Mock.Of<INotificationInstanceStore>(),
            emailChannel ?? Mock.Of<IEmailNotificationChannel>(),
            Options.Create(new NotificationDeliveryOptions { Enabled = deliveryEnabled, MaxAttempts = 5, BatchSize = 10 }),
            TimeProvider.System,
            NullLogger<NotificationDispatchProcessor>.Instance);

        return new NotificationPipelineTestService(
            auth,
            Options.Create(new PlatformEventsOptions { Enabled = true }),
            Options.Create(new NotificationEvaluationOptions { Enabled = true }),
            Options.Create(new NotificationInstanceOptions { Enabled = instancesEnabled }),
            Options.Create(new NotificationDeliveryOptions { Enabled = deliveryEnabled }),
            Options.Create(new EmailNotificationOptions
            {
                Enabled = emailEnabled,
                SmtpHost = "smtp.example.com",
                FromAddress = "noreply@example.com",
            }),
            definitions ?? Mock.Of<INotificationDefinitionStore>(),
            evaluator ?? Mock.Of<INotificationRuleEvaluator>(),
            recipients ?? Mock.Of<INotificationRecipientResolver>(),
            emitter ?? Mock.Of<IPlatformEventEmitter>(),
            instances ?? Mock.Of<INotificationInstanceStore>(),
            dispatch,
            emailChannel ?? Mock.Of<IEmailNotificationChannel>(),
            events ?? new RecordingAuditEventWriter(),
            TimeProvider.System,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ShellUi:ManagementHostUrl"] = "http://localhost:9001",
            }).Build(),
            NullLogger<NotificationPipelineTestService>.Instance);
    }

    // ---- AUD-1.4 slice 6: the live sends are recorded as external attempts ----

    private const string RecipientSentinel = "private.recipient@example.com";
    private const string ErrorSentinel = "550 5.1.1 <private.recipient@example.com> user unknown at mx.internal.example";

    private static Mock<IEmailNotificationChannel> ChannelReturning(EmailNotificationSendResult result)
    {
        var channel = new Mock<IEmailNotificationChannel>();
        channel.Setup(c => c.SendAsync(It.IsAny<EmailNotificationSendRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(result);
        return channel;
    }

    private static string Everything(RecordingAuditEventWriter events) =>
        JsonSerializer.Serialize(events.Written.Select(w => new { w.Event.Action, w.Event.Target, w.Event.OutcomeCode, w.Event.Fields }));

    [Theory]
    [InlineData(EmailSendDisposition.Accepted, "accepted", AuditOutcomeCodes.Succeeded)]
    [InlineData(EmailSendDisposition.Rejected, "rejected", AuditOutcomeCodes.Failed)]
    [InlineData(EmailSendDisposition.Failed, "failed", AuditOutcomeCodes.Failed)]
    [InlineData(EmailSendDisposition.Cancelled, "cancelled", AuditOutcomeCodes.Unknown)]
    public async Task A_live_smtp_test_records_what_the_transport_reported_and_never_delivery(EmailSendDisposition disposition, string result, string outcome)
    {
        var events = new RecordingAuditEventWriter();
        var channel = ChannelReturning(new EmailNotificationSendResult(disposition == EmailSendDisposition.Accepted, null, ErrorSentinel, disposition));
        var sut = CreateSut(emailChannel: channel.Object, events: events);

        await sut.SendSmtpTestEmailAsync(new NotificationSmtpTestRequest(RecipientSentinel));

        events.Actions.Should().Equal(AuditActionNames.NotificationSmtpTestAttempted);
        var written = events.Written.Single();
        written.Event.OutcomeCode.Should().Be(outcome);
        written.Event.Fields["result"].Should().Be(result);
        written.Event.Definition.Basis.Should().Be(AuditBases.ExternalAttempt);
        written.BestEffort.Should().BeTrue("a failed write of the evidence never changes the response");
        written.Actor.IsCurrentPrincipal.Should().BeTrue("the operator who asked, not a caller-supplied identity");
        Everything(events).Should().NotContain(RecipientSentinel).And.NotContain("mx.internal.example").And.NotContain("user unknown");
    }

    [Fact]
    public async Task A_channel_that_does_not_state_a_disposition_is_read_from_its_success_flag()
    {
        var events = new RecordingAuditEventWriter();
        var sut = CreateSut(emailChannel: ChannelReturning(new EmailNotificationSendResult(true, "msg-1", null)).Object, events: events);

        await sut.SendSmtpTestEmailAsync(new NotificationSmtpTestRequest("ops@example.com"));

        events.Single(AuditActionNames.NotificationSmtpTestAttempted).Fields["result"].Should().Be("accepted");
    }

    [Fact]
    public async Task A_smtp_test_that_never_reached_the_transport_is_a_refusal_not_an_attempt()
    {
        var events = new RecordingAuditEventWriter();
        var notConfigured = ChannelReturning(new EmailNotificationSendResult(false, null, "smtp_not_configured", EmailSendDisposition.NotAttempted));
        var sut = CreateSut(emailChannel: notConfigured.Object, events: events);

        var configured = await sut.SendSmtpTestEmailAsync(new NotificationSmtpTestRequest("ops@example.com"));
        var blank = await sut.SendSmtpTestEmailAsync(new NotificationSmtpTestRequest("  "));

        configured.Succeeded.Should().BeFalse();
        blank.Succeeded.Should().BeFalse();
        events.Actions.Should().Equal(AuditActionNames.NotificationDiagnosticsTestRefused, AuditActionNames.NotificationDiagnosticsTestRefused);
        events.Written[0].Event.Fields["reason"].Should().Be("not_configured");
        events.Written[1].Event.Fields["reason"].Should().Be("invalid_input");
        events.Written.Should().OnlyContain(w => w.Event.OutcomeCode == AuditOutcomeCodes.Rejected && w.Event.Fields["test"].Equals("smtp"));
        notConfigured.Verify(c => c.SendAsync(It.IsAny<EmailNotificationSendRequest>(), It.IsAny<CancellationToken>()), Times.Once, "the blank recipient never reaches the channel");
    }

    [Fact]
    public async Task A_channel_that_throws_is_recorded_as_failed_or_cancelled_and_the_exception_still_propagates()
    {
        var events = new RecordingAuditEventWriter();
        var channel = new Mock<IEmailNotificationChannel>();
        channel.SetupSequence(c => c.SendAsync(It.IsAny<EmailNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException())
            .ThrowsAsync(new InvalidOperationException(ErrorSentinel));
        var sut = CreateSut(emailChannel: channel.Object, events: events);

        await FluentActions.Awaiting(() => sut.SendSmtpTestEmailAsync(new NotificationSmtpTestRequest("ops@example.com")).AsTask()).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => sut.SendSmtpTestEmailAsync(new NotificationSmtpTestRequest("ops@example.com")).AsTask()).Should().ThrowAsync<InvalidOperationException>();

        events.Written.Select(w => (w.Event.Fields["result"], w.Event.OutcomeCode)).Should().Equal(
            ("cancelled", AuditOutcomeCodes.Unknown), ("failed", AuditOutcomeCodes.Failed));
        Everything(events).Should().NotContain("mx.internal.example");
    }

    [Fact]
    public async Task A_failed_evidence_write_does_not_change_the_smtp_test_response()
    {
        var events = new RecordingAuditEventWriter { Fail = true };
        var sut = CreateSut(emailChannel: ChannelReturning(new EmailNotificationSendResult(true, "m", null)).Object, events: events);

        var result = await sut.SendSmtpTestEmailAsync(new NotificationSmtpTestRequest("ops@example.com"));

        result.Succeeded.Should().BeTrue();
        events.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task A_dry_run_records_nothing_even_when_it_is_refused()
    {
        var events = new RecordingAuditEventWriter();
        var sut = CreateSut(instancesEnabled: false, events: events);

        await sut.RunPlatformPipelineTestAsync(new PlatformNotificationPipelineTestRequest("int_test", DryRun: true));

        events.Written.Should().BeEmpty("a dry run persists nothing and sends nothing");
    }

    [Fact]
    public async Task A_live_run_refused_by_configuration_is_a_refusal_row()
    {
        var events = new RecordingAuditEventWriter();
        var sut = CreateSut(instancesEnabled: false, events: events);

        await sut.RunPlatformPipelineTestAsync(new PlatformNotificationPipelineTestRequest("int_test", DryRun: false));

        events.Actions.Should().Equal(AuditActionNames.NotificationDiagnosticsTestRefused);
        events.Written.Single().Event.Fields["test"].Should().Be("pipeline");
        events.Written.Single().Event.Fields["reason"].Should().Be("not_configured");
    }

    [Fact]
    public async Task A_live_run_for_a_missing_definition_is_a_refusal_row()
    {
        var events = new RecordingAuditEventWriter();
        var definitions = new Mock<INotificationDefinitionStore>();
        definitions.Setup(d => d.GetAsync(It.IsAny<NotificationDefinitionId>(), It.IsAny<CancellationToken>())).ReturnsAsync((NotificationDefinition?)null);
        var sut = CreateSut(definitions: definitions.Object, events: events);

        await sut.RunPlatformPipelineTestAsync(new PlatformNotificationPipelineTestRequest("int_test", DryRun: false));

        events.Single(AuditActionNames.NotificationDiagnosticsTestRefused).Fields["reason"].Should().Be("no_definition");
    }

    private sealed record LiveRun(
        NotificationPipelineTestService Sut,
        RecordingAuditEventWriter Events,
        Mock<INotificationDeliveryOutboxStore> Outbox,
        Mock<IEmailNotificationChannel> Channel,
        NotificationInstanceId InstanceId);

    /// <summary>A live run that persisted one instance with <paramref name="rows"/> pending outbox rows, dispatched by the real processor.</summary>
    private static LiveRun ArrangeLive(Func<int, EmailNotificationSendResult> sendFor, int rows = 2, bool deliveryEnabled = true, bool dispatchTrueRows = true)
    {
        var events = new RecordingAuditEventWriter();
        var definition = ExpectedRunMissingNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);
        var definitions = new Mock<INotificationDefinitionStore>();
        definitions.Setup(d => d.GetAsync(It.IsAny<NotificationDefinitionId>(), It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        var evaluator = new Mock<INotificationRuleEvaluator>();
        evaluator.Setup(e => e.EvaluateDefinitionAsync(definition, It.IsAny<NotificationRuleEvaluationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationRuleEvaluationResult(definition.Id.Value, NotificationMatchStatus.Matched, NotificationKinds.Activation, NotificationKinds.Activation, null, []));
        var recipients = new Mock<INotificationRecipientResolver>();
        recipients.Setup(r => r.ResolvePreviewAsync(definition.Id.Value, "int_test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationRecipientResolutionPreview([], [], []));
        var emitter = new Mock<IPlatformEventEmitter>();
        emitter.Setup(e => e.EmitAsync(It.IsAny<PlatformEventEmitRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformEventEnvelope(
                PlatformEventId.New(), PlatformEventTypes.ScheduleExpectedRunMissing, PlatformEventSources.PlatformReadiness, "1.0", "application/json", "{}",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, PlatformEventSeverities.Warning, null, null, "int_test", null, null, null, null, null, null, null));

        var instanceId = NotificationInstanceId.From("ninst_1");
        var instance = new NotificationInstance(
            instanceId, definition.Id.Value, NotificationKinds.Activation, NotificationInstanceStatuses.Pending, "evt-1", "diag-incident", "int_test",
            null, null, null, "dedupe", "Subject", "Summary", "Body", null, null, null, null, "hash", "{}", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var outboxRows = Enumerable.Range(0, rows).Select(i => new NotificationDeliveryOutbox(
            NotificationDeliveryOutboxId.From($"nout_{i}"), instanceId, $"recipient{i}@example.com", null, null, null, "email",
            NotificationOutboxStatuses.Pending, 0, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)).ToList();

        var instanceStore = new Mock<INotificationInstanceStore>();
        instanceStore.Setup(i => i.ListAsync(It.IsAny<NotificationInstanceListQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync([instance]);
        instanceStore.Setup(i => i.GetByIdAsync(instanceId, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        instanceStore.Setup(i => i.ListOutboxForInstanceAsync(instanceId, It.IsAny<CancellationToken>())).ReturnsAsync(outboxRows);

        var outbox = new Mock<INotificationDeliveryOutboxStore>();
        outbox.Setup(o => o.ClaimPendingForInstanceAsync(instanceId, It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outboxRows.Select(r => r with { Status = NotificationOutboxStatuses.Delivering }).ToList());
        outbox.Setup(o => o.TryUpdateAfterAttemptAsync(It.IsAny<NotificationDeliveryOutboxUpdateCommand>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var channel = new Mock<IEmailNotificationChannel>();
        var call = 0;
        channel.Setup(c => c.SendAsync(It.IsAny<EmailNotificationSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => sendFor(call++));

        var sut = CreateSut(
            definitions: definitions.Object, evaluator: evaluator.Object, recipients: recipients.Object, emitter: emitter.Object,
            instances: instanceStore.Object, emailChannel: channel.Object, events: events, outbox: outbox.Object, deliveryEnabled: deliveryEnabled);
        return new LiveRun(sut, events, outbox, channel, instanceId);
    }

    private static EmailNotificationSendResult Sent(EmailSendDisposition disposition) =>
        new(disposition == EmailSendDisposition.Accepted, null, disposition == EmailSendDisposition.Accepted ? null : ErrorSentinel, disposition);

    [Fact]
    public async Task A_live_run_that_dispatches_sends_only_its_own_instances_rows_and_records_what_the_transport_reported()
    {
        var run = ArrangeLive(i => Sent(i == 0 ? EmailSendDisposition.Accepted : EmailSendDisposition.Rejected));

        await run.Sut.RunPlatformPipelineTestAsync(new PlatformNotificationPipelineTestRequest("int_test", DryRun: false, DispatchImmediately: true));

        run.Outbox.Verify(o => o.ClaimPendingAsync(It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never,
            "the test never claims the system's oldest pending rows");
        run.Outbox.Verify(o => o.ClaimPendingForInstanceAsync(run.InstanceId, It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        run.Events.Actions.Should().Equal(AuditActionNames.NotificationPipelineTestAttempted);
        var evt = run.Events.Written.Single().Event;
        evt.OutcomeCode.Should().Be(AuditOutcomeCodes.Failed, "one send was refused by the server");
        evt.Fields["dispatchRequested"].Should().Be(true);
        evt.Fields["instancesCreated"].Should().Be(1);
        evt.Fields["outboxRowsCreated"].Should().Be(2);
        evt.Fields["sendsAttempted"].Should().Be(2);
        evt.Fields["sendsAccepted"].Should().Be(1);
        evt.Fields["sendsRejected"].Should().Be(1);
        evt.Fields["sendsFailed"].Should().Be(0);
        evt.Fields["sendsUnknown"].Should().Be(0);
        evt.Target.Type.Should().Be(AuditTargetTypes.NotificationDefinition);
        run.Events.Written.Single().BestEffort.Should().BeTrue();
        Everything(run.Events).Should().NotContain("example.com").And.NotContain("user unknown");
    }

    [Fact]
    public async Task Every_accepted_send_is_a_succeeded_attempt_that_still_does_not_claim_delivery()
    {
        var run = ArrangeLive(_ => Sent(EmailSendDisposition.Accepted));

        await run.Sut.RunPlatformPipelineTestAsync(new PlatformNotificationPipelineTestRequest("int_test", DryRun: false, DispatchImmediately: true));

        var evt = run.Events.Single(AuditActionNames.NotificationPipelineTestAttempted);
        evt.OutcomeCode.Should().Be(AuditOutcomeCodes.Succeeded);
        evt.Fields.Keys.Should().NotContain(k => k.Contains("deliver", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_send_cancelled_before_an_answer_makes_the_outcome_unknown()
    {
        var run = ArrangeLive(i => Sent(i == 0 ? EmailSendDisposition.Accepted : EmailSendDisposition.Cancelled));

        await run.Sut.RunPlatformPipelineTestAsync(new PlatformNotificationPipelineTestRequest("int_test", DryRun: false, DispatchImmediately: true));

        var evt = run.Events.Single(AuditActionNames.NotificationPipelineTestAttempted);
        evt.OutcomeCode.Should().Be(AuditOutcomeCodes.Unknown);
        evt.Fields["sendsUnknown"].Should().Be(1);
        evt.Fields["sendsAccepted"].Should().Be(1);
    }

    [Fact]
    public async Task A_request_cancelled_during_the_dispatch_still_leaves_its_evidence_and_the_cancellation_propagates()
    {
        var run = ArrangeLive(i => i == 0 ? Sent(EmailSendDisposition.Accepted) : throw new OperationCanceledException());

        var act = () => run.Sut.RunPlatformPipelineTestAsync(
            new PlatformNotificationPipelineTestRequest("int_test", DryRun: false, DispatchImmediately: true)).AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>();
        var evt = run.Events.Single(AuditActionNames.NotificationPipelineTestAttempted);
        evt.Fields["sendsAccepted"].Should().Be(1, "the send made before the cancellation is not lost");
        evt.Fields["sendsUnknown"].Should().Be(1, "the send that was cancelled gave no answer");
        evt.Fields["sendsAttempted"].Should().Be(2);
        evt.OutcomeCode.Should().Be(AuditOutcomeCodes.Unknown);
    }

    [Fact]
    public async Task A_live_run_that_does_not_dispatch_records_what_it_persisted_and_claims_a_requested_outcome()
    {
        var run = ArrangeLive(_ => Sent(EmailSendDisposition.Accepted));

        await run.Sut.RunPlatformPipelineTestAsync(new PlatformNotificationPipelineTestRequest("int_test", DryRun: false, DispatchImmediately: false));

        var evt = run.Events.Single(AuditActionNames.NotificationPipelineTestAttempted);
        evt.OutcomeCode.Should().Be(AuditOutcomeCodes.Requested);
        evt.Fields["dispatchRequested"].Should().Be(false);
        evt.Fields["outboxRowsCreated"].Should().Be(2);
        evt.Fields["sendsAttempted"].Should().Be(0);
        run.Channel.Verify(c => c.SendAsync(It.IsAny<EmailNotificationSendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_dispatch_with_delivery_switched_off_sends_nothing_and_is_not_an_attempt()
    {
        var run = ArrangeLive(_ => Sent(EmailSendDisposition.Accepted), deliveryEnabled: false);

        await run.Sut.RunPlatformPipelineTestAsync(new PlatformNotificationPipelineTestRequest("int_test", DryRun: false, DispatchImmediately: true));

        run.Channel.Verify(c => c.SendAsync(It.IsAny<EmailNotificationSendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        var evt = run.Events.Single(AuditActionNames.NotificationPipelineTestAttempted);
        evt.OutcomeCode.Should().Be(AuditOutcomeCodes.Requested);
        evt.Fields["sendsAttempted"].Should().Be(0);
    }

    [Fact]
    public async Task A_failed_evidence_write_does_not_change_the_pipeline_response()
    {
        var run = ArrangeLive(_ => Sent(EmailSendDisposition.Accepted));
        run.Events.Fail = true;

        var result = await run.Sut.RunPlatformPipelineTestAsync(new PlatformNotificationPipelineTestRequest("int_test", DryRun: false, DispatchImmediately: true));

        result.PlatformEventId.Should().NotBeNull();
        run.Events.Written.Should().BeEmpty();
    }

    [Theory]
    [InlineData(typeof(System.Net.Mail.SmtpFailedRecipientException), EmailSendDisposition.Rejected)]
    [InlineData(typeof(System.Net.Sockets.SocketException), EmailSendDisposition.Failed)]
    public void The_transport_tells_a_server_refusal_from_a_transport_error(Type exceptionType, EmailSendDisposition expected)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType, nonPublic: true)!;

        SmtpEmailTransport.Classify(ex, CancellationToken.None).Should().Be(expected);
    }

    [Fact]
    public void A_cancelled_call_is_cancelled_only_when_the_caller_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        SmtpEmailTransport.Classify(new OperationCanceledException(), cts.Token).Should().Be(EmailSendDisposition.Cancelled);
        SmtpEmailTransport.Classify(new OperationCanceledException(), CancellationToken.None).Should().Be(EmailSendDisposition.Failed,
            "a timeout the caller did not ask for is a transport error");
        SmtpEmailTransport.Classify(new InvalidOperationException(), CancellationToken.None).Should().Be(EmailSendDisposition.Failed);
    }

    private sealed class AllowAuth : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? governedResource,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class AllowAuthEval : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class StubIntegrationRepo : IIntegrationRepository
    {
        public ValueTask<DomainIntegration?> GetByIdAsync(IntegrationId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<DomainIntegration?>(
                new DomainIntegration(
                    id,
                    "Test",
                    "mod-1",
                    null,
                    IntegrationState.Active,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow));

        public ValueTask<IReadOnlyList<DomainIntegration>> GetVisibleAsync(string scopeKey, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<DomainIntegration>>([]);

        public ValueTask AddAsync(DomainIntegration integration, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask UpdateAsync(DomainIntegration integration, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<DomainIntegration?> GetByIdForUpdateAsync(IntegrationId id, CancellationToken cancellationToken = default) =>
            GetByIdAsync(id, cancellationToken);

        public ValueTask SetResourceContextIdAsync(IntegrationId id, ResourceContextId resourceContextId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class FixedPrincipalContext : IPrincipalContext
    {
        private readonly IActor _actor;
        public FixedPrincipalContext(IActor actor) => _actor = actor;

        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(_actor);
    }

    private sealed class StubActor : IActor
    {
        public StubActor(string id, string? name)
        {
            Id = new ActorId(id);
            DisplayName = name;
        }

        public ActorId Id { get; }
        public ActorKind Kind => ActorKind.Human;
        public string? DisplayName { get; }
    }
}
